using static XenoAtom.Interop.vulkan;

using static XenoAtom.Graphics.Vk.VulkanUtil;
using System.Collections.Generic;
using System.Diagnostics;
using System;

namespace XenoAtom.Graphics.Vk
{
    internal unsafe class VkDeviceMemoryManager : IDisposable
    {
        private readonly VkDevice _device;
        private readonly VkPhysicalDevice _physicalDevice;
        private readonly VkPhysicalDeviceProperties _physicalDeviceProperties;
        private readonly VkPhysicalDeviceMemoryProperties _memoryProperties;
        private readonly ulong _bufferImageGranularity;
        private readonly uint _maxMemoryAllocationCount;
        private readonly ulong _maxMemoryAllocationSize;
        private readonly object _lock = new object();
        private ulong _totalAllocatedBytes;
        private readonly Dictionary<uint, ChunkAllocatorSet> _allocatorsByMemoryTypeUnmapped = new();
        private readonly Dictionary<uint, ChunkAllocatorSet> _allocatorsByMemoryType = new();
        private readonly ulong _preferredPersistentChunkSize;
        private readonly ulong _preferredChunkSize;

        private const ulong PersistentMappedChunkSize = 1024 * 1024 * 64;
        private const ulong SmallHeapSize = 1024 * 1024 * 1024;
        private const ulong DefaultLargeHeapChunkSize = 1024 * 1024 * 256;
        
        // https://blog.io7m.com/2023/11/11/vulkan-memory-allocation.xhtml

        public VkDeviceMemoryManager(
            VkDevice device,
            VkPhysicalDevice physicalDevice,
            in VkPhysicalDeviceProperties physicalDeviceProperties,
            in VkPhysicalDeviceMemoryProperties memoryProperties)

        {
            _device = device;
            _physicalDevice = physicalDevice;
            _physicalDeviceProperties = physicalDeviceProperties;
            _memoryProperties = memoryProperties;
            _bufferImageGranularity = physicalDeviceProperties.limits.bufferImageGranularity;
            _maxMemoryAllocationCount = physicalDeviceProperties.limits.maxMemoryAllocationCount;
            _preferredPersistentChunkSize = PersistentMappedChunkSize;
            _preferredChunkSize = DefaultLargeHeapChunkSize; // TODO: Allow to make it configurable

            var subProps3 = new VkPhysicalDeviceMaintenance3Properties();
            var memoryProperties2 = new VkPhysicalDeviceProperties2
            {
                pNext = &subProps3
            };
            vkGetPhysicalDeviceProperties2(physicalDevice, ref memoryProperties2);
            _maxMemoryAllocationSize = subProps3.maxMemoryAllocationSize;
        }

        public VkMemoryBlock Allocate(
            uint memoryTypeBits,
            VkMemoryPropertyFlags flags,
            bool persistentMapped,
            ulong size,
            ulong alignment)
        {
            return Allocate(
                memoryTypeBits,
                flags,
                persistentMapped,
                size,
                alignment,
                false,
                default,
                default);
        }

        public VkMemoryBlock Allocate(
            uint memoryTypeBits,
            VkMemoryPropertyFlags flags,
            bool persistentMapped,
            ulong size,
            ulong alignment,
            bool dedicated,
            VkImage dedicatedImage,
            XenoAtom.Interop.vulkan.VkBuffer dedicatedBuffer)
        {
            lock (_lock)
            {
                if (!TryFindMemoryType(memoryTypeBits, flags, out var memoryTypeIndex))
                {
                    throw new GraphicsException("No suitable memory type.");
                }

                if (dedicated)
                {
                    if (dedicatedImage != default)
                    {
                        VkImageMemoryRequirementsInfo2 requirementsInfo = new() { image = dedicatedImage };
                        VkMemoryRequirements2 requirements = new();
                        vkGetImageMemoryRequirements2(_device, requirementsInfo, ref requirements);
                        size = requirements.memoryRequirements.size;
                    }
                    else if (dedicatedBuffer != default)
                    {
                        VkBufferMemoryRequirementsInfo2 requirementsInfo = new() { buffer = dedicatedBuffer };
                        VkMemoryRequirements2 requirements = new();
                        vkGetBufferMemoryRequirements2(_device, requirementsInfo, ref requirements);
                        size = requirements.memoryRequirements.size;
                    }
                }
                else
                {
                    size = ((size / _bufferImageGranularity) + 1) * _bufferImageGranularity;
                }
                _totalAllocatedBytes += size;


                ulong minDedicatedAllocationSize = persistentMapped
                    ? _preferredPersistentChunkSize
                    : _preferredChunkSize;

                if (dedicated || size >= minDedicatedAllocationSize)
                {
                    var allocateInfo = new VkMemoryAllocateInfo
                    {
                        allocationSize = size,
                        memoryTypeIndex = memoryTypeIndex
                    };

                    VkMemoryDedicatedAllocateInfo dedicatedAI;
                    if (dedicated)
                    {
                        dedicatedAI = new VkMemoryDedicatedAllocateInfo
                        {
                            buffer = dedicatedBuffer,
                            image = dedicatedImage
                        };
                        allocateInfo.pNext = &dedicatedAI;
                    }

                    VkResult allocationResult = vkAllocateMemory(_device, allocateInfo, null, out VkDeviceMemory memory);
                    if (allocationResult != VK_SUCCESS)
                    {
                        throw new GraphicsException("Unable to allocate sufficient Vulkan memory.");
                    }

                    void* mappedPtr = null;
                    if (persistentMapped)
                    {
                        VkResult mapResult = vkMapMemory(_device, memory, 0, size, default, &mappedPtr);
                        if (mapResult != VK_SUCCESS)
                        {
                            vkFreeMemory(_device, memory, null);
                            throw new GraphicsException("Unable to map newly-allocated Vulkan memory.");
                        }
                    }

                    return new VkMemoryBlock(memory, 0, size, memoryTypeBits, mappedPtr, true);
                }
                else
                {
                    ChunkAllocatorSet allocator = GetAllocator(memoryTypeIndex, persistentMapped);
                    bool result = allocator.Allocate(size, alignment, out VkMemoryBlock ret);
                    if (!result)
                    {
                        throw new GraphicsException("Unable to allocate sufficient Vulkan memory.");
                    }

                    return ret;
                }
            }
        }

        public bool TryFindMemoryType(uint typeFilter, VkMemoryPropertyFlags properties, out uint typeIndex)
        {
            typeIndex = 0;

            for (int i = 0; i < _memoryProperties.memoryTypeCount; i++)
            {
                if (((typeFilter & (1 << i)) != 0)
                    && (_memoryProperties.GetMemoryType((uint)i).propertyFlags & properties) == properties)
                {
                    typeIndex = (uint)i;
                    return true;
                }
            }

            return false;
        }

        private ulong CalcPreferredChunkSize(uint memTypeIndex)
        {
            var heapIndex = GetMemoryHeapIndexFromTypeIndex(memTypeIndex);
            var heapSize = _memoryProperties.memoryHeaps[(int)heapIndex].size;
            bool isSmallHeap = heapSize <= SmallHeapSize;
            return isSmallHeap ? (heapSize.Value / 8) : _preferredChunkSize;
        }

        private uint GetMemoryHeapIndexFromTypeIndex(uint memTypeIndex)
        {
            Debug.Assert(memTypeIndex < _memoryProperties.memoryTypeCount);
            return _memoryProperties.memoryTypes[(int)memTypeIndex].heapIndex;
        }

        private bool IsMemoryTypeNonCoherent(uint memTypeIndex)
        {
            Debug.Assert(memTypeIndex < _memoryProperties.memoryTypeCount);
            return (_memoryProperties.memoryTypes[(int)memTypeIndex].propertyFlags & (VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT | VK_MEMORY_PROPERTY_HOST_COHERENT_BIT)) ==
                   VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT;
        }

        private ulong GetMemoryTypeMinAlignment(uint memTypeIndex)
        {
            return IsMemoryTypeNonCoherent(memTypeIndex)
                ? Math.Max(1U, (ulong)_physicalDeviceProperties.limits.nonCoherentAtomSize.Value)
                : 1;
        }
        public void Free(VkMemoryBlock block)
        {
            _totalAllocatedBytes -= block.Size;
            lock (_lock)
            {
                if (block.DedicatedAllocation)
                {
                    vkFreeMemory(_device, block.DeviceMemory, null);
                }
                else
                {
                    GetAllocator(block.MemoryTypeIndex, block.IsPersistentMapped).Free(block);
                }
            }
        }

        private ChunkAllocatorSet GetAllocator(uint memoryTypeIndex, bool persistentMapped)
        {
            ChunkAllocatorSet? ret;
            if (persistentMapped)
            {
                if (!_allocatorsByMemoryType.TryGetValue(memoryTypeIndex, out ret))
                {
                    ret = new ChunkAllocatorSet(this, memoryTypeIndex, true);
                    _allocatorsByMemoryType.Add(memoryTypeIndex, ret);
                }
            }
            else
            {
                if (!_allocatorsByMemoryTypeUnmapped.TryGetValue(memoryTypeIndex, out ret))
                {
                    ret = new ChunkAllocatorSet(this, memoryTypeIndex, false);
                    _allocatorsByMemoryTypeUnmapped.Add(memoryTypeIndex, ret);
                }
            }

            return ret;
        }

        private class ChunkAllocatorSet : IDisposable
        {
            private readonly VkDeviceMemoryManager _manager;
            private readonly VkDevice _device;
            private readonly uint _memoryTypeIndex;
            private readonly bool _persistentMapped;
            private readonly List<ChunkAllocator> _allocators = new List<ChunkAllocator>();

            public ChunkAllocatorSet(VkDeviceMemoryManager manager, uint memoryTypeIndex, bool persistentMapped)
            {
                _manager = manager;
                _device = manager._device;
                _memoryTypeIndex = memoryTypeIndex;
                _persistentMapped = persistentMapped;
            }

            public bool Allocate(ulong size, ulong alignment, out VkMemoryBlock block)
            {
                for (int i = 0; i < _allocators.Count; i++)
                {
                    ChunkAllocator allocator = _allocators[i];
                    if (allocator.Allocate(size, alignment, out block))
                    {
                        return true;
                    }

                    // Allocate may merge free blocks.
                    if (allocator.IsFullFreeBlock())
                    {
                        allocator.Dispose();
                        _allocators.RemoveAt(i);
                        i--;
                    }
                }

                ChunkAllocator newAllocator = new ChunkAllocator(_manager, _memoryTypeIndex, _persistentMapped);
                _allocators.Add(newAllocator);
                return newAllocator.Allocate(size, alignment, out block);
            }

            public void Free(VkMemoryBlock block)
            {
                for (int i = 0; i < _allocators.Count; i++)
                {
                    ChunkAllocator allocator = _allocators[i];
                    if (allocator.Memory == block.DeviceMemory)
                    {
                        allocator.Free(block);
                    }
                }
            }

            public void Dispose()
            {
                foreach (ChunkAllocator allocator in _allocators)
                {
                    allocator.Dispose();
                }
            }
        }

        private class ChunkAllocator : IDisposable
        {
            private readonly VkDeviceMemoryManager _manager;
            private readonly VkDevice _device;
            private readonly uint _memoryTypeIndex;
            private readonly bool _persistentMapped;
            private readonly List<VkMemoryBlock> _freeBlocks = new List<VkMemoryBlock>();
            private readonly VkDeviceMemory _memory;
            private readonly void* _mappedPtr;

            private readonly ulong _chunkSize;

            public VkDeviceMemory Memory => _memory;

            public ChunkAllocator(VkDeviceMemoryManager manager, uint memoryTypeIndex, bool persistentMapped)
            {
                _manager = manager;
                _device = manager._device;
                _memoryTypeIndex = memoryTypeIndex;
                _persistentMapped = persistentMapped;
                _chunkSize = persistentMapped ? manager._preferredPersistentChunkSize : manager._preferredChunkSize;

                var memoryAllocateInfo = new VkMemoryAllocateInfo()
                {
                    allocationSize = _chunkSize,
                    memoryTypeIndex = _memoryTypeIndex
                };

                var result = vkAllocateMemory(_device, memoryAllocateInfo, null, out _memory);
                if (result != VK_SUCCESS)
                {
                    result.VkCheck($"Cannot allocate memory {_chunkSize} bytes with memory type index = {memoryTypeIndex}");
                }
                
                void* mappedPtr = null;
                if (persistentMapped)
                {
                    result = vkMapMemory(_device, _memory, 0, _chunkSize, default, &mappedPtr);
                    if (result != VK_SUCCESS)
                    {
                        vkFreeMemory(_device, _memory, null);
                        result.VkCheck("Cannot map memory");
                    }
                }
                _mappedPtr = mappedPtr;

                VkMemoryBlock initialBlock = new VkMemoryBlock(
                    _memory,
                    0,
                    _chunkSize,
                    _memoryTypeIndex,
                    _mappedPtr,
                    false);
                _freeBlocks.Add(initialBlock);
            }

            public bool Allocate(ulong size, ulong alignment, out VkMemoryBlock block)
            {
                checked
                {
                    List<VkMemoryBlock> freeBlocks = _freeBlocks;

                    // Don't try merging blocks if there are none.
                    bool hasMergedBlocks = freeBlocks.Count == 0;

                    do
                    {
                        for (int i = 0; i < freeBlocks.Count; i++)
                        {
                            VkMemoryBlock freeBlock = freeBlocks[i];
                            ulong alignedBlockSize = freeBlock.Size;
                            ulong alignedOffsetRemainder = freeBlock.Offset % alignment;
                            if (alignedOffsetRemainder != 0)
                            {
                                ulong alignmentCorrection = alignment - alignedOffsetRemainder;
                                if (alignedBlockSize <= alignmentCorrection)
                                {
                                    continue;
                                }
                                alignedBlockSize -= alignmentCorrection;
                            }

                            if (alignedBlockSize >= size) // Valid match -- split it and return.
                            {
                                block = freeBlock;
                                block.Size = alignedBlockSize;
                                if (alignedOffsetRemainder != 0)
                                {
                                    block.Offset += alignment - alignedOffsetRemainder;
                                }

                                if (alignedBlockSize != size)
                                {
                                    VkMemoryBlock splitBlock = new VkMemoryBlock(
                                        block.DeviceMemory,
                                        block.Offset + size,
                                        block.Size - size,
                                        _memoryTypeIndex,
                                        block.BaseMappedPointer,
                                        false);

                                    freeBlocks[i] = splitBlock;
                                    block.Size = size;
                                }
                                else
                                {
                                    freeBlocks.RemoveAt(i);
                                }

#if DEBUG
                                CheckAllocatedBlock(block);
#endif
                                return true;
                            }
                        }

                        if (hasMergedBlocks)
                        {
                            break;
                        }
                        hasMergedBlocks = MergeContiguousBlocks();
                    }
                    while (hasMergedBlocks);

                    block = default(VkMemoryBlock);
                    return false;
                }
            }

            private static int FindPrecedingBlockIndex(List<VkMemoryBlock> list, int length, ulong targetOffset)
            {
                int low = 0;
                int high = length - 1;

                if (length == 0 || list[high].Offset < targetOffset)
                    return -1;

                while (low <= high)
                {
                    int mid = low + ((high - low) / 2);

                    if (list[mid].Offset >= targetOffset)
                        high = mid - 1;
                    else
                        low = mid + 1;
                }

                return high + 1;
            }


            public void Free(VkMemoryBlock block)
            {
                // Assume that _freeBlocks is always sorted.
                int precedingBlock = FindPrecedingBlockIndex(_freeBlocks, _freeBlocks.Count, block.Offset);
                if (precedingBlock != -1)
                {
                    _freeBlocks.Insert(precedingBlock, block);
                }
                else
                {
                    _freeBlocks.Add(block);
                }

#if DEBUG
                RemoveAllocatedBlock(block);
#endif
            }

            private bool MergeContiguousBlocks()
            {
                List<VkMemoryBlock> freeBlocks = _freeBlocks;
                bool hasMerged = false;
                int contiguousLength = 1;

                for (int i = 0; i < freeBlocks.Count - 1; i++)
                {
                    ulong blockStart = freeBlocks[i].Offset;
                    while (i + contiguousLength < freeBlocks.Count
                        && freeBlocks[i + contiguousLength - 1].End == freeBlocks[i + contiguousLength].Offset)
                    {
                        contiguousLength += 1;
                    }

                    if (contiguousLength > 1)
                    {
                        ulong blockEnd = freeBlocks[i + contiguousLength - 1].End;
                        freeBlocks.RemoveRange(i, contiguousLength);

                        VkMemoryBlock mergedBlock = new VkMemoryBlock(
                            Memory,
                            blockStart,
                            blockEnd - blockStart,
                            _memoryTypeIndex,
                            _mappedPtr,
                            false);
                        freeBlocks.Insert(i, mergedBlock);
                        hasMerged = true;
                        contiguousLength = 0;
                    }
                }

                return hasMerged;
            }

#if DEBUG
            private HashSet<VkMemoryBlock> _allocatedBlocks = new HashSet<VkMemoryBlock>();

            private void CheckAllocatedBlock(VkMemoryBlock block)
            {
                foreach (VkMemoryBlock oldBlock in _allocatedBlocks)
                {
                    Debug.Assert(!BlocksOverlap(block, oldBlock), "Allocated blocks have overlapped.");
                }

                Debug.Assert(_allocatedBlocks.Add(block), "Same block added twice.");
            }

            private bool BlocksOverlap(VkMemoryBlock first, VkMemoryBlock second)
            {
                ulong firstStart = first.Offset;
                ulong firstEnd = first.Offset + first.Size;
                ulong secondStart = second.Offset;
                ulong secondEnd = second.Offset + second.Size;

                return (firstStart <= secondStart && firstEnd > secondStart
                    || firstStart >= secondStart && firstEnd <= secondEnd
                    || firstStart < secondEnd && firstEnd >= secondEnd
                    || firstStart <= secondStart && firstEnd >= secondEnd);
            }

            private void RemoveAllocatedBlock(VkMemoryBlock block)
            {
                Debug.Assert(_allocatedBlocks.Remove(block), "Unable to remove a supposedly allocated block.");
            }
#endif
            public bool IsFullFreeBlock()
            {
                if (_freeBlocks.Count == 1)
                {
                    VkMemoryBlock freeBlock = _freeBlocks[0];
                    return freeBlock.Offset == 0
                           && freeBlock.Size == _chunkSize;
                }
                return false;
            }

            public void Dispose()
            {
                vkFreeMemory(_device, _memory, null);
            }
        }

        public void Dispose()
        {
            foreach (KeyValuePair<uint, ChunkAllocatorSet> kvp in _allocatorsByMemoryType)
            {
                kvp.Value.Dispose();
            }

            foreach (KeyValuePair<uint, ChunkAllocatorSet> kvp in _allocatorsByMemoryTypeUnmapped)
            {
                kvp.Value.Dispose();
            }
        }

        internal IntPtr Map(VkMemoryBlock memoryBlock)
        {
            void* ret;
            VkResult result = vkMapMemory(_device, memoryBlock.DeviceMemory, memoryBlock.Offset, memoryBlock.Size, default, &ret);
            CheckResult(result);
            return (IntPtr)ret;
        }
    }

    [DebuggerDisplay("[Mem:{DeviceMemory.Handle}] Off:{Offset}, Size:{Size} End:{Offset+Size}")]
    internal unsafe struct VkMemoryBlock : IEquatable<VkMemoryBlock>
    {
        public readonly uint MemoryTypeIndex;
        public readonly VkDeviceMemory DeviceMemory;
        public readonly void* BaseMappedPointer;
        public readonly bool DedicatedAllocation;

        public ulong Offset;
        public ulong Size;

        public void* BlockMappedPointer => ((byte*)BaseMappedPointer) + Offset;
        public bool IsPersistentMapped => BaseMappedPointer != null;
        public ulong End => Offset + Size;

        public VkMemoryBlock(
            VkDeviceMemory memory,
            ulong offset,
            ulong size,
            uint memoryTypeIndex,
            void* mappedPtr,
            bool dedicatedAllocation)
        {
            DeviceMemory = memory;
            Offset = offset;
            Size = size;
            MemoryTypeIndex = memoryTypeIndex;
            BaseMappedPointer = mappedPtr;
            DedicatedAllocation = dedicatedAllocation;
        }

        public bool Equals(VkMemoryBlock other)
        {
            return DeviceMemory.Equals(other.DeviceMemory)
                && Offset.Equals(other.Offset)
                && Size.Equals(other.Size);
        }
    }
}

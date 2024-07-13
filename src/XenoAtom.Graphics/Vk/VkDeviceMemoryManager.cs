// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Threading;
using XenoAtom.Allocators;
using XenoAtom.Collections;
using XenoAtom.Interop;
using static XenoAtom.Interop.vulkan;

namespace XenoAtom.Graphics.Vk;

internal unsafe class VkDeviceMemoryManager : IDisposable
{
    private readonly VkDevice _device;
    private readonly VkPhysicalDevice _physicalDevice;
    private readonly VkPhysicalDeviceProperties _physicalDeviceProperties;
    private readonly VkPhysicalDeviceMemoryProperties _memoryProperties;
    private UnsafeDictionary<int, VkDeviceMemoryChunkAllocator> _dedicatedAllocators;
    private UnsafeDictionary<VkDeviceMemory, VkDeviceMemoryMappedState> _mappedMemory;
    private UnsafeDictionary<VkMemoryAllocatorKey, TlsfAllocator> _allocators;
    private readonly ulong _bufferImageGranularity;
    private readonly uint _maxMemoryAllocationCount;
    private readonly ulong _maxMemoryAllocationSize;
    private readonly bool _useAmdDeviceCoherentMemory = false;
    private readonly object _lock = new object();
    private ulong _totalAllocatedBytes;
        
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

        var subProps3 = new VkPhysicalDeviceMaintenance3Properties();
        var memoryProperties2 = new VkPhysicalDeviceProperties2
        {
            pNext = &subProps3
        };
        vkGetPhysicalDeviceProperties2(physicalDevice, ref memoryProperties2);
        _maxMemoryAllocationSize = subProps3.maxMemoryAllocationSize;

        _mappedMemory = new(128);
        _dedicatedAllocators = new((int)VK_MAX_MEMORY_TYPES);
        _allocators = new((int)VK_MAX_MEMORY_TYPES);
    }

    private uint MemoryTypeCount => _memoryProperties.memoryTypeCount;

    private uint GlobalMemoryTypeBits => CalculateGlobalMemoryTypeBits();
    private bool IsIntegratedGpu => _physicalDeviceProperties.deviceType == VK_PHYSICAL_DEVICE_TYPE_INTEGRATED_GPU;

    public VkDeviceMemoryAllocation Allocate(vulkan.VkBuffer vkBuffer, in VkDeviceMemoryAllocationCreateInfo allocationCreateInfo)
    {
        var memoryTypeBits = allocationCreateInfo.MemoryTypeBits != 0 ? allocationCreateInfo.MemoryTypeBits : uint.MaxValue;

        while (TryFindMemoryTypeIndex(memoryTypeBits, in allocationCreateInfo, out int memoryTypeIndex))
        {
            VkBufferMemoryRequirementsInfo2 requirementsInfo = new() { buffer = vkBuffer };
            VkMemoryRequirements2 requirements = new();

            var dedicatedReqs = new VkMemoryDedicatedRequirements();
            requirements.pNext = &dedicatedReqs;
            
            vkGetBufferMemoryRequirements2(_device, requirementsInfo, ref requirements);
            ulong size = requirements.memoryRequirements.size;
            ulong alignment = Math.Max(_bufferImageGranularity, requirements.memoryRequirements.alignment.Value);

            var copyAllocationCreateInfo = allocationCreateInfo;
            if (dedicatedReqs.prefersDedicatedAllocation || dedicatedReqs.requiresDedicatedAllocation)
            {
                copyAllocationCreateInfo.Flags |= VkDeviceMemoryAllocationCreateFlags.VMA_ALLOCATION_CREATE_DEDICATED_MEMORY_BIT;
            }

            var dedicated = (allocationCreateInfo.Flags & VkDeviceMemoryAllocationCreateFlags.VMA_ALLOCATION_CREATE_DEDICATED_MEMORY_BIT) != 0;

            if (TryAllocate(memoryTypeIndex, (uint)size, (uint)alignment, dedicated ? vkBuffer.Value.Handle : 0, 0, in copyAllocationCreateInfo, out VkDeviceMemoryAllocation memoryBlock))
            {
                vkBindBufferMemory(_device, vkBuffer, memoryBlock.DeviceMemory, memoryBlock.Offset)
                    .VkCheck("Unable to bind allocated buffer memory");

                return memoryBlock;
            }

            // Remove old memTypeIndex from list of possibilities.
            memoryTypeBits &= ~(1u << memoryTypeIndex);
        }

        throw new GraphicsException("Unable to allocate sufficient Vulkan memory.");
    }
    
    public VkDeviceMemoryAllocation Allocate(VkImage vkImage, in VkDeviceMemoryAllocationCreateInfo allocationCreateInfo)
    {
        var memoryTypeBits = allocationCreateInfo.MemoryTypeBits;

        while (TryFindMemoryTypeIndex(memoryTypeBits, in allocationCreateInfo, out int memoryTypeIndex))
        {

            VkImageMemoryRequirementsInfo2 requirementsInfo = new() { image = vkImage };
            VkMemoryRequirements2 requirements = new();
            var dedicatedReqs = new VkMemoryDedicatedRequirements();
            requirements.pNext = &dedicatedReqs;

            vkGetImageMemoryRequirements2(_device, requirementsInfo, ref requirements);
            ulong size = requirements.memoryRequirements.size;
            ulong alignment = Math.Max(_bufferImageGranularity, requirements.memoryRequirements.alignment.Value);

            var copyAllocationCreateInfo = allocationCreateInfo;
            if (dedicatedReqs.prefersDedicatedAllocation || dedicatedReqs.requiresDedicatedAllocation)
            {
                copyAllocationCreateInfo.Flags |= VkDeviceMemoryAllocationCreateFlags.VMA_ALLOCATION_CREATE_DEDICATED_MEMORY_BIT;
            }

            var dedicated = (allocationCreateInfo.Flags & VkDeviceMemoryAllocationCreateFlags.VMA_ALLOCATION_CREATE_DEDICATED_MEMORY_BIT) != 0;

            if (TryAllocate(memoryTypeIndex, (uint)size, (uint)alignment, 0, dedicated ? vkImage.Value.Handle : 0, in copyAllocationCreateInfo, out VkDeviceMemoryAllocation memoryBlock))
            {
                vkBindImageMemory(_device, vkImage, memoryBlock.DeviceMemory, memoryBlock.Offset)
                    .VkCheck("Unable to bind allocated image memory");

                return memoryBlock;
            }

            // Remove old memTypeIndex from list of possibilities.
            memoryTypeBits &= ~(1u << memoryTypeIndex);
        }

        throw new GraphicsException("Unable to allocate sufficient Vulkan memory.");
    }

    public void Free(VkDeviceMemoryAllocation block)
    {
        if (block.Token != default)
        {
            var allocator = GetOrCreateAllocator(block.MemoryTypeIndex, block.Alignment);
            lock (allocator)
            {
                allocator.Free(block.Token);
            }
        }
        else
        {
            vkFreeMemory(_device, block.DeviceMemory, null);
        }
    }

    public void Dispose()
    {
        TlsfAllocator[] allocators;
        lock (_lock)
        {
            allocators = _allocators.Values.ToArray();
            _allocators.Clear();
            _dedicatedAllocators.Clear();
        }

        foreach (var allocator in allocators)
        {
            lock (allocator)
            {
                allocator.Reset();
            }
        }
    }
    
    private VkDeviceMemoryChunkAllocator GetOrCreateDedicatedChunkAllocator(int memoryTypeIndex)
    {
        VkDeviceMemoryChunkAllocator chunkAllocator;
        lock (_lock)
        {
            ref var refChunkAllocator = ref _dedicatedAllocators.GetValueRefOrAddDefault(memoryTypeIndex, out var exists);
            if (!exists)
            {
                refChunkAllocator = new VkDeviceMemoryChunkAllocator(_device, memoryTypeIndex);
            }
            chunkAllocator = refChunkAllocator;
        }
        return chunkAllocator;
    }

    private TlsfAllocator GetOrCreateAllocator(int memoryTypeIndex, uint alignment)
    {
        TlsfAllocator allocator;
        lock (_lock)
        {
            ref var refAllocator = ref _allocators.GetValueRefOrAddDefault(new VkMemoryAllocatorKey(memoryTypeIndex, alignment), out var exists);
            if (!exists)
            {
                var backend = new VkDeviceMemoryChunkAllocator(_device, memoryTypeIndex);
                refAllocator = new TlsfAllocator(backend, alignment);
            }
            allocator = refAllocator;
        }
        return allocator;
    }
    
    private bool TryAllocate(int memoryTypeIndex, uint size, uint alignment, nint dedicatedBuffer, nint dedicatedImage, in VkDeviceMemoryAllocationCreateInfo allocationCreateInfo, out VkDeviceMemoryAllocation memoryBlock)
    {
        VkDeviceMemory vkDeviceMemory;
        VkDeviceMemoryMappedState? mapped = null;
        if ((allocationCreateInfo.Flags & VkDeviceMemoryAllocationCreateFlags.VMA_ALLOCATION_CREATE_DEDICATED_MEMORY_BIT) != 0)
        {
            var chunkAllocator = GetOrCreateDedicatedChunkAllocator(memoryTypeIndex);
            
            MemoryChunk chunk;
            lock (chunkAllocator)
            {
                if (!chunkAllocator.TryAllocateChunk(size, dedicatedBuffer, dedicatedImage, out chunk))
                {
                    memoryBlock = default;
                    return false;
                }

                vkDeviceMemory = new(new((nint)chunk.Id.Value));

                if ((allocationCreateInfo.Flags & (VkDeviceMemoryAllocationCreateFlags.VMA_ALLOCATION_CREATE_HOST_ACCESS_RANDOM_BIT | VkDeviceMemoryAllocationCreateFlags.VMA_ALLOCATION_CREATE_HOST_ACCESS_SEQUENTIAL_WRITE_BIT)) != 0)
                {
                    if (!_mappedMemory.TryGetValue(vkDeviceMemory, out mapped))
                    {
                        mapped = new VkDeviceMemoryMappedState();
                        _mappedMemory.Add(vkDeviceMemory, mapped);
                    }
                }
            }

            memoryBlock = new VkDeviceMemoryAllocation(memoryTypeIndex, 0, size, alignment, vkDeviceMemory, mapped, default);
            return true;
        }
        else
        {
            var allocator = GetOrCreateAllocator(memoryTypeIndex, alignment);

            TlsfAllocation allocation;
            lock (allocator)
            {
                if (!allocator.TryAllocate(size, out allocation))
                {
                    memoryBlock = default;
                    return false;
                }

                vkDeviceMemory = new(new((nint)allocation.ChunkId.Value));
                if ((allocationCreateInfo.Flags & (VkDeviceMemoryAllocationCreateFlags.VMA_ALLOCATION_CREATE_HOST_ACCESS_RANDOM_BIT | VkDeviceMemoryAllocationCreateFlags.VMA_ALLOCATION_CREATE_HOST_ACCESS_SEQUENTIAL_WRITE_BIT)) != 0)
                {
                    if (!_mappedMemory.TryGetValue(vkDeviceMemory, out mapped))
                    {
                        mapped = new VkDeviceMemoryMappedState();
                        _mappedMemory.Add(vkDeviceMemory, mapped);
                    }
                }
            }

            memoryBlock = new VkDeviceMemoryAllocation(memoryTypeIndex, allocation.Address, size, alignment, vkDeviceMemory, mapped, allocation.Token);
            if (mapped != null && mapped.IsPersistentMapped)
            {
                mapped.Map(_device, vkDeviceMemory);
            }
            return true;
        }
    }
    
    private bool TryFindMemoryTypeIndex(uint memoryTypeBits, in VkDeviceMemoryAllocationCreateInfo allocationCreateInfo, out int memoryTypeIndex)
    {
        memoryTypeBits &= GlobalMemoryTypeBits;

        if (allocationCreateInfo.MemoryTypeBits != 0)
        {
            memoryTypeBits &= allocationCreateInfo.MemoryTypeBits;
        }

        if (!FindMemoryPreferences(
                IsIntegratedGpu,
                in allocationCreateInfo,
                out VkMemoryPropertyFlags requiredFlags, out VkMemoryPropertyFlags preferredFlags, out VkMemoryPropertyFlags notPreferredFlags))
        {
            memoryTypeIndex = int.MaxValue;
            return false;
        }

        memoryTypeIndex = int.MaxValue;
        int minCost = int.MaxValue;
        for (int memTypeIndex = 0, memTypeBit = 1; memTypeIndex < MemoryTypeCount; ++memTypeIndex, memTypeBit <<= 1)
        {
            // This memory type is acceptable according to memoryTypeBits bitmask.
            if ((memTypeBit & memoryTypeBits) != 0)
            {
                VkMemoryPropertyFlags currFlags = _memoryProperties.memoryTypes[memTypeIndex].propertyFlags;
                // This memory type contains requiredFlags.
                if ((requiredFlags & ~currFlags) == 0)
                {
                    // Calculate cost as number of bits from preferredFlags not present in this memory type.
                    int currCost =
                        BitOperations.PopCount((uint)(preferredFlags & ~currFlags)) +
                        BitOperations.PopCount((uint)(currFlags & notPreferredFlags));
                    // Remember memory type with lowest cost.
                    if (currCost < minCost)
                    {
                        memoryTypeIndex = memTypeIndex;
                        if (currCost == 0)
                        {
                            return true;
                        }
                        minCost = currCost;
                    }
                }
            }
        }
        return (memoryTypeIndex != int.MaxValue);
    }

    private uint CalculateGlobalMemoryTypeBits()
    {
        Debug.Assert(MemoryTypeCount > 0);

        uint memoryTypeBits = uint.MaxValue;

        if (!_useAmdDeviceCoherentMemory)
        {
            // Exclude memory types that have VK_MEMORY_PROPERTY_DEVICE_COHERENT_BIT_AMD.
            for (int index = 0; index < MemoryTypeCount; ++index)
            {
                if ((_memoryProperties.memoryTypes[index].propertyFlags & VK_MEMORY_PROPERTY_DEVICE_COHERENT_BIT_AMD) != 0)
                {
                    memoryTypeBits &= ~(1u << index);
                }
            }
        }

        return memoryTypeBits;
    }
    
    private static bool FindMemoryPreferences(
        bool isIntegratedGPU,
        in VkDeviceMemoryAllocationCreateInfo allocCreateInfo,
        out VkMemoryPropertyFlags requiredFlags,
        out VkMemoryPropertyFlags preferredFlags,
        out VkMemoryPropertyFlags notPreferredFlags)
    {
        requiredFlags = allocCreateInfo.RequiredFlags;
        preferredFlags = allocCreateInfo.PreferredFlags;
        notPreferredFlags = 0;

        switch (allocCreateInfo.Usage)
        {
            case VkDeviceMemoryUsage.Auto:
            case VkDeviceMemoryUsage.AutoPreferDevice:
            case VkDeviceMemoryUsage.AutoPreferHost:
            {
                // This relies on values of VK_IMAGE_USAGE_TRANSFER* being the same VK_BUFFER_IMAGE_TRANSFER*.
                bool deviceAccess = (allocCreateInfo.Flags & VkDeviceMemoryAllocationCreateFlags.VMA_ALLOCATION_CREATE_DEVICE_ACCESS_BUFFER_OR_IMAGE_BIT) != 0;
                bool hostAccessSequentialWrite = (allocCreateInfo.Flags & VkDeviceMemoryAllocationCreateFlags.VMA_ALLOCATION_CREATE_HOST_ACCESS_SEQUENTIAL_WRITE_BIT) != 0;
                bool hostAccessRandom = (allocCreateInfo.Flags & VkDeviceMemoryAllocationCreateFlags.VMA_ALLOCATION_CREATE_HOST_ACCESS_RANDOM_BIT) != 0;
                bool hostAccessAllowTransferInstead = (allocCreateInfo.Flags & VkDeviceMemoryAllocationCreateFlags.VMA_ALLOCATION_CREATE_HOST_ACCESS_ALLOW_TRANSFER_INSTEAD_BIT) != 0;
                bool preferDevice = allocCreateInfo.Usage == VkDeviceMemoryUsage.AutoPreferDevice;
                bool preferHost = allocCreateInfo.Usage == VkDeviceMemoryUsage.AutoPreferHost;

                // CPU random access - e.g. a buffer written to or transferred from GPU to read back on CPU.
                if (hostAccessRandom)
                {
                    // Prefer cached. Cannot require it, because some platforms don't have it (e.g. Raspberry Pi - see #362)!
                    preferredFlags |= VK_MEMORY_PROPERTY_HOST_CACHED_BIT;

                    if (!isIntegratedGPU && deviceAccess && hostAccessAllowTransferInstead && !preferHost)
                    {
                        // Nice if it will end up in HOST_VISIBLE, but more importantly prefer DEVICE_LOCAL.
                        // Omitting HOST_VISIBLE here is intentional.
                        // In case there is DEVICE_LOCAL | HOST_VISIBLE | HOST_CACHED, it will pick that one.
                        // Otherwise, this will give same weight to DEVICE_LOCAL as HOST_VISIBLE | HOST_CACHED and select the former if occurs first on the list.
                        preferredFlags |= VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT;
                    }
                    else
                    {
                        // Always CPU memory.
                        requiredFlags |= VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT;
                    }
                }
                // CPU sequential write - may be CPU or host-visible GPU memory, uncached and write-combined.
                else if (hostAccessSequentialWrite)
                {
                    // Want uncached and write-combined.
                    notPreferredFlags |= VK_MEMORY_PROPERTY_HOST_CACHED_BIT;

                    if (!isIntegratedGPU && deviceAccess && hostAccessAllowTransferInstead && !preferHost)
                    {
                        preferredFlags |= VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT | VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT;
                    }
                    else
                    {
                        requiredFlags |= VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT;

                        // Direct GPU access, CPU sequential write (e.g. a dynamic uniform buffer updated every frame)
                        if (deviceAccess)
                        {
                            // Could go to CPU memory or GPU BAR/unified. Up to the user to decide. If no preference, choose GPU memory.
                            if (preferHost)
                                notPreferredFlags |= VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT;
                            else
                                preferredFlags |= VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT;
                        }
                        // GPU no direct access, CPU sequential write (e.g. an upload buffer to be transferred to the GPU)
                        else
                        {
                            // Could go to CPU memory or GPU BAR/unified. Up to the user to decide. If no preference, choose CPU memory.
                            if (preferDevice)
                                preferredFlags |= VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT;
                            else
                                notPreferredFlags |= VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT;
                        }
                    }
                }
                // No CPU access
                else
                {
                    // if(deviceAccess)
                    //
                    // GPU access, no CPU access (e.g. a color attachment image) - prefer GPU memory,
                    // unless there is a clear preference from the user not to do so.
                    //
                    // else:
                    //
                    // No direct GPU access, no CPU access, just transfers.
                    // It may be staging copy intended for e.g. preserving image for next frame (then better GPU memory) or
                    // a "swap file" copy to free some GPU memory (then better CPU memory).
                    // Up to the user to decide. If no preferece, assume the former and choose GPU memory.

                    if (preferHost)
                        notPreferredFlags |= VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT;
                    else
                        preferredFlags |= VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT;
                }
                break;
            }
            default:
                throw new InvalidOperationException($"Invalid usage {allocCreateInfo.Usage} value.");
        }

        // Avoid DEVICE_COHERENT unless explicitly requested.
        if (((allocCreateInfo.RequiredFlags | allocCreateInfo.PreferredFlags) & (VK_MEMORY_PROPERTY_DEVICE_COHERENT_BIT_AMD | VK_MEMORY_PROPERTY_DEVICE_UNCACHED_BIT_AMD)) == 0)
        {
            notPreferredFlags |= VK_MEMORY_PROPERTY_DEVICE_UNCACHED_BIT_AMD;
        }

        return true;
    }


    //private ulong CalcPreferredChunkSize(uint memTypeIndex)
    //{
    //    var heapIndex = GetMemoryHeapIndexFromTypeIndex(memTypeIndex);
    //    var heapSize = _memoryProperties.memoryHeaps[(int)heapIndex].size;
    //    bool isSmallHeap = heapSize <= SmallHeapSize;
    //    return isSmallHeap ? (heapSize.Value / 8) : _preferredChunkSize;
    //}

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

    internal IntPtr Map(VkDeviceMemoryAllocation memoryBlock)
    {
        Debug.Assert(memoryBlock.Mapped != null);

        if (!memoryBlock.IsPersistentMapped)
        {
            memoryBlock.Mapped!.Map(_device, memoryBlock.DeviceMemory);
        }

        return memoryBlock.Mapped!.MappedPointer;
    }

    internal void Unmap(VkDeviceMemoryAllocation memoryBlock)
    {
        Debug.Assert(memoryBlock.Mapped != null);

        if (!memoryBlock.IsPersistentMapped)
        {
            memoryBlock.Mapped!.Unmap(_device, memoryBlock.DeviceMemory);
        }
    }
    
    private class VkDeviceMemoryChunkAllocator : IMemoryChunkAllocator
    {
        private readonly VkDevice _device;
        private readonly int _memoryTypeIndex;
        private uint _memorySize = 65536;
        private const uint MaxMemorySize = 256 * 1024 * 1024;

        public VkDeviceMemoryChunkAllocator(VkDevice device, int memoryTypeIndex)
        {
            _device = device;
            _memoryTypeIndex = memoryTypeIndex;
        }
        
        public ulong TotalAllocatedBytes { get; private set; }
        
        public bool TryAllocateChunk(MemorySize size, nint dedicatedBuffer, nint dedicatedImage, out MemoryChunk chunk)
        {
            var allocateInfo = new VkMemoryAllocateInfo
            {
                allocationSize = size.Value,
                memoryTypeIndex = (uint)_memoryTypeIndex
            };

            VkMemoryDedicatedAllocateInfo dedicatedAI;
            dedicatedAI = new VkMemoryDedicatedAllocateInfo
            {
                buffer = new(new(dedicatedBuffer)),
                image = new(new(dedicatedImage)),
            };
            allocateInfo.pNext = &dedicatedAI;

            var result = vkAllocateMemory(_device, allocateInfo, null, out var _memory);
            if (result != VK_SUCCESS)
            {
                chunk = default;
                return false;
            }

            chunk = new MemoryChunk(new((ulong)_memory.Value.Handle), 0, size);
            TotalAllocatedBytes += size;
            return true;
        }

        public bool TryAllocateChunk(MemorySize minSize, out MemoryChunk chunk)
        {
            var size = Math.Max(_memorySize, (uint)minSize.Value);
            var allocateInfo = new VkMemoryAllocateInfo
            {
                allocationSize = size,
                memoryTypeIndex = (uint)_memoryTypeIndex
            };

            var result = vkAllocateMemory(_device, allocateInfo, null, out var vkMemory);
            if (result != VK_SUCCESS)
            {
                chunk = default;
                return false;
            }

            // Increase the size for the next chunk allocation
            _memorySize *= 2;

            chunk = new MemoryChunk(new((ulong)vkMemory.Value.Handle), 0, size);
            TotalAllocatedBytes += size;
            return true;
        }
        
        public void FreeChunk(MemoryChunkId chunkId)
        {
            vkFreeMemory(_device, new VkDeviceMemory(new((nint)chunkId.Value)), null);
        }
    }

    private record struct VkMemoryAllocatorKey(int MemoryTypeIndex, uint Alignment);
}
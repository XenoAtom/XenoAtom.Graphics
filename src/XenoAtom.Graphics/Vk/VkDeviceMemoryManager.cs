// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
using XenoAtom.Allocators;
using XenoAtom.Collections;
using XenoAtom.Interop;
using static XenoAtom.Interop.vulkan;

namespace XenoAtom.Graphics.Vk;

internal unsafe class VkDeviceMemoryManager : IDisposable
{
    private const ulong MaxMemoryForNonDedicated = 256 * 1024 * 1024;
    private const ulong MinAlignment = 64;
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

    public void DumpStatistics(StringBuilder writer)
    {
        writer.AppendLine("VkDeviceMemoryManager:");
        writer.AppendLine($"  TotalAllocatedBytes: {_totalAllocatedBytes}");
        writer.AppendLine($"  MaxMemoryAllocationCount: {_maxMemoryAllocationCount}");
        writer.AppendLine($"  MaxMemoryAllocationSize: {_maxMemoryAllocationSize}");
        writer.AppendLine($"  BufferImageGranularity: {_bufferImageGranularity}");
        writer.AppendLine($"  UseAmdDeviceCoherentMemory: {_useAmdDeviceCoherentMemory}");
        writer.AppendLine($"  MemoryTypeCount: {MemoryTypeCount}");
        writer.AppendLine($"  GlobalMemoryTypeBits: {GlobalMemoryTypeBits}");
        writer.AppendLine($"  IsIntegratedGpu: {IsIntegratedGpu}");
        writer.AppendLine($"  MemoryProperties:");
        for (int i = 0; i < MemoryTypeCount; i++)
        {
            writer.AppendLine($"    MemoryType[{i}]: {_memoryProperties.memoryTypes[i].propertyFlags}");
        }

        KeyValuePair<VkMemoryAllocatorKey, TlsfAllocator>[] allocators;
        lock (_lock)
        {
            writer.AppendLine();
            writer.AppendLine($"  MappedMemory:");
            // TODO: lock is not correct for _mappedMemory
            foreach (var mapped in _mappedMemory)
            {
                writer.AppendLine($"    0x{mapped.Key.Value.Handle:X16} -> {mapped.Value}");
            }

            writer.AppendLine();
            writer.AppendLine($"  DedicatedAllocators:");
            foreach (var dedicatedAllocator in _dedicatedAllocators)
            {
                writer.AppendLine($"    {dedicatedAllocator.Key} -> {dedicatedAllocator.Value.TotalAllocatedBytes} bytes allocated");
            }

            allocators = _allocators.ToArray();
        }

        writer.AppendLine();
        writer.AppendLine($"  Allocators:");
        foreach (var allocator in allocators)
        {
            writer.AppendLine("**************************************************************");
            writer.AppendLine($"Chunk {allocator.Key}");
            writer.AppendLine("**************************************************************");
            lock (allocator.Value)
            {
                allocator.Value.Dump(writer);
            }

            writer.AppendLine();
        }

    }

    public VkDeviceMemoryAllocation CreateBufferOrImage(in VkDeviceMemoryAllocationCreateInfo allocationCreateInfo, out nint handle)
    {
        if (allocationCreateInfo.pNext == null)
        {
            throw new InvalidOperationException($"pNext cannot be null in {nameof(VkDeviceMemoryAllocationCreateInfo)}.");
        }

        var dedicatedReqs = new VkMemoryDedicatedRequirements();
        VkMemoryRequirements2 requirements = new()
        {
            pNext = &dedicatedReqs
        };

        var stype = *((VkStructureType*)allocationCreateInfo.pNext);
        bool isImage = false;
        bool isLinear = true;
        VkImage vkImage = default;
        vulkan.VkBuffer vkBuffer = default;
        if (stype == VkStructureType.VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO)
        {
            var imageCreateInfo = (VkImageCreateInfo*)allocationCreateInfo.pNext;
            isImage = true;
            isLinear = imageCreateInfo->tiling == VkImageTiling.VK_IMAGE_TILING_LINEAR;
            vkCreateImage(_device, imageCreateInfo, null, &vkImage)
                .VkCheck("Unable to create image");
            handle = vkImage.Value.Handle;

            VkImageMemoryRequirementsInfo2 requirementsInfo = new() { image = vkImage };
            vkGetImageMemoryRequirements2(_device, requirementsInfo, ref requirements);
        }
        else if (stype == VkStructureType.VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO)
        {
            var bufferCreateInfo = (VkBufferCreateInfo*)allocationCreateInfo.pNext;
            vkCreateBuffer(_device, bufferCreateInfo, null, &vkBuffer)
                .VkCheck("Unable to create buffer");
            handle = vkBuffer.Value.Handle;

            VkBufferMemoryRequirementsInfo2 requirementsInfo = new() { buffer = vkBuffer };
            vkGetBufferMemoryRequirements2(_device, requirementsInfo, ref requirements);
        }
        else
        {
            throw new InvalidOperationException($"Invalid pNext structure type {stype}");
        }

        ulong alignment = Math.Max(MinAlignment, requirements.memoryRequirements.alignment.Value);
        ulong size = requirements.memoryRequirements.size;
        var maxSize = (size + alignment - 1) & ~(alignment - 1);
        if (maxSize > int.MaxValue)
        {
            throw new InvalidOperationException(
                $"Invalid size. The requested size {maxSize} bytes must be <= int.MaxValue");
        }

        var copyAllocationCreateInfo = allocationCreateInfo;
        // Force dedicated allocation if the size is above the limit for the TLSF allocator or if it is explicitly requested
        if (maxSize >= MaxMemoryForNonDedicated || (dedicatedReqs.prefersDedicatedAllocation || dedicatedReqs.requiresDedicatedAllocation))
        {
            copyAllocationCreateInfo.Flags |= VkDeviceMemoryAllocationCreateFlags.Dedicated;
        }

        var memoryTypeBits = allocationCreateInfo.MemoryTypeBits;
        bool triedToAllocate = false;
        while (TryFindMemoryTypeIndex(memoryTypeBits, in allocationCreateInfo, out int memoryTypeIndex))
        {
            // Adjust alignment based on memory type (coherent/non-coherent)
            var requestedAlignment = GetMemoryTypeMinAlignment(memoryTypeIndex, alignment);

            if (TryAllocate(memoryTypeIndex, (uint)size, (uint)requestedAlignment, isLinear, vkBuffer, vkImage, in copyAllocationCreateInfo, out VkDeviceMemoryAllocation memoryBlock))
            {
                if (isImage)
                {
                    vkBindImageMemory(_device, new(new(handle)), memoryBlock.DeviceMemory, memoryBlock.Offset)
                        .VkCheck("Unable to bind allocated image memory");
                }
                else
                {
                    vkBindBufferMemory(_device, new(new(handle)), memoryBlock.DeviceMemory, memoryBlock.Offset)
                        .VkCheck("Unable to bind allocated buffer memory");
                }

                return memoryBlock;
            }
            else
            {
                triedToAllocate = true;
            }

            // Remove old memTypeIndex from list of possibilities.
            memoryTypeBits &= ~(1u << memoryTypeIndex);
        }

        throw new GraphicsException(triedToAllocate ? "Unable to allocate sufficient Vulkan memory." : $"Unable to find a memory type with bits 0x{allocationCreateInfo.MemoryTypeBits:X8}.");
    }

    public void Free(VkDeviceMemoryAllocation block)
    {
        if (block.Token.HasValue)
        {
            var allocator = GetOrCreateAllocator(block.MemoryTypeIndex, block.IsLinear, block.Alignment);
            lock (allocator)
            {
                allocator.Free(block.Token.Value);
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

    private TlsfAllocator GetOrCreateAllocator(int memoryTypeIndex, bool isLinear, uint alignment)
    {
        Debug.Assert(BitOperations.IsPow2(alignment));
        TlsfAllocator allocator;
        lock (_lock)
        {
            ref var refAllocator = ref _allocators.GetValueRefOrAddDefault(new VkMemoryAllocatorKey(memoryTypeIndex, alignment | (isLinear ? 1U : 0)), out var exists);
            if (!exists)
            {
                var backend = new VkDeviceMemoryChunkAllocator(_device, memoryTypeIndex);
                refAllocator = new TlsfAllocator(backend, alignment);
            }
            allocator = refAllocator;
        }
        return allocator;
    }

    private bool TryAllocate(int memoryTypeIndex, uint size, uint alignment, bool isLinear, vulkan.VkBuffer dedicatedBuffer, VkImage dedicatedImage, in VkDeviceMemoryAllocationCreateInfo allocationCreateInfo, out VkDeviceMemoryAllocation memoryBlock)
    {
        VkDeviceMemory vkDeviceMemory;
        VkDeviceMemoryMappedState? mapped = null;
        if ((allocationCreateInfo.Flags & VkDeviceMemoryAllocationCreateFlags.Dedicated) != 0)
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

                if ((allocationCreateInfo.Flags & (VkDeviceMemoryAllocationCreateFlags.MappeableForRandomAccess | VkDeviceMemoryAllocationCreateFlags.MappeableForSequentialWrite)) != 0)
                {
                    if (!_mappedMemory.TryGetValue(vkDeviceMemory, out mapped))
                    {
                        mapped = new VkDeviceMemoryMappedState()
                        {
                            IsPersistentMapped = (allocationCreateInfo.Flags & VkDeviceMemoryAllocationCreateFlags.Mapped) != 0
                        };
                        _mappedMemory.Add(vkDeviceMemory, mapped);
                    }
                }
            }

            memoryBlock = new VkDeviceMemoryAllocation(memoryTypeIndex, 0, size, alignment, vkDeviceMemory, isLinear, mapped, null);
        }
        else
        {
            var allocator = GetOrCreateAllocator(memoryTypeIndex, isLinear, alignment);

            TlsfAllocation allocation;
            lock (allocator)
            {
                if (!allocator.TryAllocate(size, out allocation))
                {
                    memoryBlock = default;
                    return false;
                }

                vkDeviceMemory = new(new((nint)allocation.ChunkId.Value));
                if ((allocationCreateInfo.Flags & (VkDeviceMemoryAllocationCreateFlags.MappeableForRandomAccess | VkDeviceMemoryAllocationCreateFlags.MappeableForSequentialWrite)) != 0)
                {
                    if (!_mappedMemory.TryGetValue(vkDeviceMemory, out mapped))
                    {
                        mapped = new VkDeviceMemoryMappedState()
                        {
                            IsPersistentMapped = (allocationCreateInfo.Flags & VkDeviceMemoryAllocationCreateFlags.Mapped) != 0
                        };
                        _mappedMemory.Add(vkDeviceMemory, mapped);
                    }
                }
            }
            memoryBlock = new VkDeviceMemoryAllocation(memoryTypeIndex, allocation.Address, size, alignment, vkDeviceMemory, isLinear, mapped, allocation.Token);
        }

        if (mapped != null && mapped.IsPersistentMapped)
        {
            mapped.Map(_device, vkDeviceMemory);
        }

        return true;
    }

    private bool TryFindMemoryTypeIndex(uint memoryTypeBits, in VkDeviceMemoryAllocationCreateInfo allocationCreateInfo, out int memoryTypeIndex)
    {
        memoryTypeBits &= GlobalMemoryTypeBits;

        GetMemoryPreferences(
            IsIntegratedGpu,
            in allocationCreateInfo,
            out VkMemoryPropertyFlags requiredFlags, out VkMemoryPropertyFlags preferredFlags, out VkMemoryPropertyFlags notPreferredFlags);

        memoryTypeIndex = int.MaxValue;
        int minCost = int.MaxValue;
        for (int memTypeIndex = 0, memTypeBit = 1; memTypeIndex < MemoryTypeCount; ++memTypeIndex, memTypeBit <<= 1)
        {
            // This memory type is acceptable according to memoryTypeBits bitmask.
            if ((memTypeBit & memoryTypeBits) != 0)
            {
                VkMemoryPropertyFlags currFlags = _memoryProperties.memoryTypes[memTypeIndex].propertyFlags;
                // This memory type contains requiredFlags.
                if (currFlags != 0 && (requiredFlags & ~currFlags) == 0)
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

    private static void GetMemoryPreferences(
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
            case VkDeviceMemoryUsage.Default:
            case VkDeviceMemoryUsage.PreferDevice:
            case VkDeviceMemoryUsage.PreferHost:
            {
                // This relies on values of VK_IMAGE_USAGE_TRANSFER* being the same VK_BUFFER_IMAGE_TRANSFER*.
                bool deviceAccess = (allocCreateInfo.Flags & VkDeviceMemoryAllocationCreateFlags.RequiredTransfer) != 0;
                bool hostAccessSequentialWrite = (allocCreateInfo.Flags & VkDeviceMemoryAllocationCreateFlags.MappeableForSequentialWrite) != 0;
                bool hostAccessRandom = (allocCreateInfo.Flags & VkDeviceMemoryAllocationCreateFlags.MappeableForRandomAccess) != 0;
                bool hostAccessAllowTransferInstead = (allocCreateInfo.Flags & VkDeviceMemoryAllocationCreateFlags.AllowTransfer) != 0;
                bool preferDevice = allocCreateInfo.Usage == VkDeviceMemoryUsage.PreferDevice;
                bool preferHost = allocCreateInfo.Usage == VkDeviceMemoryUsage.PreferHost;

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
    }

    private bool IsMemoryTypeNonCoherent(int memTypeIndex)
    {
        Debug.Assert((uint)memTypeIndex < _memoryProperties.memoryTypeCount);
        return (_memoryProperties.memoryTypes[(int)memTypeIndex].propertyFlags & (VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT | VK_MEMORY_PROPERTY_HOST_COHERENT_BIT)) ==
               VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT;
    }

    private ulong GetMemoryTypeMinAlignment(int memTypeIndex, ulong alignment)
    {
        return IsMemoryTypeNonCoherent(memTypeIndex)
            ? Math.Max(alignment, _physicalDeviceProperties.limits.nonCoherentAtomSize.Value)
            : alignment;
    }

    internal IntPtr Map(VkDeviceMemoryAllocation memoryBlock)
    {
        Debug.Assert(memoryBlock.Mapped != null);

        if (!memoryBlock.IsPersistentMapped)
        {
            memoryBlock.Mapped!.Map(_device, memoryBlock.DeviceMemory);
        }

        return memoryBlock.Mapped!.MappedPointer + (nint)memoryBlock.Offset;
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

        public VkDeviceMemoryChunkAllocator(VkDevice device, int memoryTypeIndex)
        {
            _device = device;
            _memoryTypeIndex = memoryTypeIndex;
        }

        public ulong TotalAllocatedBytes { get; private set; }

        public bool TryAllocateChunk(MemorySize size, vulkan.VkBuffer dedicatedBuffer, VkImage dedicatedImage, out MemoryChunk chunk)
        {
            var allocateInfo = new VkMemoryAllocateInfo
            {
                allocationSize = size.Value,
                memoryTypeIndex = (uint)_memoryTypeIndex
            };

            VkMemoryDedicatedAllocateInfo dedicatedAI;
            dedicatedAI = new VkMemoryDedicatedAllocateInfo
            {
                buffer = dedicatedBuffer,
                image = dedicatedImage,
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
            _memorySize = Math.Min(_memorySize * 2, (uint)MaxMemoryForNonDedicated);

            chunk = new MemoryChunk(new((ulong)vkMemory.Value.Handle), 0, size);
            TotalAllocatedBytes += size;
            return true;
        }

        public void FreeChunk(MemoryChunkId chunkId)
        {
            vkFreeMemory(_device, new VkDeviceMemory(new((nint)chunkId.Value)), null);
        }
    }

    /// <summary>
    /// Key used for the allocator cache. We have one TLSF allocator per MemoryTypeIndex/AlignmentAndLinear
    /// </summary>
    /// <param name="MemoryTypeIndex">The memory type index.</param>
    /// <param name="AlignmentAndLinear">The alignment >= 64 and combined with a flag indicating whether the resource is linear or not.</param>
    private record struct VkMemoryAllocatorKey(int MemoryTypeIndex, uint AlignmentAndLinear)
    {
        public override string ToString()
        {
            return $"MemoryTypeIndex: {MemoryTypeIndex}, Alignment: {AlignmentAndLinear & ~1}, Linear: {(AlignmentAndLinear & 1) != 0}";
        }
    }
}
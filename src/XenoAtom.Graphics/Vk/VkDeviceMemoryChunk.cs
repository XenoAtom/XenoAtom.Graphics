// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;
using System.Diagnostics;
using XenoAtom.Allocators;
using XenoAtom.Interop;
using static XenoAtom.Interop.vulkan;

namespace XenoAtom.Graphics.Vk;

internal class VkDeviceMemoryChunk
{
    private int _mapRefCount;

    public int Size { get; private set; }

    public int MemoryTypeIndex { get; private set; }

    public VkDeviceMemory Handle { get; private set; }

    public TlsfAllocator? TlsfAllocator { get; set; }

    public bool IsLinear { get; set; }

    public bool IsPersistentMapped { get; set; }

    public nint MappedPointer { get; private set; }

    public void Initialize(VkDeviceMemory handle, int memoryTypeIndex, int size)
    {
        Handle = handle;
        MemoryTypeIndex = memoryTypeIndex;
        Size = size;
    }

    public void InitializedMapped(VkDevice device)
    {
        lock (this)
        {
            if (!IsPersistentMapped)
            {
                Map(device);
                IsPersistentMapped = true;
            }
        }
    }

    public unsafe void Map(VkDevice device)
    {
        lock (this)
        {
            if (_mapRefCount == 0)
            {
                var mappedPointer = nint.Zero;
                vkMapMemory(device, Handle, 0, VK_WHOLE_SIZE, default, (void**)&mappedPointer)
                    .VkCheck("Unable to map memory");
                MappedPointer = mappedPointer;
            }

            _mapRefCount++;
        }
    }

    public void Unmap(VkDevice device)
    {
        lock (this)
        {
            _mapRefCount--;
            Debug.Assert(_mapRefCount >= 0);

            if (_mapRefCount == 0)
            {
                vkUnmapMemory(device, Handle);
                MappedPointer = nint.Zero;
            }
        }
    }

    public unsafe void Dispose(VkDevice device)
    {
        lock (this)
        {
            if (Handle != default)
            {
                if (IsPersistentMapped)
                {
                    Unmap(device);
                }

                vkFreeMemory(device, Handle, null);
                Handle = default;
                TlsfAllocator = null;
            }
        }
    }

    public override string ToString()
        => $"VkDeviceMemoryChunk({Handle}) MemoryTypeIndex: {MemoryTypeIndex}, Size: {Size}, IsLinear: {IsLinear}, IsPersistentMapped: {IsPersistentMapped}, MappedPointer: 0x{MappedPointer:X16}, MapRefCount: {_mapRefCount}";
}
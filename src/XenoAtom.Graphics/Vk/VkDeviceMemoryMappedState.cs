// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System.Diagnostics;
using XenoAtom.Interop;
using static XenoAtom.Interop.vulkan;

namespace XenoAtom.Graphics.Vk;

internal class VkDeviceMemoryMappedState
{
    private int _refCount;

    public bool IsPersistentMapped { get; set; }

    public nint MappedPointer { get; private set; }

    public unsafe void Map(VkDevice device, VkDeviceMemory memory)
    {
        lock (this)
        {
            if (_refCount == 0)
            {
                var mappedPointer = nint.Zero;
                var result = vkMapMemory(device, memory, 0, VK_WHOLE_SIZE, default, (void**)&mappedPointer);
                result.VkCheck("Unable to map memory");
                MappedPointer = mappedPointer;
            }

            _refCount++;
        }
    }

    public void Unmap(VkDevice device, VkDeviceMemory memory)
    {
        lock (this)
        {
            _refCount--;
            Debug.Assert(_refCount >= 0);

            if (_refCount == 0)
            {
                vkUnmapMemory(device, memory);
                MappedPointer = nint.Zero;
            }
        }
    }
}
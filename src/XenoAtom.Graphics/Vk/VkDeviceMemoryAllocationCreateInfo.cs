// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using XenoAtom.Interop;

namespace XenoAtom.Graphics.Vk;

public struct VkDeviceMemoryAllocationCreateInfo
{
    public VkDeviceMemoryAllocationCreateInfo()
    {
        MemoryTypeBits = uint.MaxValue;
    }
    
    public VkDeviceMemoryUsage Usage;
    public VkDeviceMemoryAllocationCreateFlags Flags;
    public vulkan.VkMemoryPropertyFlags RequiredFlags;
    public vulkan.VkMemoryPropertyFlags PreferredFlags;
    public uint MemoryTypeBits;
}
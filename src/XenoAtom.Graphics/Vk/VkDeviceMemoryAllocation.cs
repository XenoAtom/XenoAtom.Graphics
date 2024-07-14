// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;
using System.Diagnostics;
using XenoAtom.Allocators;
using XenoAtom.Interop;

namespace XenoAtom.Graphics.Vk;

[DebuggerDisplay("[Mem:{DeviceMemory}] Off:{Offset}, Size:{Size} End:{End}")]
internal readonly unsafe struct VkDeviceMemoryAllocation : IEquatable<VkDeviceMemoryAllocation>
{
    public VkDeviceMemoryAllocation(int memoryTypeIndex, ulong offset, ulong size, uint alignment, vulkan.VkDeviceMemory deviceMemory, bool isLinear, VkDeviceMemoryMappedState? mapped, TlsfAllocationToken? token)
    {
        MemoryTypeIndex = memoryTypeIndex;
        DeviceMemory = deviceMemory;
        Mapped = mapped;
        Token = token;
        IsLinear = isLinear;
        Offset = offset;
        Size = size;
        Alignment = alignment;
    }

    public readonly int MemoryTypeIndex;

    public readonly vulkan.VkDeviceMemory DeviceMemory;

    public bool IsPersistentMapped => Mapped?.IsPersistentMapped ?? false;

    public readonly bool IsLinear;

    public readonly VkDeviceMemoryMappedState? Mapped;

    public readonly TlsfAllocationToken? Token;

    public nint MappedPointerWithOffset => Mapped?.MappedPointer + (nint)Offset ?? nint.Zero;

    public readonly ulong Offset;

    public readonly ulong Size;

    public readonly uint Alignment;
    
    public ulong End => Offset + Size;

    public bool Equals(VkDeviceMemoryAllocation other)
    {
        return DeviceMemory.Equals(other.DeviceMemory) && Offset == other.Offset && Size == other.Size;
    }

    public override bool Equals(object? obj)
    {
        return obj is VkDeviceMemoryAllocation other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(DeviceMemory, Offset, Size);
    }

    public static bool operator ==(VkDeviceMemoryAllocation left, VkDeviceMemoryAllocation right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(VkDeviceMemoryAllocation left, VkDeviceMemoryAllocation right)
    {
        return !left.Equals(right);
    }
}
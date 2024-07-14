// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Numerics;
using XenoAtom.Allocators;
using static XenoAtom.Interop.vulkan;

namespace XenoAtom.Graphics.Vk;

[DebuggerDisplay("[Chunk:{Chunk}] Offset:{Offset}, Size:{Size}")]
internal readonly struct VkDeviceMemoryChunkRange : IEquatable<VkDeviceMemoryChunkRange>
{
    public VkDeviceMemoryChunkRange(VkDeviceMemoryChunk chunk, ulong offset, uint size, uint alignment, TlsfAllocationToken token)
    {
        Chunk = chunk;
        Offset = offset;
        Size = size;
        Alignment = alignment;
        Token = token;
    }

    public nint MappedPointerWithOffset => Chunk.MappedPointer + (nint)Offset;

    public readonly VkDeviceMemoryChunk Chunk;

    public readonly ulong Offset;

    public readonly uint Size;
    
    public readonly uint Alignment;

    public readonly TlsfAllocationToken Token;

    public VkDeviceMemory DeviceMemory => Chunk.Handle;

    public bool IsPersistentMapped => Chunk.IsPersistentMapped;

    public bool Equals(VkDeviceMemoryChunkRange other)
    {
        return Chunk.Equals(other.Chunk) && Offset == other.Offset;
    }

    public override bool Equals(object? obj)
    {
        return obj is VkDeviceMemoryChunkRange other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Chunk, Offset);
    }

    public static bool operator ==(VkDeviceMemoryChunkRange left, VkDeviceMemoryChunkRange right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(VkDeviceMemoryChunkRange left, VkDeviceMemoryChunkRange right)
    {
        return !left.Equals(right);
    }
}
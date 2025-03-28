// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace XenoAtom.Graphics;

/// <summary>
/// Extensions for <see cref="CommandBuffer"/>
/// </summary>
public static class CommandBufferExtensions
{
    /// <summary>
    /// Pushes a constant value to the active <see cref="Pipeline"/> for the given shader stage.
    /// </summary>
    public static void PushConstant<T>(this CommandBuffer buffer, ShaderStages stage, in T data, uint offset = 0) where T : unmanaged
        => buffer.PushConstant(stage, MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref Unsafe.AsRef(in data), 1)), offset);
}
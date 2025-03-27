// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;

namespace XenoAtom.Graphics;

public readonly record struct CommandBufferPoolDescription(CommandBufferPoolFlags Flags = CommandBufferPoolFlags.None)
{
    public bool IsTransient => (Flags & CommandBufferPoolFlags.Transient) != 0;

    public bool CanResetCommandBuffer => (Flags & CommandBufferPoolFlags.CanResetCommandBuffer) != 0;
}

[Flags]
public enum CommandBufferPoolFlags
{
    None = 0,
    Transient = 1,
    CanResetCommandBuffer = 2
}

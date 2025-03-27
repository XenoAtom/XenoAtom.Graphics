// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;

namespace XenoAtom.Graphics;

/// <summary>
/// Describes the flags used to begin a <see cref="CommandBuffer.Begin"/>.
/// </summary>
[Flags]
public enum CommandBufferBeginFlags
{
    /// <summary>
    /// No flags are set.
    /// </summary>
    None = 0,
    /// <summary>
    /// Specifies that each recording of the command buffer will only be submitted once, and the command buffer will be reset and recorded again between each submission.
    /// </summary>
    OneTime = 1 << 0,
    /// <summary>
    /// Indicates that a command buffer can be resubmitted to any queue of the same queue family while it is in the pending state, and recorded into multiple primary command buffers.
    /// </summary>
    Simultaneous = 1 << 1,
}
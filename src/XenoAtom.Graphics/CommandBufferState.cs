// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace XenoAtom.Graphics;

/// <summary>
/// Describes the state of a <see cref="CommandBuffer"/>.
/// </summary>
public enum CommandBufferState
{
    /// <summary>
    /// The command buffer is in an unallocated state and cannot be used.
    /// </summary>
    Unallocated = 0,
    
    /// <summary>
    /// The command buffer is idle and ready for recording commands.
    /// </summary>
    Ready = 1,

    /// <summary>
    /// The command buffer is actively recording commands.
    /// </summary>
    Recording = 2,

    /// <summary>
    /// The command buffer has finished recording commands.
    /// </summary>
    Recorded = 3,

    /// <summary>
    /// The command buffer has been submitted to a queue for execution.
    /// </summary>
    Submitted = 4,

    /// <summary>
    /// The command buffer has completed execution.
    /// </summary>
    Completed = 5,

    /// <summary>
    /// The command buffer has been disposed and can no longer be used.
    /// </summary>
    Disposed = 6,
}
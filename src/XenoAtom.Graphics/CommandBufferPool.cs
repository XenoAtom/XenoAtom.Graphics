// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;

namespace XenoAtom.Graphics;

/// <summary>
/// A device resource which allows to create and managed <see cref="CommandBuffer"/>>.
/// </summary>
public abstract class CommandBufferPool : GraphicsDeviceObject
{
    internal CommandBufferPool(GraphicsDevice device,
        in CommandBufferPoolDescription description) : base(device)
    {
        Description = description;
    }
    
    /// <summary>
    /// Gets the description of this command buffer pool.
    /// </summary>
    public readonly CommandBufferPoolDescription Description;

    private CommandBufferPoolState _state;

    /// <summary>
    /// Gets the current state of the command buffer pool.
    /// </summary>
    public CommandBufferPoolState State
    {
        get => _state;
        private protected set
        {
            if (_state == value)
            {
                return;
            }
            _state = value;
            StateChanged?.Invoke(this);
        }
    }

    /// <summary>
    /// Event raised when the state of this pool has changed.
    /// </summary>
    public event Action<CommandBufferPool>? StateChanged;

    /// <summary>
    /// Gets the native handle of this command buffer pool.
    /// </summary>
    /// <remarks>
    /// For Vulkan, this is a VkCommandPool.
    /// </remarks>
    public abstract nint Handle { get; }

    /// <summary>
    /// Creates a new <see cref="CommandBuffer"/>.
    /// </summary>
    /// <returns>A new command buffer</returns>
    public abstract CommandBuffer CreateCommandBuffer();
    
    /// <summary>
    /// Resets all the command buffers in this pool.
    /// </summary>
    public void Reset() => Reset(false);

    /// <summary>
    /// Resets all the command buffers in this pool.
    /// </summary>
    /// <param name="allowReleasingSystemMemory"></param>
    public abstract void Reset(bool allowReleasingSystemMemory);

    /// <summary>
    /// Notifies the pool that a <see cref="CommandBuffer"/> has changed state.
    /// </summary>
    /// <param name="cb">The command buffer state changed</param>
    internal abstract void NotifyBufferStateChanged(CommandBuffer cb);

    internal void ChangeState(CommandBufferPoolState state)
    {
        State = state;
    }
}

/// <summary>
/// Describes the simplified state of a <see cref="CommandBufferPool"/>.
/// </summary>
public enum CommandBufferPoolState
{
    /// <summary>
    /// A pool manager is owning this pool and this pool is not ready for any operations.
    /// </summary>
    InPool = 0,

    /// <summary>
    /// The pool has no active buffers. It's ready for allocation or reuse.
    /// </summary>
    Ready = 1,

    /// <summary>
    /// The pool has buffers currently in use (recording, recorded or submitted).
    /// </summary>
    InUse = 2,

    /// <summary>
    /// All buffers allocated from this pool have completed execution; the pool can safely be reset.
    /// </summary>
    Completed = 3,

    /// <summary>
    /// The pool has been disposed and can no longer be used.
    /// </summary>
    Disposed = 4,
}

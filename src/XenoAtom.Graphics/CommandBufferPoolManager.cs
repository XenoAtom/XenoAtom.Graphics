// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;
using XenoAtom.Collections;

namespace XenoAtom.Graphics;

/// <summary>
/// A device resource which allows to manage automatically <see cref="CommandBufferPool"/> in flight.
/// </summary>
public class CommandBufferPoolManager : GraphicsDeviceObject
{
    private readonly object _lock = new();
    private UnsafeList<CommandBufferPool> _availableCommandBufferPools = new();
    private UnsafeList<CommandBufferPool> _inUseCommandBufferPools = new();
    private int _availableCommandBufferPoolsCount;
    private int _inUseCommandBufferPoolsCount;
    
    /// <summary>
    /// Initializes a new instance of the <see cref="CommandBufferPoolManager"/> class.
    /// </summary>
    /// <param name="device">The device</param>
    public CommandBufferPoolManager(GraphicsDevice device) : base(device)
    {
    }

    /// <summary>
    /// Gets the number of available command buffer pools.
    /// </summary>
    public int AvailableCommandBufferPoolsCount => _availableCommandBufferPoolsCount;

    /// <summary>
    /// Gets the number of command buffer pools in use.
    /// </summary>
    public int InUseCommandBufferPoolsCount => _inUseCommandBufferPoolsCount;

    /// <summary>
    /// Rents a <see cref="CommandBufferPool"/>.
    /// </summary>
    /// <returns>A command buffer pool ready to be used for recording new commands.</returns>
    public CommandBufferPool Rent()
    {
        // We always perform a refresh before a rent to allow to minimize the number of command pool in flight
        // We expect to create one command buffer pool per frame per thread
        Device.Refresh();

        CommandBufferPool? pool = null;
        lock (_lock)
        {
            if (_availableCommandBufferPools.Count > 0)
            {
                pool = _availableCommandBufferPools.Pop();
                _availableCommandBufferPoolsCount--;
                _inUseCommandBufferPoolsCount++;
                _inUseCommandBufferPools.Add(pool);
            }
        }

        pool ??= CreateCommandBufferPool();
        pool.AddReference(); // We expect the client to dispose it
        pool.ChangeState(CommandBufferPoolState.Ready);

        return pool;
    }

    public void Return(CommandBufferPool pool)
    {
        if (pool.State != CommandBufferPoolState.Ready) throw new InvalidOperationException("The command buffer pool is not in a ready state");
        ReturnToPool(pool);
    }

    private CommandBufferPool CreateCommandBufferPool()
    {
        var pool = Device.CreateCommandBufferPool(new CommandBufferPoolDescription());
        pool.ChangeState(CommandBufferPoolState.InPool);
        pool.StateChanged += OnCommandBufferPoolStateChanged;
        lock (_lock)
        {
            _inUseCommandBufferPools.Add(pool);
            _inUseCommandBufferPoolsCount++;
        }
        return pool;
    }

    private void OnCommandBufferPoolStateChanged(CommandBufferPool pool)
    {
        if (pool.State == CommandBufferPoolState.Completed)
        {
            ReturnToPool(pool);
        }
    }

    private void ReturnToPool(CommandBufferPool pool)
    {
        lock (_lock)
        {

            if (!_inUseCommandBufferPools.Remove(pool))
            {
                throw new InvalidOperationException("The command buffer pool is not in use");
            }
            _inUseCommandBufferPoolsCount--;
            _availableCommandBufferPools.Add(pool);
            _availableCommandBufferPoolsCount++;
            pool.Reset();
            pool.ChangeState(CommandBufferPoolState.InPool);
        }
    }

    internal override void Destroy()
    {
        lock (_lock)
        {
            if (_inUseCommandBufferPools.Count > 0)
            {
                throw new InvalidOperationException("There are still some command buffer pools in use");
            }

            foreach (var pool in _availableCommandBufferPools)
            {
                pool.ReleaseReference();
            }
            _availableCommandBufferPools.Clear();
            _availableCommandBufferPoolsCount = 0;

            foreach (var pool in _inUseCommandBufferPools)
            {
                pool.ReleaseReference();
            }
            _inUseCommandBufferPools.Clear();
            _inUseCommandBufferPoolsCount = 0;
        }
    }
}
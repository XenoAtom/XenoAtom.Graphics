// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using XenoAtom.Collections;
using static XenoAtom.Interop.vulkan;

namespace XenoAtom.Graphics.Vk;

internal unsafe class VkCommandBufferPool : CommandBufferPool
{
    private new VkGraphicsDevice Device => Unsafe.As<GraphicsDevice, VkGraphicsDevice>(ref Unsafe.AsRef(in base.Device));

    private readonly VkCommandPool _pool;
    private UnsafeList<VkBuffer> _availableStagingBuffers = new();
    private UnsafeList<VkCommandBufferExt> _commandBuffers = new();
    private int _commandBufferCreatedCount;
    private int _inUseCount;
    private int _completedCount;
    private bool _blockStateUpdate;
    
    public VkCommandPool CommandPool => _pool;

    public VkCommandBufferPool(VkGraphicsDevice gd, in CommandBufferPoolDescription description)
        : base(gd, in description)
    {
        var poolCInfo = new VkCommandPoolCreateInfo
        {
            queueFamilyIndex = gd.MainQueueFamilyIndex
        };
        if (description.IsTransient)
        {
            poolCInfo.flags |= VK_COMMAND_POOL_CREATE_TRANSIENT_BIT;
        }

        if (description.CanResetCommandBuffer)
        {
            poolCInfo.flags |= VK_COMMAND_POOL_CREATE_RESET_COMMAND_BUFFER_BIT;
        }

        VkResult result = vkCreateCommandPool(Device, poolCInfo, null, out _pool);
        VulkanUtil.CheckResult(result);
        State = CommandBufferPoolState.Ready;
    }

    public override IntPtr Handle => _pool.Value.Handle;

    public override CommandBuffer CreateCommandBuffer()
    {
        VkCommandBufferExt commandBuffer;
        if (_commandBufferCreatedCount == _commandBuffers.Count)
        {
            commandBuffer = new VkCommandBufferExt(this);
            _commandBuffers.Add(commandBuffer);
        }
        else
        {
            commandBuffer = _commandBuffers[_commandBufferCreatedCount];
        }
        _commandBufferCreatedCount++;
        commandBuffer.Allocate();
        commandBuffer.AddReference(); // We expect a client to dispose it
        return commandBuffer;
    }

    public override void Reset(bool allowReleasingSystemMemory)
    {
        _blockStateUpdate = true; // Prevent NotifyBufferStateChanged to be called
        var span = _commandBuffers.AsSpan().Slice(0, _commandBufferCreatedCount);
        foreach (var commandBuffer in span)
        {
            commandBuffer.Free();
        }
        _commandBufferCreatedCount = 0;
        _inUseCount = 0;
        _completedCount = 0;

        var result = vkResetCommandPool(Device.VkDevice, _pool, allowReleasingSystemMemory ? VK_COMMAND_POOL_RESET_RELEASE_RESOURCES_BIT : 0);
        VulkanUtil.CheckResult(result);

        _blockStateUpdate = false;
        State = CommandBufferPoolState.Ready;
    }

    internal override void NotifyBufferStateChanged(CommandBuffer cb)
    {
        if (_blockStateUpdate)
        {
            return;
        }

        if (State == CommandBufferPoolState.Disposed)
        {
            throw new ObjectDisposedException($"The command buffer pool {Name} has been disposed");
        }
        
        UpdateCounters(cb);
        UpdatePoolState();
    }

    private void UpdateCounters(CommandBuffer buffer)
    {
        // Assuming buffer tracks previous state internally
        var previousState = buffer.PreviousState;
        var currentState = buffer.State;

        // Decrement counters based on previous state
        switch (previousState)
        {
            case CommandBufferState.Recording:
            case CommandBufferState.Submitted:
                _inUseCount--;
                break;

            case CommandBufferState.Completed:
                _completedCount--;
                break;

            case CommandBufferState.Ready:
                break;
        }

        // Increment counters based on new state
        switch (currentState)
        {
            case CommandBufferState.Recording:
            case CommandBufferState.Submitted:
                _inUseCount++;
                break;

            case CommandBufferState.Completed:
                _completedCount++;
                break;

            case CommandBufferState.Ready:
                break;
        }
    }

    private void UpdatePoolState()
    {
        if (_inUseCount > 0)
            State = CommandBufferPoolState.InUse;
        else if (_completedCount > 0)
            State = CommandBufferPoolState.Completed;
        else
            State = CommandBufferPoolState.Ready;
    }
    
    internal override void Destroy()
    {
        _blockStateUpdate = true;

        foreach (var commandBuffer in _commandBuffers.AsSpan())
        {
            commandBuffer.ReleaseReference();
            Debug.Assert(commandBuffer.State == CommandBufferState.Disposed);
        }
        _commandBuffers.Clear();
        _commandBufferCreatedCount = 0;
        _inUseCount = 0;
        _completedCount = 0;

        vkDestroyCommandPool(Device, _pool, null);

        foreach (VkBuffer buffer in _availableStagingBuffers)
        {
            buffer.Dispose();
        }

        State = CommandBufferPoolState.Disposed;
    }
    
    internal VkBuffer GetStagingBuffer(uint size)
    {
        VkBuffer? ret = null;
        foreach (VkBuffer buffer in _availableStagingBuffers)
        {
            if (buffer.SizeInBytes >= size)
            {
                ret = buffer;
                _availableStagingBuffers.Remove(buffer);
                break;
            }
        }

        if (ret == null)
        {
            ret = (VkBuffer)Device.CreateBuffer(new BufferDescription(size, BufferUsage.Staging));
            ret.Name = $"Staging Buffer (CommandBufferPool {Name})";
        }
        return ret;
    }

    internal void ReturnStagingBuffer(VkBuffer buffer)
    {
        _availableStagingBuffers.Add(buffer);
    }
}
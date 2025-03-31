// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;
using System.Runtime.CompilerServices;
using static XenoAtom.Interop.vulkan;

namespace XenoAtom.Graphics.Vk;

internal abstract class VkGraphicsQueryPool<TQueryData, TRawData> : GraphicsQueryPool<TQueryData>
    where TQueryData: struct
    where TRawData: unmanaged
{
    private protected new VkGraphicsDevice Device => Unsafe.As<GraphicsDevice, VkGraphicsDevice>(ref Unsafe.AsRef(in base.Device));

    public readonly VkQueryPool NativeQueryPool;

    public sealed override uint QueryCount { get; }

    protected unsafe VkGraphicsQueryPool(GraphicsDevice device, GraphicsQueryKind kind, uint queryCount) : base(device, kind)
    {
        QueryCount = queryCount;
        VkQueryPoolCreateInfo queryPoolCreateInfo = new()
        {
            sType = VK_STRUCTURE_TYPE_QUERY_POOL_CREATE_INFO,
            queryType = kind switch
            {
                GraphicsQueryKind.Timestamp => VK_QUERY_TYPE_TIMESTAMP,
                //GraphicsQueryKind.PipelineStatistics => VK_QUERY_TYPE_PIPELINE_STATISTICS,
                _ => throw new GraphicsException($"Invalid query kind {kind}")
            },
            queryCount = queryCount
        };
        VkResult result = vkCreateQueryPool(Device, queryPoolCreateInfo, null, out NativeQueryPool);
        VulkanUtil.CheckResult(result);
    }

    public override void Reset(GraphicsQueryIndex start, uint count)
    {
        vkResetQueryPool(Device, NativeQueryPool, start.Value, count);
    }

    public override IntPtr Handle => NativeQueryPool.Value.Handle;

    internal override unsafe void Destroy()
    {
        vkDestroyQueryPool(Device, NativeQueryPool, null);
    }
    public override unsafe bool TryGetQueryData(GraphicsQueryIndex query, out TQueryData data)
    {
        TRawData rawData = default;
        var result = vkGetQueryPoolResults(Device, NativeQueryPool, query.Value, 1, (nuint)Unsafe.SizeOf<TRawData>(), (void*)&rawData,(nuint)Unsafe.SizeOf<TRawData>(), VK_QUERY_RESULT_64_BIT);
        if (result == VK_SUCCESS)
        {
            GetQueryData(in rawData, out data);
            return true;
        }

        data = default;
        return false;
    }

    protected abstract void GetQueryData(in TRawData rawData, out TQueryData data);

    public static implicit operator VkQueryPool(VkGraphicsQueryPool<TQueryData, TRawData> queryPool) => queryPool.NativeQueryPool;
}

internal class VkGraphicsTimestampQueryPool : VkGraphicsQueryPool<TimeSpan, ulong>
{
    public VkGraphicsTimestampQueryPool(GraphicsDevice device, uint queryCount) : base(device, GraphicsQueryKind.Timestamp, queryCount)
    {
    }

    protected override void GetQueryData(in ulong gpuTicks, out TimeSpan data)
    {
        var timestampPeriod = Device.Adapter.PhysicalDeviceProperties.limits.timestampPeriod;
        double nanoseconds = gpuTicks * (double)timestampPeriod;
        // Convert nanoseconds to TimeSpan ticks (1 tick = 100 ns)
        long timeSpanTicks = (long)(nanoseconds / 100.0);
        data = new TimeSpan(timeSpanTicks);
    }
}
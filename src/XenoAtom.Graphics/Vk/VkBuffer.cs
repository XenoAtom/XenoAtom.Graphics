using System;
using System.Runtime.CompilerServices;
using static XenoAtom.Interop.vulkan;
using static XenoAtom.Graphics.Vk.VulkanUtil;

namespace XenoAtom.Graphics.Vk
{
    internal unsafe class VkBuffer : DeviceBuffer
    {
        private new VkGraphicsDevice Device => Unsafe.As<GraphicsDevice, VkGraphicsDevice>(ref Unsafe.AsRef(in base.Device));

        private readonly XenoAtom.Interop.vulkan.VkBuffer _deviceBuffer;
        private readonly VkMemoryBlock _memory;
        private readonly VkMemoryRequirements _bufferMemoryRequirements;
        
        public override uint SizeInBytes { get; }
        public override BufferUsage Usage { get; }

        public XenoAtom.Interop.vulkan.VkBuffer DeviceBuffer => _deviceBuffer;
        public VkMemoryBlock Memory => _memory;

        public VkMemoryRequirements BufferMemoryRequirements => _bufferMemoryRequirements;

        public VkBuffer(VkGraphicsDevice gd, uint sizeInBytes, BufferUsage usage) : base(gd)
        {
            SizeInBytes = sizeInBytes;
            Usage = usage;

            VkBufferUsageFlagBits vkUsage = VkBufferUsageFlagBits.VK_BUFFER_USAGE_TRANSFER_SRC_BIT | VkBufferUsageFlagBits.VK_BUFFER_USAGE_TRANSFER_DST_BIT;
            if ((usage & BufferUsage.VertexBuffer) == BufferUsage.VertexBuffer)
            {
                vkUsage |= VkBufferUsageFlagBits.VK_BUFFER_USAGE_VERTEX_BUFFER_BIT;
            }
            if ((usage & BufferUsage.IndexBuffer) == BufferUsage.IndexBuffer)
            {
                vkUsage |= VkBufferUsageFlagBits.VK_BUFFER_USAGE_INDEX_BUFFER_BIT;
            }
            if ((usage & BufferUsage.UniformBuffer) == BufferUsage.UniformBuffer)
            {
                vkUsage |= VkBufferUsageFlagBits.VK_BUFFER_USAGE_UNIFORM_BUFFER_BIT;
            }
            if ((usage & BufferUsage.StructuredBufferReadWrite) == BufferUsage.StructuredBufferReadWrite
                || (usage & BufferUsage.StructuredBufferReadOnly) == BufferUsage.StructuredBufferReadOnly)
            {
                vkUsage |= VkBufferUsageFlagBits.VK_BUFFER_USAGE_STORAGE_BUFFER_BIT;
            }
            if ((usage & BufferUsage.IndirectBuffer) == BufferUsage.IndirectBuffer)
            {
                vkUsage |= VK_BUFFER_USAGE_INDIRECT_BUFFER_BIT;
            }

            var bufferCI = new VkBufferCreateInfo
            {
                size = sizeInBytes,
                usage = vkUsage
            };
            VkResult result = vkCreateBuffer(gd.VkDevice, bufferCI, null, out _deviceBuffer);
            CheckResult(result);
            //_gd.DebugLog(DebugLogLevel.Info, DebugLogKind.General, $"VkBuffer Created 0x{_deviceBuffer.Value.Handle:X16}");

            bool prefersDedicatedAllocation;
            var memReqInfo2 = new VkBufferMemoryRequirementsInfo2
            {
                buffer = _deviceBuffer
            };
            var memReqs2 = new VkMemoryRequirements2();
            var dedicatedReqs = new VkMemoryDedicatedRequirements();
            memReqs2.pNext = &dedicatedReqs;
            vkGetBufferMemoryRequirements2(Device, memReqInfo2, ref memReqs2);
            _bufferMemoryRequirements = memReqs2.memoryRequirements;
            prefersDedicatedAllocation = dedicatedReqs.prefersDedicatedAllocation || dedicatedReqs.requiresDedicatedAllocation;

            var isStaging = (usage & BufferUsage.Staging) == BufferUsage.Staging;
            var hostVisible = isStaging || (usage & BufferUsage.Dynamic) == BufferUsage.Dynamic;

            VkMemoryPropertyFlagBits memoryPropertyFlags =
                hostVisible
                    ? VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT | VK_MEMORY_PROPERTY_HOST_COHERENT_BIT
                    : VkMemoryPropertyFlagBits.VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT;

            if (isStaging)
            {
                // Use "host cached" memory for staging when available, for better performance of GPU -> CPU transfers
                var hostCachedAvailable = gd.MemoryManager.TryFindMemoryType(
                    _bufferMemoryRequirements.memoryTypeBits,
                    memoryPropertyFlags | VK_MEMORY_PROPERTY_HOST_CACHED_BIT,
                    out _);
                if (hostCachedAvailable)
                {
                    memoryPropertyFlags |= VkMemoryPropertyFlagBits.VK_MEMORY_PROPERTY_HOST_CACHED_BIT;
                }
            }

            VkMemoryBlock memoryToken = gd.MemoryManager.Allocate(
                _bufferMemoryRequirements.memoryTypeBits,
                memoryPropertyFlags,
                hostVisible,
                _bufferMemoryRequirements.size,
                _bufferMemoryRequirements.alignment,
                prefersDedicatedAllocation,
                default,
                _deviceBuffer);
            _memory = memoryToken;
            result = vkBindBufferMemory(gd.VkDevice, _deviceBuffer, _memory.DeviceMemory, _memory.Offset);
            CheckResult(result);
        }

        internal override void Destroy()
        {
            //_gd.DebugLog(DebugLogLevel.Info, DebugLogKind.General,$"VkBuffer Destroyed 0x{_deviceBuffer.Value.Handle:X16}");
            vkDestroyBuffer(Device, _deviceBuffer, null);
            Device.MemoryManager.Free(Memory);
        }
    }
}

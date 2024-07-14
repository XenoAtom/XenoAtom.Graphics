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
        private readonly VkDeviceMemoryAllocation _memory;
        
        public override uint SizeInBytes { get; }
        public override BufferUsage Usage { get; }

        public XenoAtom.Interop.vulkan.VkBuffer DeviceBuffer => _deviceBuffer;
        public VkDeviceMemoryAllocation Memory => _memory;

        public VkBuffer(VkGraphicsDevice gd, uint sizeInBytes, BufferUsage usage) : base(gd)
        {
            SizeInBytes = sizeInBytes;
            Usage = usage;

            VkBufferUsageFlags vkUsage = VK_BUFFER_USAGE_TRANSFER_SRC_BIT | VK_BUFFER_USAGE_TRANSFER_DST_BIT;
            if ((usage & BufferUsage.VertexBuffer) == BufferUsage.VertexBuffer)
            {
                vkUsage |= VK_BUFFER_USAGE_VERTEX_BUFFER_BIT;
            }
            if ((usage & BufferUsage.IndexBuffer) == BufferUsage.IndexBuffer)
            {
                vkUsage |= VK_BUFFER_USAGE_INDEX_BUFFER_BIT;
            }
            if ((usage & BufferUsage.UniformBuffer) == BufferUsage.UniformBuffer)
            {
                vkUsage |= VK_BUFFER_USAGE_UNIFORM_BUFFER_BIT;
            }
            if ((usage & BufferUsage.StructuredBufferReadWrite) == BufferUsage.StructuredBufferReadWrite
                || (usage & BufferUsage.StructuredBufferReadOnly) == BufferUsage.StructuredBufferReadOnly)
            {
                vkUsage |= VK_BUFFER_USAGE_STORAGE_BUFFER_BIT;
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
            vkCreateBuffer(gd.VkDevice, bufferCI, null, out _deviceBuffer)
                .VkCheck("Unable to create buffer");
            
            var isStaging = (usage & BufferUsage.Staging) == BufferUsage.Staging;
            var hostVisible = isStaging || (usage & BufferUsage.Dynamic) == BufferUsage.Dynamic;

            var allocInfo = new VkDeviceMemoryAllocationCreateInfo
            {
                Usage = hostVisible ? VkDeviceMemoryUsage.PreferHost : VkDeviceMemoryUsage.PreferDevice,
                Flags = isStaging ? VkDeviceMemoryAllocationCreateFlags.MappeableForRandomAccess | VkDeviceMemoryAllocationCreateFlags.Mapped : VkDeviceMemoryAllocationCreateFlags.None
            };
            _memory = gd.MemoryManager.Allocate(_deviceBuffer, allocInfo);
        }

        internal override void Destroy()
        {
            //_gd.DebugLog(DebugLogLevel.Info, DebugLogKind.General,$"VkBuffer Destroyed 0x{_deviceBuffer.Value.Handle:X16}");
            vkDestroyBuffer(Device, _deviceBuffer, null);
            Device.MemoryManager.Free(Memory);
        }
    }
}

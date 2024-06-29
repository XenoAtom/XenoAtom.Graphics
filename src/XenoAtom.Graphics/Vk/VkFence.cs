using System.Runtime.CompilerServices;
using static XenoAtom.Interop.vulkan;


namespace XenoAtom.Graphics.Vk
{
    internal unsafe class VkFence : Fence
    {
        private new VkGraphicsDevice Device => Unsafe.As<GraphicsDevice, VkGraphicsDevice>(ref Unsafe.AsRef(in base.Device));

        private readonly XenoAtom.Interop.vulkan.VkFence _fence;

        public XenoAtom.Interop.vulkan.VkFence DeviceFence => _fence;

        public VkFence(VkGraphicsDevice gd, bool signaled) : base(gd)
        {
            VkFenceCreateInfo fenceCI = new() { flags = signaled ? VK_FENCE_CREATE_SIGNALED_BIT : 0 };
            VkResult result = vkCreateFence(Device, fenceCI, null, out _fence);
            VulkanUtil.CheckResult(result);
        }

        public override void Reset()
        {
            Device.ResetFence(this);
        }

        public override bool Signaled => vkGetFenceStatus(Device, _fence) == VkResult.VK_SUCCESS;

        internal override void Destroy()
        {
            vkDestroyFence(Device, _fence, null);
        }
    }
}

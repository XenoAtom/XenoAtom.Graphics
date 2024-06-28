using System.Runtime.CompilerServices;
using static XenoAtom.Interop.vulkan;


namespace XenoAtom.Graphics.Vk
{
    internal unsafe class VkFence : Fence
    {
        private VkGraphicsDevice _gd => Unsafe.As<GraphicsDevice, VkGraphicsDevice>(ref Unsafe.AsRef(in Device));
        private readonly XenoAtom.Interop.vulkan.VkFence _fence;

        public XenoAtom.Interop.vulkan.VkFence DeviceFence => _fence;

        public VkFence(VkGraphicsDevice gd, bool signaled) : base(gd)
        {
            VkFenceCreateInfo fenceCI = new() { flags = signaled ? VK_FENCE_CREATE_SIGNALED_BIT : 0 };
            VkResult result = vkCreateFence(_gd.Device, fenceCI, null, out _fence);
            VulkanUtil.CheckResult(result);
        }

        public override void Reset()
        {
            _gd.ResetFence(this);
        }

        public override bool Signaled => vkGetFenceStatus(_gd.Device, _fence) == VkResult.VK_SUCCESS;

        internal override void DisposeCore()
        {
            vkDestroyFence(_gd.Device, _fence, null);
        }
    }
}

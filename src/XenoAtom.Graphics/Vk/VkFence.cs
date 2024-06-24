using static XenoAtom.Interop.vulkan;


namespace XenoAtom.Graphics.Vk
{
    internal unsafe class VkFence : Fence
    {
        private readonly VkGraphicsDevice _gd;
        private XenoAtom.Interop.vulkan.VkFence _fence;
        private string _name;
        private bool _destroyed;

        public XenoAtom.Interop.vulkan.VkFence DeviceFence => _fence;

        public VkFence(VkGraphicsDevice gd, bool signaled)
        {
            _gd = gd;
            VkFenceCreateInfo fenceCI = new() { flags = signaled ? VK_FENCE_CREATE_SIGNALED_BIT : 0 };
            VkResult result = vkCreateFence(_gd.Device, fenceCI, null, out _fence);
            VulkanUtil.CheckResult(result);
        }

        public override void Reset()
        {
            _gd.ResetFence(this);
        }

        public override bool Signaled => vkGetFenceStatus(_gd.Device, _fence) == VkResult.VK_SUCCESS;
        public override bool IsDisposed => _destroyed;

        public override string Name
        {
            get => _name;
            set
            {
                _name = value; _gd.SetResourceName(this, value);
            }
        }

        public override void Dispose()
        {
            if (!_destroyed)
            {
                vkDestroyFence(_gd.Device, _fence, null);
                _destroyed = true;
            }
        }
    }
}

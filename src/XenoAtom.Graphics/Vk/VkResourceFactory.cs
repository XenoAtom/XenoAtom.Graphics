using static XenoAtom.Interop.vulkan;

namespace XenoAtom.Graphics.Vk
{
    internal class VkResourceFactory : ResourceFactory
    {
        private readonly VkGraphicsDevice _gd;
        private readonly VkDevice _device;

        public VkResourceFactory(VkGraphicsDevice vkGraphicsDevice)
            : base (vkGraphicsDevice.Features)
        {
            _gd = vkGraphicsDevice;
            _device = vkGraphicsDevice.Device;
        }

        public override GraphicsBackend BackendType => GraphicsBackend.Vulkan;

        public override CommandList CreateCommandList(in CommandListDescription description)
        {
            return new VkCommandList(_gd, description);
        }

        public override Framebuffer CreateFramebuffer(in FramebufferDescription description)
        {
            return new VkFramebuffer(_gd, description, false);
        }

        protected override Pipeline CreateGraphicsPipelineCore(in GraphicsPipelineDescription description)
        {
            return new VkPipeline(_gd, description);
        }

        public override Pipeline CreateComputePipeline(in ComputePipelineDescription description)
        {
            return new VkPipeline(_gd, description);
        }

        public override ResourceLayout CreateResourceLayout(in ResourceLayoutDescription description)
        {
            return new VkResourceLayout(_gd, description);
        }

        public override ResourceSet CreateResourceSet(in ResourceSetDescription description)
        {
            ValidationHelpers.ValidateResourceSet(_gd, description);
            return new VkResourceSet(_gd, description);
        }

        protected override Sampler CreateSamplerCore(in SamplerDescription description)
        {
            return new VkSampler(_gd, description);
        }

        protected override Shader CreateShaderCore(in ShaderDescription description)
        {
            return new VkShader(_gd, description);
        }

        protected override Texture CreateTextureCore(in TextureDescription description)
        {
            return new VkTexture(_gd, description);
        }

        public override Texture CreateTexture(ulong nativeTexture, in TextureDescription description)
        {
            return new VkTexture(
                _gd,
                description.Width, description.Height,
                description.MipLevels, description.ArrayLayers,
                VkFormats.VdToVkPixelFormat(description.Format, (description.Usage & TextureUsage.DepthStencil) != 0),
                description.Usage,
                description.SampleCount,
                new VkImage(new((nint)nativeTexture)));
        }

        protected override TextureView CreateTextureViewCore(in TextureViewDescription description)
        {
            return new VkTextureView(_gd, description);
        }

        protected override DeviceBuffer CreateBufferCore(in BufferDescription description)
        {
            return new VkBuffer(_gd, description.SizeInBytes, description.Usage);
        }

        public override Fence CreateFence(bool signaled)
        {
            return new VkFence(_gd, signaled);
        }

        public override Swapchain CreateSwapchain(in SwapchainDescription description)
        {
            return new VkSwapchain(_gd, description);
        }
    }
}

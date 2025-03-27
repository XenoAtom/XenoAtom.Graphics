using static XenoAtom.Interop.vulkan;

namespace XenoAtom.Graphics.Vk
{
    partial class VkGraphicsDevice
    {
        public override CommandBufferPool CreateCommandBufferPool(in CommandBufferPoolDescription description)
            => LogResourceCreated(new VkCommandBufferPool(this, description));

        public override Framebuffer CreateFramebuffer(in FramebufferDescription description)
            => LogResourceCreated(new VkFramebuffer(this, description, false));

        protected override Pipeline CreateGraphicsPipelineCore(in GraphicsPipelineDescription description)
            => LogResourceCreated(new VkPipeline(this, description));

        public override Pipeline CreateComputePipeline(in ComputePipelineDescription description)
            => LogResourceCreated(new VkPipeline(this, description));

        public override ResourceLayout CreateResourceLayout(in ResourceLayoutDescription description)
            => LogResourceCreated(new VkResourceLayout(this, description));

        public override ResourceSet CreateResourceSet(in ResourceSetDescription description)
        {
            ValidationHelpers.ValidateResourceSet(this, description);
            return LogResourceCreated(new VkResourceSet(this, description));
        }

        protected override Sampler CreateSamplerCore(in SamplerDescription description)
            => LogResourceCreated(new VkSampler(this, description));

        protected override Shader CreateShaderCore(in ShaderDescription description)
            => LogResourceCreated(new VkShader(this, description));

        protected override Texture CreateTextureCore(in TextureDescription description)
            => LogResourceCreated(new VkTexture(this, description));

        public override Texture CreateTexture(ulong nativeTexture, in TextureDescription description)
            => LogResourceCreated(
                new VkTexture(
                    this,
                    description.Width, description.Height,
                    description.MipLevels, description.ArrayLayers,
                    VkFormats.VdToVkPixelFormat(description.Format, (description.Usage & TextureUsage.DepthStencil) != 0),
                    description.Usage,
                    description.SampleCount,
                    new VkImage(new((nint)nativeTexture)))
                );
        

        protected override TextureView CreateTextureViewCore(in TextureViewDescription description)
            => LogResourceCreated(new VkTextureView(this, description));

        protected override DeviceBuffer CreateBufferCore(in BufferDescription description)
            => LogResourceCreated(new VkBuffer(this, description.SizeInBytes, description.Usage));
        
        public override Fence CreateFence(bool signaled)
            => LogResourceCreated(new VkFence(this, signaled));

        public override Swapchain CreateSwapchain(in SwapchainDescription description)
            => LogResourceCreated(new VkSwapchain(this, description));

        private TResource LogResourceCreated<TResource>(TResource resource) where TResource : GraphicsObject
        {
            _onResourceCreated?.Invoke(resource);
            return resource;
        }
    }
}

using static XenoAtom.Interop.vulkan;

using static XenoAtom.Graphics.Vk.VulkanUtil;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace XenoAtom.Graphics.Vk
{
    internal unsafe class VkSwapchainFramebuffer : VkFramebufferBase
    {
        private readonly VkSwapchain _swapchain;
        private readonly VkSurfaceKHR _surface;
        private readonly PixelFormat? _depthFormat;
        private uint _currentImageIndex;

        private VkFramebuffer?[] _scFramebuffers = [null];
        private VkImage[] _scImages = [];
        private VkFormat _scImageFormat;
        private VkExtent2D _scExtent;
        private FramebufferAttachment[][] _scColorTextures = [];

        private FramebufferAttachment? _depthAttachment;
        private uint _desiredWidth;
        private uint _desiredHeight;
        private OutputDescription _outputDescription;

        public override XenoAtom.Interop.vulkan.VkFramebuffer CurrentFramebuffer => _scFramebuffers[(int)_currentImageIndex]!.CurrentFramebuffer;

        public override VkRenderPass RenderPassNoClear_Init => _scFramebuffers[0]!.RenderPassNoClear_Init;

        public override VkRenderPass RenderPassNoClear_Load => _scFramebuffers[0]!.RenderPassNoClear_Load;

        public override VkRenderPass RenderPassClear => _scFramebuffers[0]!.RenderPassClear;

        public override ReadOnlySpan<FramebufferAttachment> ColorTargets => _scColorTextures[(int)_currentImageIndex];

        public override FramebufferAttachment? DepthTarget => _depthAttachment;

        public override uint RenderableWidth => _scExtent.width;

        public override uint RenderableHeight => _scExtent.height;

        public override uint Width => _desiredWidth;
        public override uint Height => _desiredHeight;

        public uint ImageIndex => _currentImageIndex;

        public override OutputDescription OutputDescription => _outputDescription;

        public override uint AttachmentCount { get; }

        public VkSwapchain Swapchain => _swapchain;

        public VkSwapchainFramebuffer(
            VkGraphicsDevice gd,
            VkSwapchain swapchain,
            VkSurfaceKHR surface,
            uint width,
            uint height,
            PixelFormat? depthFormat)
            : base(gd)
        {
            _swapchain = swapchain;
            _surface = surface;
            _depthFormat = depthFormat;

            AttachmentCount = depthFormat.HasValue ? 2u : 1u; // 1 Color + 1 Depth
        }

        internal void SetImageIndex(uint index)
        {
            _currentImageIndex = index;
        }

        internal void SetNewSwapchain(
            VkSwapchainKHR deviceSwapchain,
            uint width,
            uint height,
            VkSurfaceFormatKHR surfaceFormat,
            VkExtent2D swapchainExtent)
        {
            _desiredWidth = width;
            _desiredHeight = height;

            // Get the images
            uint scImageCount = 0;
            VkResult result = Device.vkGetSwapchainImagesKHR.Invoke(Device, deviceSwapchain, out scImageCount);
            CheckResult(result);
            if (_scImages.Length < scImageCount)
            {
                _scImages = new VkImage[(int)scImageCount];
            }
            result = Device.vkGetSwapchainImagesKHR.Invoke(Device, deviceSwapchain, _scImages);
            CheckResult(result);

            _scImageFormat = surfaceFormat.format;
            _scExtent = swapchainExtent;

            CreateDepthTexture();
            CreateFramebuffers();

            _outputDescription = OutputDescription.CreateFromFramebuffer(this);
        }

        private void DestroySwapchainFramebuffers()
        {
            if (_scFramebuffers != null)
            {
                for (int i = 0; i < _scFramebuffers.Length; i++)
                {
                    _scFramebuffers[i]?.Dispose();
                    _scFramebuffers[i] = null;
                }
                Array.Clear(_scFramebuffers, 0, _scFramebuffers.Length);
            }
        }

        private void CreateDepthTexture()
        {
            if (_depthFormat.HasValue)
            {
                _depthAttachment?.Target.Dispose();
                VkTexture depthTexture = (VkTexture)Device.CreateTexture(TextureDescription.Texture2D(
                    Math.Max(1, _scExtent.width),
                    Math.Max(1, _scExtent.height),
                    1,
                    1,
                    _depthFormat.Value,
                    TextureUsage.DepthStencil));
                _depthAttachment = new FramebufferAttachment(depthTexture, 0);
            }
        }

        private void CreateFramebuffers()
        {
            for (int i = 0; i < _scFramebuffers.Length; i++)
            {
                _scFramebuffers[i]?.Dispose();
                _scFramebuffers[i] = null;
            }
            Array.Clear(_scFramebuffers, 0, _scFramebuffers.Length);

            Util.EnsureArrayMinimumSize(ref _scFramebuffers, (uint)_scImages.Length);
            Util.EnsureArrayMinimumSize(ref _scColorTextures, (uint)_scImages.Length);
            for (uint i = 0; i < _scImages.Length; i++)
            {
                VkTexture colorTex = new VkTexture(
                    Device,
                    Math.Max(1, _scExtent.width),
                    Math.Max(1, _scExtent.height),
                    1,
                    1,
                    _scImageFormat,
                    TextureUsage.RenderTarget,
                    TextureSampleCount.Count1,
                    _scImages[i]);
                FramebufferDescription desc = new FramebufferDescription(_depthAttachment?.Target, colorTex);
                VkFramebuffer fb = new VkFramebuffer(Device, desc, true);
                _scFramebuffers[i] = fb;
                _scColorTextures[i] = new FramebufferAttachment[] { new FramebufferAttachment(colorTex, 0) };
            }
        }

        public override void TransitionToIntermediateLayout(VkCommandBuffer cb)
        {
            foreach (var ca in ColorTargets)
            {
                VkTexture vkTex = Util.AssertSubtype<Texture, VkTexture>(ca.Target);
                vkTex.SetImageLayout(0, ca.ArrayLayer, VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL);
            }
        }

        public override void TransitionToFinalLayout(VkCommandBuffer cb)
        {
            foreach (var ca in ColorTargets)
            {
                VkTexture vkTex = Util.AssertSubtype<Texture, VkTexture>(ca.Target);
                vkTex.TransitionImageLayout(cb, 0, 1, ca.ArrayLayer, 1, VK_IMAGE_LAYOUT_PRESENT_SRC_KHR);
            }
        }

        internal override void Destroy()
        {
            _depthAttachment?.Target.Dispose();
            DestroySwapchainFramebuffers();
        }
    }
}

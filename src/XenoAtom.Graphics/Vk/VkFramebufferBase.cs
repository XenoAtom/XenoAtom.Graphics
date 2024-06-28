using System.Collections.Generic;
using System.Runtime.CompilerServices;
using static XenoAtom.Interop.vulkan;

namespace XenoAtom.Graphics.Vk
{
    internal abstract class VkFramebufferBase : Framebuffer
    {
        internal VkGraphicsDevice _gd => Unsafe.As<GraphicsDevice, VkGraphicsDevice>(ref Unsafe.AsRef(in Device));

        public VkFramebufferBase(
            VkGraphicsDevice gd,
            FramebufferAttachmentDescription? depthTexture,
            IReadOnlyList<FramebufferAttachmentDescription> colorTextures)
            : base(gd, depthTexture, colorTextures)
        {
        }

        public VkFramebufferBase(VkGraphicsDevice device) : base(device)
        {
        }

        public abstract uint RenderableWidth { get; }

        public abstract uint RenderableHeight { get; }

        public abstract XenoAtom.Interop.vulkan.VkFramebuffer CurrentFramebuffer { get; }
        public abstract VkRenderPass RenderPassNoClear_Init { get; }
        public abstract VkRenderPass RenderPassNoClear_Load { get; }
        public abstract VkRenderPass RenderPassClear { get; }
        public abstract uint AttachmentCount { get; }
        public abstract void TransitionToIntermediateLayout(VkCommandBuffer cb);
        public abstract void TransitionToFinalLayout(VkCommandBuffer cb);
    }
}

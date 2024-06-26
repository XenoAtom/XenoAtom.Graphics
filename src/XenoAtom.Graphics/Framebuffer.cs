using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace XenoAtom.Graphics
{
    /// <summary>
    /// A device resource used to control which color and depth textures are rendered to.
    /// See <see cref="FramebufferDescription"/>.
    /// </summary>
    public abstract class Framebuffer : IDeviceResource, IDisposable
    {
        private readonly FramebufferAttachment[] _colorTargets = [];

        /// <summary>
        /// Gets the depth attachment associated with this instance. May be null if no depth texture is used.
        /// </summary>
        public virtual FramebufferAttachment? DepthTarget { get; }

        /// <summary>
        /// Gets the collection of color attachments associated with this instance. May be empty.
        /// </summary>
        public virtual ReadOnlySpan<FramebufferAttachment> ColorTargets => _colorTargets;

        /// <summary>
        /// Gets an <see cref="XenoAtom.Graphics.OutputDescription"/> which describes the number and formats of the depth and color targets
        /// in this instance.
        /// </summary>
        public virtual OutputDescription OutputDescription { get; }

        /// <summary>
        /// Gets the width of the <see cref="Framebuffer"/>.
        /// </summary>
        public virtual uint Width { get; }

        /// <summary>
        /// Gets the height of the <see cref="Framebuffer"/>.
        /// </summary>
        public virtual uint Height { get; }

        internal Framebuffer()
        {
        }

        internal Framebuffer(
            FramebufferAttachmentDescription? depthTargetDesc,
            IReadOnlyList<FramebufferAttachmentDescription> colorTargetDescs)
        {
            FramebufferAttachment? depthTarget = null;
            if (depthTargetDesc != null)
            {
                FramebufferAttachmentDescription depthAttachment = depthTargetDesc.Value;
                depthTarget = new FramebufferAttachment(
                    depthAttachment.Target,
                    depthAttachment.ArrayLayer,
                    depthAttachment.MipLevel);
            }
            FramebufferAttachment[] colorTargets = new FramebufferAttachment[colorTargetDescs.Count];
            for (int i = 0; i < colorTargets.Length; i++)
            {
                colorTargets[i] = new FramebufferAttachment(
                    colorTargetDescs[i].Target,
                    colorTargetDescs[i].ArrayLayer,
                    colorTargetDescs[i].MipLevel);
            }

            if (colorTargetDescs.Count == 0 && depthTarget is null)
            {
                throw new ArgumentException("At least one color target or depth target must be specified.");
            }

            DepthTarget = depthTarget;
            _colorTargets = colorTargets;

            Texture dimTex;
            uint mipLevel;
            if (colorTargets.Length > 0)
            {
                dimTex = colorTargets[0].Target;
                mipLevel = colorTargets[0].MipLevel;
            }
            else
            {
                dimTex = depthTarget!.Value.Target;
                mipLevel = depthTarget.Value.MipLevel;
            }

            Util.GetMipDimensions(dimTex, mipLevel, out uint mipWidth, out uint mipHeight, out _);
            Width = mipWidth;
            Height = mipHeight;


            OutputDescription = OutputDescription.CreateFromFramebuffer(this);
        }

        /// <summary>
        /// A string identifying this instance. Can be used to differentiate between objects in graphics debuggers and other
        /// tools.
        /// </summary>
        public abstract string? Name { get; set; }

        /// <summary>
        /// A bool indicating whether this instance has been disposed.
        /// </summary>
        public abstract bool IsDisposed { get; }

        /// <summary>
        /// Frees unmanaged device resources controlled by this instance.
        /// </summary>
        public abstract void Dispose();
    }
}

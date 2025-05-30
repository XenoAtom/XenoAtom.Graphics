using System;

namespace XenoAtom.Graphics
{
    /// <summary>
    /// A device resource used to store arbitrary image data in a specific format.
    /// See <see cref="TextureDescription"/>.
    /// </summary>
    public abstract class Texture : GraphicsDeviceObject, IMappableResource, IBindableResource
    {
        private readonly object _fullTextureViewLock = new();
        private TextureView? _fullTextureView;

        internal Texture(GraphicsDevice device) : base(device)
        {
        }

        /// <summary>
        /// Calculates the subresource index, given a mipmap level and array layer.
        /// </summary>
        /// <param name="mipLevel">The mip level. This should be less than <see cref="MipLevels"/>.</param>
        /// <param name="arrayLayer">The array layer. This should be less than <see cref="ArrayLayers"/>.</param>
        /// <returns>The subresource index.</returns>
        public uint CalculateSubresource(uint mipLevel, uint arrayLayer)
        {
            return arrayLayer * MipLevels + mipLevel;
        }

        /// <summary>
        /// The handle to the platform specific texture handle.
        /// </summary>
        /// <remarks>
        /// For Vulkan, this is a <c>VkImage</c>.
        /// </remarks>
        public abstract nint Handle { get; }

        /// <summary>
        /// The format of individual texture elements stored in this instance.
        /// </summary>
        public abstract PixelFormat Format { get; }
        /// <summary>
        /// The total width of this instance, in texels.
        /// </summary>
        public abstract uint Width { get; }
        /// <summary>
        /// The total height of this instance, in texels.
        /// </summary>
        public abstract uint Height { get; }
        /// <summary>
        /// The total depth of this instance, in texels.
        /// </summary>
        public abstract uint Depth { get; }
        /// <summary>
        /// The total number of mipmap levels in this instance.
        /// </summary>
        public abstract uint MipLevels { get; }
        /// <summary>
        /// The total number of array layers in this instance.
        /// </summary>
        public abstract uint ArrayLayers { get; }
        /// <summary>
        /// The usage flags given when this instance was created. This property controls how this instance is permitted to be
        /// used, and it is an error to attempt to use the Texture outside of those contexts.
        /// </summary>
        public abstract TextureUsage Usage { get; }
        /// <summary>
        /// The <see cref="TextureKind"/> of this instance.
        /// </summary>
        public abstract TextureKind Kind { get; }
        /// <summary>
        /// The number of samples in this instance. If this returns any value other than <see cref="TextureSampleCount.Count1"/>,
        /// then this instance is a multipsample texture.
        /// </summary>
        public abstract TextureSampleCount SampleCount { get; }

        internal TextureView GetFullTextureView(GraphicsDevice gd)
        {
            lock (_fullTextureViewLock)
            {
                if (_fullTextureView == null)
                {
                    _fullTextureView = CreateFullTextureView(gd);
                }

                return _fullTextureView;
            }
        }

        private protected virtual TextureView CreateFullTextureView(GraphicsDevice gd)
        {
            return gd.CreateTextureView(this);
        }

        internal void DisposeTextureView()
        {
            lock (_fullTextureViewLock)
            {
                _fullTextureView?.Dispose();
            }
        }
    }
}

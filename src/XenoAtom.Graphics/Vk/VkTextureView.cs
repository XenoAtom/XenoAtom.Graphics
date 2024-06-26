using static XenoAtom.Interop.vulkan;
using static XenoAtom.Graphics.Vk.VulkanUtil;


namespace XenoAtom.Graphics.Vk
{
    internal unsafe class VkTextureView : TextureView
    {
        private readonly VkGraphicsDevice _gd;
        private readonly VkImageView _imageView;
        private bool _destroyed;
        private string? _name;

        public VkImageView ImageView => _imageView;

        public new VkTexture Target => (VkTexture)base.Target;

        public ResourceRefCount RefCount { get; }

        public override bool IsDisposed => _destroyed;

        public VkTextureView(VkGraphicsDevice gd, ref TextureViewDescription description)
            : base(ref description)
        {
            _gd = gd;
            VkImageViewCreateInfo imageViewCI = new VkImageViewCreateInfo();
            VkTexture tex = Util.AssertSubtype<Texture, VkTexture>(description.Target);
            imageViewCI.image = tex.OptimalDeviceImage;
            imageViewCI.format = VkFormats.VdToVkPixelFormat(Format, (Target.Usage & TextureUsage.DepthStencil) != 0);

            VkImageAspectFlagBits aspectFlags;
            if ((description.Target.Usage & TextureUsage.DepthStencil) == TextureUsage.DepthStencil)
            {
                aspectFlags = VK_IMAGE_ASPECT_DEPTH_BIT;
            }
            else
            {
                aspectFlags = VK_IMAGE_ASPECT_COLOR_BIT;
            }

            imageViewCI.subresourceRange = new VkImageSubresourceRange()
            {
                aspectMask = aspectFlags,
                baseMipLevel = description.BaseMipLevel,
                levelCount = description.MipLevels,
                baseArrayLayer = description.BaseArrayLayer,
                layerCount = description.ArrayLayers
            };

            if ((tex.Usage & TextureUsage.Cubemap) == TextureUsage.Cubemap)
            {
                imageViewCI.viewType = description.ArrayLayers == 1 ? VK_IMAGE_VIEW_TYPE_CUBE : VK_IMAGE_VIEW_TYPE_CUBE_ARRAY;
                imageViewCI.subresourceRange.layerCount *= 6;
            }
            else
            {
                switch (tex.Type)
                {
                    case TextureType.Texture1D:
                        imageViewCI.viewType = description.ArrayLayers == 1
                            ? VK_IMAGE_VIEW_TYPE_1D
                            : VK_IMAGE_VIEW_TYPE_1D_ARRAY;
                        break;
                    case TextureType.Texture2D:
                        imageViewCI.viewType = description.ArrayLayers == 1
                            ? VK_IMAGE_VIEW_TYPE_2D
                            : VK_IMAGE_VIEW_TYPE_2D_ARRAY;
                        break;
                    case TextureType.Texture3D:
                        imageViewCI.viewType = VK_IMAGE_VIEW_TYPE_3D;
                        break;
                }
            }

            vkCreateImageView(_gd.Device, imageViewCI, null, out _imageView);
            RefCount = new ResourceRefCount(DisposeCore);
        }

        public override string? Name
        {
            get => _name;
            set
            {
                _name = value;
                _gd.SetResourceName(this, value);
            }
        }

        public override void Dispose()
        {
            RefCount.Decrement();
        }

        private void DisposeCore()
        {
            if (!_destroyed)
            {
                _destroyed = true;
                vkDestroyImageView(_gd.Device, ImageView, null);
            }
        }
    }
}

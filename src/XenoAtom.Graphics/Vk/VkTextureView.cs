using static XenoAtom.Interop.vulkan;
using static XenoAtom.Graphics.Vk.VulkanUtil;
using System.Runtime.CompilerServices;


namespace XenoAtom.Graphics.Vk
{
    internal unsafe class VkTextureView : TextureView
    {
        private VkGraphicsDevice _gd => Unsafe.As<GraphicsDevice, VkGraphicsDevice>(ref Unsafe.AsRef(in Device));
        private readonly VkImageView _imageView;

        public VkImageView ImageView => _imageView;

        public new VkTexture Target => (VkTexture)base.Target;

        public VkTextureView(VkGraphicsDevice gd, ref TextureViewDescription description)
            : base(gd, ref description)
        {
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
        }

        internal override void DisposeCore()
        {
            vkDestroyImageView(_gd.Device, ImageView, null);
        }
    }
}

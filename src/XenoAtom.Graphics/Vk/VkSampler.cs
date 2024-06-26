using System.Runtime.CompilerServices;
using static XenoAtom.Interop.vulkan;


namespace XenoAtom.Graphics.Vk
{
    internal unsafe class VkSampler : Sampler
    {
        public new VkGraphicsDevice Device => Unsafe.As<GraphicsDevice, VkGraphicsDevice>(ref Unsafe.AsRef(in base.Device));

        private readonly XenoAtom.Interop.vulkan.VkSampler _sampler;

        public XenoAtom.Interop.vulkan.VkSampler DeviceSampler => _sampler;

        public VkSampler(VkGraphicsDevice gd, in SamplerDescription description) : base(gd)
        {
            VkFormats.GetFilterParams(description.Filter, out VkFilter minFilter, out VkFilter magFilter, out VkSamplerMipmapMode mipmapMode);

            VkSamplerCreateInfo samplerCI = new VkSamplerCreateInfo
            {
                addressModeU = VkFormats.VdToVkSamplerAddressMode(description.AddressModeU),
                addressModeV = VkFormats.VdToVkSamplerAddressMode(description.AddressModeV),
                addressModeW = VkFormats.VdToVkSamplerAddressMode(description.AddressModeW),
                minFilter = minFilter,
                magFilter = magFilter,
                mipmapMode = mipmapMode,
                compareEnable = description.ComparisonKind != null,
                compareOp = description.ComparisonKind != null
                    ? VkFormats.VdToVkCompareOp(description.ComparisonKind.Value)
                    : VK_COMPARE_OP_NEVER,
                anisotropyEnable = description.Filter == SamplerFilter.Anisotropic,
                maxAnisotropy = description.MaximumAnisotropy,
                minLod = description.MinimumLod,
                maxLod = description.MaximumLod,
                mipLodBias = description.LodBias,
                borderColor = VkFormats.VdToVkSamplerBorderColor(description.BorderColor)
            };

            vkCreateSampler(Device, samplerCI, null, out _sampler);
        }

        internal override void Destroy()
        {
            vkDestroySampler(Device, _sampler, null);
        }
    }
}

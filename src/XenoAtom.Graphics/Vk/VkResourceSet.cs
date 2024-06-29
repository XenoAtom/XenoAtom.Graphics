using System.Collections.Generic;
using static XenoAtom.Interop.vulkan;

using static XenoAtom.Graphics.Vk.VulkanUtil;
using System.Runtime.CompilerServices;

namespace XenoAtom.Graphics.Vk
{
    internal unsafe class VkResourceSet : ResourceSet
    {
        private new VkGraphicsDevice Device => Unsafe.As<GraphicsDevice, VkGraphicsDevice>(ref Unsafe.AsRef(in base.Device));
        private readonly DescriptorResourceCounts _descriptorCounts;
        private readonly DescriptorAllocationToken _descriptorAllocationToken;
        private readonly List<ResourceRefCount> _refCounts = new List<ResourceRefCount>();

        public VkDescriptorSet DescriptorSet => _descriptorAllocationToken.Set;

        private readonly List<VkTexture> _sampledTextures = new List<VkTexture>();
        public List<VkTexture> SampledTextures => _sampledTextures;
        private readonly List<VkTexture> _storageImages = new List<VkTexture>();
        public List<VkTexture> StorageTextures => _storageImages;

        public List<ResourceRefCount> RefCounts => _refCounts;

        public VkResourceSet(VkGraphicsDevice gd, in ResourceSetDescription description)
            : base(gd, description)
        {
            VkResourceLayout vkLayout = Util.AssertSubtype<ResourceLayout, VkResourceLayout>(description.Layout);

            VkDescriptorSetLayout dsl = vkLayout.DescriptorSetLayout;
            _descriptorCounts = vkLayout.DescriptorResourceCounts;
            _descriptorAllocationToken = Device.DescriptorPoolManager.Allocate(_descriptorCounts, dsl);

            IBindableResource[] boundResources = description.BoundResources;
            uint descriptorWriteCount = (uint)boundResources.Length;
            VkWriteDescriptorSet* descriptorWrites = stackalloc VkWriteDescriptorSet[(int)descriptorWriteCount];
            VkDescriptorBufferInfo* bufferInfos = stackalloc VkDescriptorBufferInfo[(int)descriptorWriteCount];
            VkDescriptorImageInfo* imageInfos = stackalloc VkDescriptorImageInfo[(int)descriptorWriteCount];

            for (int i = 0; i < descriptorWriteCount; i++)
            {
                VkDescriptorType type = vkLayout.DescriptorTypes[i];

                descriptorWrites[i].sType = VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET;
                descriptorWrites[i].descriptorCount = 1;
                descriptorWrites[i].descriptorType = type;
                descriptorWrites[i].dstBinding = (uint)i;
                descriptorWrites[i].dstSet = _descriptorAllocationToken.Set;

                if (type == VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER || type == VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER_DYNAMIC
                                                              || type == VK_DESCRIPTOR_TYPE_STORAGE_BUFFER || type == VK_DESCRIPTOR_TYPE_STORAGE_BUFFER_DYNAMIC)
                {
                    DeviceBufferRange range = Util.GetBufferRange(boundResources[i], 0);
                    VkBuffer rangedVkBuffer = Util.AssertSubtype<DeviceBuffer, VkBuffer>(range.Buffer);
                    bufferInfos[i].buffer = rangedVkBuffer.DeviceBuffer;
                    bufferInfos[i].offset = range.Offset;
                    bufferInfos[i].range = range.SizeInBytes;
                    descriptorWrites[i].pBufferInfo = &bufferInfos[i];
                    _refCounts.Add(rangedVkBuffer);
                }
                else if (type == VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE)
                {
                    TextureView texView = Util.GetTextureView(Device, boundResources[i]);
                    VkTextureView vkTexView = Util.AssertSubtype<TextureView, VkTextureView>(texView);
                    imageInfos[i].imageView = vkTexView.ImageView;
                    imageInfos[i].imageLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
                    descriptorWrites[i].pImageInfo = &imageInfos[i];
                    _sampledTextures.Add(Util.AssertSubtype<Texture, VkTexture>(texView.Target));
                    _refCounts.Add(vkTexView);
                }
                else if (type == VK_DESCRIPTOR_TYPE_STORAGE_IMAGE)
                {
                    TextureView texView = Util.GetTextureView(Device, boundResources[i]);
                    VkTextureView vkTexView = Util.AssertSubtype<TextureView, VkTextureView>(texView);
                    imageInfos[i].imageView = vkTexView.ImageView;
                    imageInfos[i].imageLayout = VK_IMAGE_LAYOUT_GENERAL;
                    descriptorWrites[i].pImageInfo = &imageInfos[i];
                    _storageImages.Add(Util.AssertSubtype<Texture, VkTexture>(texView.Target));
                    _refCounts.Add(vkTexView);
                }
                else if (type == VK_DESCRIPTOR_TYPE_SAMPLER)
                {
                    VkSampler sampler = Util.AssertSubtype<IBindableResource, VkSampler>(boundResources[i]);
                    imageInfos[i].sampler = sampler.DeviceSampler;
                    descriptorWrites[i].pImageInfo = &imageInfos[i];
                    _refCounts.Add(sampler);
                }
            }

            vkUpdateDescriptorSets(Device, descriptorWriteCount, descriptorWrites, 0, null);
        }

        internal override void Destroy()
        {
            Device.DescriptorPoolManager.Free(_descriptorAllocationToken, _descriptorCounts);
        }
    }
}

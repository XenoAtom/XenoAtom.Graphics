using static XenoAtom.Interop.vulkan;

using static XenoAtom.Graphics.Vk.VulkanUtil;
using System.Runtime.CompilerServices;

namespace XenoAtom.Graphics.Vk
{
    internal unsafe class VkResourceLayout : ResourceLayout
    {
        internal new VkGraphicsDevice Device => Unsafe.As<GraphicsDevice, VkGraphicsDevice>(ref Unsafe.AsRef(in base.Device));

        private readonly VkDescriptorSetLayout _dsl;
        private readonly VkDescriptorType[] _descriptorTypes;

        public VkDescriptorSetLayout DescriptorSetLayout => _dsl;
        public VkDescriptorType[] DescriptorTypes => _descriptorTypes;

        public readonly DescriptorResourceCounts DescriptorResourceCounts;

        public new int DynamicBufferCount { get; }

        public VkResourceLayout(VkGraphicsDevice gd, in ResourceLayoutDescription description)
            : base(gd, description)
        {
            VkDescriptorSetLayoutCreateInfo dslCI = new VkDescriptorSetLayoutCreateInfo();
            ResourceLayoutElementDescription[] elements = description.Elements;
            _descriptorTypes = new VkDescriptorType[elements.Length];
            VkDescriptorSetLayoutBinding* bindings = stackalloc VkDescriptorSetLayoutBinding[elements.Length];

            uint uniformBufferCount = 0;
            uint uniformBufferDynamicCount = 0;
            uint sampledImageCount = 0;
            uint samplerCount = 0;
            uint storageBufferCount = 0;
            uint storageBufferDynamicCount = 0;
            uint storageImageCount = 0;

            for (uint i = 0; i < elements.Length; i++)
            {
                bindings[i].binding = i;
                bindings[i].descriptorCount = 1;
                VkDescriptorType descriptorType = VkFormats.VdToVkDescriptorType(elements[i].Kind, elements[i].Options);
                bindings[i].descriptorType = descriptorType;
                bindings[i].stageFlags = VkFormats.VdToVkShaderStages(elements[i].Stages);
                if ((elements[i].Options & ResourceLayoutElementOptions.DynamicBinding) != 0)
                {
                    DynamicBufferCount += 1;
                }

                _descriptorTypes[i] = descriptorType;

                switch (descriptorType)
                {
                    case VK_DESCRIPTOR_TYPE_SAMPLER:
                        samplerCount += 1;
                        break;
                    case VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE:
                        sampledImageCount += 1;
                        break;
                    case VK_DESCRIPTOR_TYPE_STORAGE_IMAGE:
                        storageImageCount += 1;
                        break;
                    case VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER:
                        uniformBufferCount += 1;
                        break;
                    case VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER_DYNAMIC:
                        uniformBufferDynamicCount += 1;
                        break;
                    case VK_DESCRIPTOR_TYPE_STORAGE_BUFFER:
                        storageBufferCount += 1;
                        break;
                    case VK_DESCRIPTOR_TYPE_STORAGE_BUFFER_DYNAMIC:
                        storageBufferDynamicCount += 1;
                        break;
                }
            }

            DescriptorResourceCounts = new DescriptorResourceCounts(
                uniformBufferCount,
                uniformBufferDynamicCount,
                sampledImageCount,
                samplerCount,
                storageBufferCount,
                storageBufferDynamicCount,
                storageImageCount);

            dslCI.bindingCount = (uint)elements.Length;
            dslCI.pBindings = bindings;

            VkResult result = vkCreateDescriptorSetLayout(Device, dslCI, null, out _dsl);
            CheckResult(result);
        }

        internal override void Destroy()
        {
            vkDestroyDescriptorSetLayout(Device, _dsl, null);
        }
    }
}

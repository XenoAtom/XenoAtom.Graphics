using System;
using System.Collections.Generic;
using System.Diagnostics;
using static XenoAtom.Interop.vulkan;


namespace XenoAtom.Graphics.Vk
{
    internal class VkDescriptorPoolManager
    {
        private readonly VkGraphicsDevice _gd;
        private readonly List<PoolInfo> _pools = new List<PoolInfo>();
        private readonly object _lock = new object();

        public VkDescriptorPoolManager(VkGraphicsDevice gd)
        {
            _gd = gd;
            _pools.Add(CreateNewPool());
        }

        public unsafe DescriptorAllocationToken Allocate(DescriptorResourceCounts counts, VkDescriptorSetLayout setLayout)
        {
            lock (_lock)
            {
                VkDescriptorPool pool = GetPool(counts);
                VkDescriptorSetAllocateInfo dsAI = new VkDescriptorSetAllocateInfo();
                dsAI.descriptorSetCount = 1;
                dsAI.pSetLayouts = &setLayout;
                dsAI.descriptorPool = pool;
                VkDescriptorSet set;
                VkResult result = vkAllocateDescriptorSets(_gd.VkDevice, dsAI, &set);
                VulkanUtil.CheckResult(result);

                return new DescriptorAllocationToken(set, pool);
            }
        }

        public void Free(DescriptorAllocationToken token, DescriptorResourceCounts counts)
        {
            lock (_lock)
            {
                foreach (PoolInfo poolInfo in _pools)
                {
                    if (poolInfo.Pool == token.Pool)
                    {
                        poolInfo.Free(_gd.VkDevice, token, counts);
                    }
                }
            }
        }

        private VkDescriptorPool GetPool(DescriptorResourceCounts counts)
        {
            lock (_lock)
            {
                foreach (PoolInfo poolInfo in _pools)
                {
                    if (poolInfo.Allocate(counts))
                    {
                        return poolInfo.Pool;
                    }
                }

                PoolInfo newPool = CreateNewPool();
                _pools.Add(newPool);
                bool result = newPool.Allocate(counts);
                Debug.Assert(result);
                return newPool.Pool;
            }
        }

        private unsafe PoolInfo CreateNewPool()
        {
            uint totalSets = 1000;
            uint descriptorCount = 100;
            uint poolSizeCount = 7;
            VkDescriptorPoolSize* sizes = stackalloc VkDescriptorPoolSize[(int)poolSizeCount];
            sizes[0].type = VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER;
            sizes[0].descriptorCount = descriptorCount;
            sizes[1].type = VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE;
            sizes[1].descriptorCount = descriptorCount;
            sizes[2].type = VK_DESCRIPTOR_TYPE_SAMPLER;
            sizes[2].descriptorCount = descriptorCount;
            sizes[3].type = VK_DESCRIPTOR_TYPE_STORAGE_BUFFER;
            sizes[3].descriptorCount = descriptorCount;
            sizes[4].type = VK_DESCRIPTOR_TYPE_STORAGE_IMAGE;
            sizes[4].descriptorCount = descriptorCount;
            sizes[5].type = VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER_DYNAMIC;
            sizes[5].descriptorCount = descriptorCount;
            sizes[6].type = VK_DESCRIPTOR_TYPE_STORAGE_BUFFER_DYNAMIC;
            sizes[6].descriptorCount = descriptorCount;

            VkDescriptorPoolCreateInfo poolCI = new VkDescriptorPoolCreateInfo();
            poolCI.flags = VK_DESCRIPTOR_POOL_CREATE_FREE_DESCRIPTOR_SET_BIT;
            poolCI.maxSets = totalSets;
            poolCI.pPoolSizes = sizes;
            poolCI.poolSizeCount = poolSizeCount;

            VkResult result = vkCreateDescriptorPool(_gd.VkDevice, poolCI, null, out VkDescriptorPool descriptorPool);
            VulkanUtil.CheckResult(result);

            return new PoolInfo(descriptorPool, totalSets, descriptorCount);
        }

        internal unsafe void DestroyAll()
        {
            foreach (PoolInfo poolInfo in _pools)
            {
                vkDestroyDescriptorPool(_gd.VkDevice, poolInfo.Pool, null);
            }
        }

        private class PoolInfo
        {
            public readonly VkDescriptorPool Pool;

            public uint RemainingSets;

            public uint UniformBufferCount;
            public uint UniformBufferDynamicCount;
            public uint SampledImageCount;
            public uint SamplerCount;
            public uint StorageBufferCount;
            public uint StorageBufferDynamicCount;
            public uint StorageImageCount;

            public PoolInfo(VkDescriptorPool pool, uint totalSets, uint descriptorCount)
            {
                Pool = pool;
                RemainingSets = totalSets;
                UniformBufferCount = descriptorCount;
                UniformBufferDynamicCount = descriptorCount;
                SampledImageCount = descriptorCount;
                SamplerCount = descriptorCount;
                StorageBufferCount = descriptorCount;
                StorageBufferDynamicCount = descriptorCount;
                StorageImageCount = descriptorCount;
            }

            internal bool Allocate(DescriptorResourceCounts counts)
            {
                if (RemainingSets > 0
                    && UniformBufferCount >= counts.UniformBufferCount
                    && UniformBufferDynamicCount >= counts.UniformBufferDynamicCount
                    && SampledImageCount >= counts.SampledImageCount
                    && SamplerCount >= counts.SamplerCount
                    && StorageBufferCount >= counts.StorageBufferCount
                    && StorageBufferDynamicCount >= counts.StorageBufferDynamicCount
                    && StorageImageCount >= counts.StorageImageCount)
                {
                    RemainingSets -= 1;
                    UniformBufferCount -= counts.UniformBufferCount;
                    UniformBufferDynamicCount -= counts.UniformBufferDynamicCount;
                    SampledImageCount -= counts.SampledImageCount;
                    SamplerCount -= counts.SamplerCount;
                    StorageBufferCount -= counts.StorageBufferCount;
                    StorageBufferDynamicCount -= counts.StorageBufferDynamicCount;
                    StorageImageCount -= counts.StorageImageCount;
                    return true;
                }
                else
                {
                    return false;
                }
            }

            internal unsafe void Free(VkDevice device, DescriptorAllocationToken token, DescriptorResourceCounts counts)
            {
                VkDescriptorSet set = token.Set;
                vkFreeDescriptorSets(device, Pool, 1, &set);

                RemainingSets += 1;

                UniformBufferCount += counts.UniformBufferCount;
                SampledImageCount += counts.SampledImageCount;
                SamplerCount += counts.SamplerCount;
                StorageBufferCount += counts.StorageBufferCount;
                StorageImageCount += counts.StorageImageCount;
            }
        }
    }

    internal struct DescriptorAllocationToken
    {
        public readonly VkDescriptorSet Set;
        public readonly VkDescriptorPool Pool;

        public DescriptorAllocationToken(VkDescriptorSet set, VkDescriptorPool pool)
        {
            Set = set;
            Pool = pool;
        }
    }
}

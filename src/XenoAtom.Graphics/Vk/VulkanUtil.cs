using System;
using System.Diagnostics;
using XenoAtom.Interop;
using static XenoAtom.Interop.vulkan;


namespace XenoAtom.Graphics.Vk
{
    internal static unsafe class VulkanUtil
    {
        private static Lazy<bool> s_isVulkanLoaded = new Lazy<bool>(TryLoadVulkan);

        [Conditional("DEBUG")]
        public static void CheckResult(VkResult result)
        {
            if (result != VK_SUCCESS)
            {
                throw new GraphicsException("Unsuccessful VkResult: " + result);
            }
        }

        public static bool TryFindMemoryType(VkPhysicalDeviceMemoryProperties memProperties, uint typeFilter, VkMemoryPropertyFlagBits properties, out uint typeIndex)
        {
            typeIndex = 0;

            for (int i = 0; i < memProperties.memoryTypeCount; i++)
            {
                if (((typeFilter & (1 << i)) != 0)
                    && (memProperties.GetMemoryType((uint)i).propertyFlags & properties) == properties)
                {
                    typeIndex = (uint)i;
                    return true;
                }
            }

            return false;
        }

        public static ReadOnlyMemoryUtf8[] EnumerateInstanceLayers()
        {
            uint propCount = 0;
            VkResult result = vkEnumerateInstanceLayerProperties(out propCount);
            CheckResult(result);
            if (propCount == 0)
            {
                return Array.Empty<ReadOnlyMemoryUtf8>();
            }

            VkLayerProperties[] props = new VkLayerProperties[propCount];
            vkEnumerateInstanceLayerProperties(props);

            var ret = new ReadOnlyMemoryUtf8[propCount];
            for (int i = 0; i < propCount; i++)
            {
                fixed (byte* layerNamePtr = props[i].layerName)
                {
                    ret[i] = new ReadOnlyMemoryUtf8(layerNamePtr);
                }
            }

            return ret;
        }

        public static ReadOnlyMemoryUtf8[] GetInstanceExtensions() => EnumerateInstanceExtensions();


        public static ReadOnlyMemoryUtf8[] EnumerateInstanceExtensions()
        {
            if (!IsVulkanLoaded())
            {
                return Array.Empty<ReadOnlyMemoryUtf8>();
            }

            uint propCount = 0;
            VkResult result = vkEnumerateInstanceExtensionProperties(null, out propCount);
            if (result != VK_SUCCESS)
            {
                return Array.Empty<ReadOnlyMemoryUtf8>();
            }

            if (propCount == 0)
            {
                return Array.Empty<ReadOnlyMemoryUtf8>();
            }

            VkExtensionProperties[] props = new VkExtensionProperties[propCount];
            vkEnumerateInstanceExtensionProperties(null, props);

            ReadOnlyMemoryUtf8[] ret = new ReadOnlyMemoryUtf8[propCount];
            for (int i = 0; i < propCount; i++)
            {
                fixed (byte* extensionNamePtr = props[i].extensionName)
                {
                    ret[i] = new ReadOnlyMemoryUtf8(extensionNamePtr);
                }
            }

            return ret;
        }

        public static bool IsVulkanLoaded() => s_isVulkanLoaded.Value;

        private static bool TryLoadVulkan()
        {
            try
            {
                uint propCount;
                vkEnumerateInstanceExtensionProperties((byte*)null, &propCount, null);
                return true;
            }
            catch { return false; }
        }

        public static void TransitionImageLayout(
            VkCommandBuffer cb,
            VkImage image,
            uint baseMipLevel,
            uint levelCount,
            uint baseArrayLayer,
            uint layerCount,
            VkImageAspectFlags aspectMask,
            VkImageLayout oldLayout,
            VkImageLayout newLayout)
        {
            Debug.Assert(oldLayout != newLayout);
            VkImageMemoryBarrier barrier = new VkImageMemoryBarrier();
            barrier.oldLayout = oldLayout;
            barrier.newLayout = newLayout;
            barrier.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
            barrier.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
            barrier.image = image;
            barrier.subresourceRange.aspectMask = aspectMask;
            barrier.subresourceRange.baseMipLevel = baseMipLevel;
            barrier.subresourceRange.levelCount = levelCount;
            barrier.subresourceRange.baseArrayLayer = baseArrayLayer;
            barrier.subresourceRange.layerCount = layerCount;

            VkPipelineStageFlagBits srcStageFlags = VK_PIPELINE_STAGE_NONE;
            VkPipelineStageFlagBits dstStageFlags = VK_PIPELINE_STAGE_NONE;

            if ((oldLayout == VK_IMAGE_LAYOUT_UNDEFINED || oldLayout == VK_IMAGE_LAYOUT_PREINITIALIZED) && newLayout == VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL)
            {
                barrier.srcAccessMask = VK_ACCESS_NONE;
                barrier.dstAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
                srcStageFlags = VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT;
                dstStageFlags = VK_PIPELINE_STAGE_TRANSFER_BIT;
            }
            else if (oldLayout == VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL && newLayout == VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL)
            {
                barrier.srcAccessMask = VK_ACCESS_SHADER_READ_BIT;
                barrier.dstAccessMask = VK_ACCESS_TRANSFER_READ_BIT;
                srcStageFlags = VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT;
                dstStageFlags = VK_PIPELINE_STAGE_TRANSFER_BIT;
            }
            else if (oldLayout == VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL && newLayout == VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL)
            {
                barrier.srcAccessMask = VK_ACCESS_SHADER_READ_BIT;
                barrier.dstAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
                srcStageFlags = VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT;
                dstStageFlags = VK_PIPELINE_STAGE_TRANSFER_BIT;
            }
            else if (oldLayout == VK_IMAGE_LAYOUT_PREINITIALIZED && newLayout == VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL)
            {
                barrier.srcAccessMask = VK_ACCESS_NONE;
                barrier.dstAccessMask = VK_ACCESS_TRANSFER_READ_BIT;
                srcStageFlags = VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT;
                dstStageFlags = VK_PIPELINE_STAGE_TRANSFER_BIT;
            }
            else if (oldLayout == VK_IMAGE_LAYOUT_PREINITIALIZED && newLayout == VK_IMAGE_LAYOUT_GENERAL)
            {
                barrier.srcAccessMask = VK_ACCESS_NONE;
                barrier.dstAccessMask = VK_ACCESS_SHADER_READ_BIT;
                srcStageFlags = VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT;
                dstStageFlags = VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT;
            }
            else if (oldLayout == VK_IMAGE_LAYOUT_PREINITIALIZED && newLayout == VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL)
            {
                barrier.srcAccessMask = VK_ACCESS_NONE;
                barrier.dstAccessMask = VK_ACCESS_SHADER_READ_BIT;
                srcStageFlags = VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT;
                dstStageFlags = VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT;
            }
            else if (oldLayout == VK_IMAGE_LAYOUT_GENERAL && newLayout == VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL)
            {
                barrier.srcAccessMask = VK_ACCESS_TRANSFER_READ_BIT;
                barrier.dstAccessMask = VK_ACCESS_SHADER_READ_BIT;
                srcStageFlags = VK_PIPELINE_STAGE_TRANSFER_BIT;
                dstStageFlags = VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT;
            }
            else if (oldLayout == VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL && newLayout == VK_IMAGE_LAYOUT_GENERAL)
            {
                barrier.srcAccessMask = VK_ACCESS_SHADER_READ_BIT;
                barrier.dstAccessMask = VK_ACCESS_SHADER_READ_BIT;
                srcStageFlags = VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT;
                dstStageFlags = VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT;
            }

            else if (oldLayout == VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL && newLayout == VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL)
            {
                barrier.srcAccessMask = VK_ACCESS_TRANSFER_READ_BIT;
                barrier.dstAccessMask = VK_ACCESS_SHADER_READ_BIT;
                srcStageFlags = VK_PIPELINE_STAGE_TRANSFER_BIT;
                dstStageFlags = VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT;
            }
            else if (oldLayout == VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL && newLayout == VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL)
            {
                barrier.srcAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
                barrier.dstAccessMask = VK_ACCESS_SHADER_READ_BIT;
                srcStageFlags = VK_PIPELINE_STAGE_TRANSFER_BIT;
                dstStageFlags = VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT;
            }
            else if (oldLayout == VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL && newLayout == VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL)
            {
                barrier.srcAccessMask = VK_ACCESS_TRANSFER_READ_BIT;
                barrier.dstAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
                srcStageFlags = VK_PIPELINE_STAGE_TRANSFER_BIT;
                dstStageFlags = VK_PIPELINE_STAGE_TRANSFER_BIT;
            }
            else if (oldLayout == VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL && newLayout == VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL)
            {
                barrier.srcAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
                barrier.dstAccessMask = VK_ACCESS_TRANSFER_READ_BIT;
                srcStageFlags = VK_PIPELINE_STAGE_TRANSFER_BIT;
                dstStageFlags = VK_PIPELINE_STAGE_TRANSFER_BIT;
            }
            else if (oldLayout == VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL && newLayout == VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL)
            {
                barrier.srcAccessMask = VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT;
                barrier.dstAccessMask = VK_ACCESS_TRANSFER_READ_BIT;
                srcStageFlags = VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT;
                dstStageFlags = VK_PIPELINE_STAGE_TRANSFER_BIT;
            }
            else if (oldLayout == VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL && newLayout == VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL)
            {
                barrier.srcAccessMask = VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT;
                barrier.dstAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
                srcStageFlags = VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT;
                dstStageFlags = VK_PIPELINE_STAGE_TRANSFER_BIT;
            }
            else if (oldLayout == VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL && newLayout == VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL)
            {
                barrier.srcAccessMask = VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT;
                barrier.dstAccessMask = VK_ACCESS_SHADER_READ_BIT;
                srcStageFlags = VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT;
                dstStageFlags = VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT;
            }
            else if (oldLayout == VK_IMAGE_LAYOUT_DEPTH_STENCIL_ATTACHMENT_OPTIMAL && newLayout == VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL)
            {
                barrier.srcAccessMask = VK_ACCESS_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT;
                barrier.dstAccessMask = VK_ACCESS_SHADER_READ_BIT;
                srcStageFlags = VK_PIPELINE_STAGE_LATE_FRAGMENT_TESTS_BIT;
                dstStageFlags = VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT;
            }
            else if (oldLayout == VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL && newLayout == VK_IMAGE_LAYOUT_PRESENT_SRC_KHR)
            {
                barrier.srcAccessMask = VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT;
                barrier.dstAccessMask = VK_ACCESS_MEMORY_READ_BIT;
                srcStageFlags = VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT;
                dstStageFlags = VK_PIPELINE_STAGE_BOTTOM_OF_PIPE_BIT;
            }
            else if (oldLayout == VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL && newLayout == VK_IMAGE_LAYOUT_PRESENT_SRC_KHR)
            {
                barrier.srcAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
                barrier.dstAccessMask = VK_ACCESS_MEMORY_READ_BIT;
                srcStageFlags = VK_PIPELINE_STAGE_TRANSFER_BIT;
                dstStageFlags = VK_PIPELINE_STAGE_BOTTOM_OF_PIPE_BIT;
            }
            else if (oldLayout == VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL && newLayout == VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL)
            {
                barrier.srcAccessMask = VK_ACCESS_TRANSFER_READ_BIT;
                barrier.dstAccessMask = VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT;
                srcStageFlags = VK_PIPELINE_STAGE_TRANSFER_BIT;
                dstStageFlags = VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT;
            }
            else if (oldLayout == VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL && newLayout == VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL)
            {
                barrier.srcAccessMask = VK_ACCESS_TRANSFER_READ_BIT;
                barrier.dstAccessMask = VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT;
                srcStageFlags = VK_PIPELINE_STAGE_TRANSFER_BIT;
                dstStageFlags = VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT;
            }
            else if (oldLayout == VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL && newLayout == VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL)
            {
                barrier.srcAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
                barrier.dstAccessMask = VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT;
                srcStageFlags = VK_PIPELINE_STAGE_TRANSFER_BIT;
                dstStageFlags = VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT;
            }
            else if (oldLayout == VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL && newLayout == VK_IMAGE_LAYOUT_DEPTH_STENCIL_ATTACHMENT_OPTIMAL)
            {
                barrier.srcAccessMask = VK_ACCESS_TRANSFER_READ_BIT;
                barrier.dstAccessMask = VK_ACCESS_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT;
                srcStageFlags = VK_PIPELINE_STAGE_TRANSFER_BIT;
                dstStageFlags = VK_PIPELINE_STAGE_LATE_FRAGMENT_TESTS_BIT;
            }
            else if (oldLayout == VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL && newLayout == VK_IMAGE_LAYOUT_DEPTH_STENCIL_ATTACHMENT_OPTIMAL)
            {
                barrier.srcAccessMask = VK_ACCESS_TRANSFER_READ_BIT;
                barrier.dstAccessMask = VK_ACCESS_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT;
                srcStageFlags = VK_PIPELINE_STAGE_TRANSFER_BIT;
                dstStageFlags = VK_PIPELINE_STAGE_LATE_FRAGMENT_TESTS_BIT;
            }
            else if (oldLayout == VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL && newLayout == VK_IMAGE_LAYOUT_DEPTH_STENCIL_ATTACHMENT_OPTIMAL)
            {
                barrier.srcAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
                barrier.dstAccessMask = VK_ACCESS_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT;
                srcStageFlags = VK_PIPELINE_STAGE_TRANSFER_BIT;
                dstStageFlags = VK_PIPELINE_STAGE_LATE_FRAGMENT_TESTS_BIT;
            }
            else if (oldLayout == VK_IMAGE_LAYOUT_GENERAL && newLayout == VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL)
            {
                barrier.srcAccessMask = VK_ACCESS_SHADER_WRITE_BIT;
                barrier.dstAccessMask = VK_ACCESS_TRANSFER_READ_BIT;
                srcStageFlags = VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT;
                dstStageFlags = VK_PIPELINE_STAGE_TRANSFER_BIT;
            }
            else if (oldLayout == VK_IMAGE_LAYOUT_GENERAL && newLayout == VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL)
            {
                barrier.srcAccessMask = VK_ACCESS_SHADER_WRITE_BIT;
                barrier.dstAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
                srcStageFlags = VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT;
                dstStageFlags = VK_PIPELINE_STAGE_TRANSFER_BIT;
            }
            else if (oldLayout == VK_IMAGE_LAYOUT_PRESENT_SRC_KHR && newLayout == VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL)
            {
                barrier.srcAccessMask = VK_ACCESS_MEMORY_READ_BIT;
                barrier.dstAccessMask = VK_ACCESS_TRANSFER_READ_BIT;
                srcStageFlags = VK_PIPELINE_STAGE_BOTTOM_OF_PIPE_BIT;
                dstStageFlags = VK_PIPELINE_STAGE_TRANSFER_BIT;
            }
            else
            {
                Debug.Fail($"Invalid image layout transition. {oldLayout} -> {newLayout}");
            }

            vkCmdPipelineBarrier(
                cb,
                srcStageFlags,
                dstStageFlags,
                (VkDependencyFlagBits)0,
                0, null,
                0, null,
                1, &barrier);
        }
    }

    internal unsafe static class VkPhysicalDeviceMemoryPropertiesEx
    {
        public static VkMemoryType GetMemoryType(this VkPhysicalDeviceMemoryProperties memoryProperties, uint index)
        {
            return (&memoryProperties.memoryTypes[0])[index];
        }
    }
}

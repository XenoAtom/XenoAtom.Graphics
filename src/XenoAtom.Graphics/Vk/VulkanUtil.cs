using System;
using System.Diagnostics;
using XenoAtom.Interop;
using static XenoAtom.Interop.vulkan;


namespace XenoAtom.Graphics.Vk
{
    internal static unsafe class VulkanUtil
    {
        private static Lazy<bool> s_isVulkanLoaded = new Lazy<bool>(TryLoadVulkan);
        
        public static VkPipelineStageFlags ToVkPipelineStage(GraphicsPipelineStage stage)
        {
            return stage switch
            {
                GraphicsPipelineStage.None => 0,
                GraphicsPipelineStage.TopOfPipe => VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT,
                GraphicsPipelineStage.BottomOfPipe => VK_PIPELINE_STAGE_BOTTOM_OF_PIPE_BIT,
                GraphicsPipelineStage.VertexShader => VK_PIPELINE_STAGE_VERTEX_SHADER_BIT,
                GraphicsPipelineStage.TessellationControlShader => VK_PIPELINE_STAGE_TESSELLATION_CONTROL_SHADER_BIT,
                GraphicsPipelineStage.TessellationEvaluationShader => VK_PIPELINE_STAGE_TESSELLATION_EVALUATION_SHADER_BIT,
                GraphicsPipelineStage.GeometryShader => VK_PIPELINE_STAGE_GEOMETRY_SHADER_BIT,
                GraphicsPipelineStage.FragmentShader => VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT,
                GraphicsPipelineStage.EarlyFragmentTests => VK_PIPELINE_STAGE_EARLY_FRAGMENT_TESTS_BIT,
                GraphicsPipelineStage.LateFragmentTests => VK_PIPELINE_STAGE_LATE_FRAGMENT_TESTS_BIT,
                GraphicsPipelineStage.ColorAttachmentOutput => VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT,
                GraphicsPipelineStage.ComputeShader => VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT,
                GraphicsPipelineStage.Transfer => VK_PIPELINE_STAGE_TRANSFER_BIT,
                GraphicsPipelineStage.AllGraphics => VK_PIPELINE_STAGE_ALL_GRAPHICS_BIT,
                GraphicsPipelineStage.AllCommands => VK_PIPELINE_STAGE_ALL_COMMANDS_BIT,
                _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, null)
            };

        }

        public static void CheckResult(VkResult result)
        {
            if (result != VK_SUCCESS)
            {
                throw new GraphicsException("Unsuccessful VkResult: " + result);
            }
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

            VkPipelineStageFlags srcStageFlags;
            VkPipelineStageFlags dstStageFlags;

            barrier.srcAccessMask = GetDefaultTransitionImageLayoutFlags(oldLayout, out srcStageFlags, true);
            barrier.dstAccessMask = GetDefaultTransitionImageLayoutFlags(newLayout, out dstStageFlags, false);

            // Special cases for transitions
            if (oldLayout == VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL)
            {
                if (newLayout == VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL || newLayout == VK_IMAGE_LAYOUT_DEPTH_STENCIL_ATTACHMENT_OPTIMAL)
                {
                    barrier.srcAccessMask = VK_ACCESS_TRANSFER_READ_BIT;
                    srcStageFlags = VK_PIPELINE_STAGE_TRANSFER_BIT;
                }
            }
            else if (oldLayout == VK_IMAGE_LAYOUT_GENERAL)
            {
                if (newLayout == VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL || newLayout == VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL)
                {
                    barrier.srcAccessMask = VK_ACCESS_SHADER_WRITE_BIT;
                    srcStageFlags = VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT;
                }
            }

            vkCmdPipelineBarrier(
                cb,
                srcStageFlags,
                dstStageFlags,
                (VkDependencyFlags)0,
                0, null,
                0, null,
                1, &barrier);
        }

        private static VkAccessFlags GetDefaultTransitionImageLayoutFlags(VkImageLayout layout, out VkPipelineStageFlags stageFlags, bool isSrc)
        {
            // Return VkAccessFlags and VkPipelineStageFlags according to layout
            VkAccessFlags accessFlags;

            // Switch
            switch (layout)
            {
                case VK_IMAGE_LAYOUT_UNDEFINED:
                case VK_IMAGE_LAYOUT_PREINITIALIZED:
                    accessFlags = VK_ACCESS_NONE;
                    stageFlags = VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT;
                    break;

                case VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL:
                    accessFlags = VK_ACCESS_TRANSFER_WRITE_BIT;
                    stageFlags = VK_PIPELINE_STAGE_TRANSFER_BIT;
                    break;

                case VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL:
                    accessFlags = VK_ACCESS_TRANSFER_READ_BIT;
                    stageFlags = VK_PIPELINE_STAGE_TRANSFER_BIT;
                    break;

                case VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL:
                    accessFlags = VK_ACCESS_SHADER_READ_BIT;
                    stageFlags = VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT;
                    break;

                case VK_IMAGE_LAYOUT_GENERAL:
                    if (isSrc)
                    {
                        accessFlags = VK_ACCESS_TRANSFER_READ_BIT;
                        stageFlags = VK_PIPELINE_STAGE_TRANSFER_BIT;
                    }
                    else
                    {
                        accessFlags = VK_ACCESS_SHADER_READ_BIT;
                        stageFlags = VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT;
                    }
                    break;

                case VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL:
                    accessFlags = VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT;
                    stageFlags = VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT;
                    break;

                case VK_IMAGE_LAYOUT_DEPTH_STENCIL_ATTACHMENT_OPTIMAL:
                    accessFlags = VK_ACCESS_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT;
                    stageFlags = VK_PIPELINE_STAGE_LATE_FRAGMENT_TESTS_BIT;
                    break;

                case VK_IMAGE_LAYOUT_PRESENT_SRC_KHR:
                    accessFlags = VK_ACCESS_MEMORY_READ_BIT;
                    stageFlags = VK_PIPELINE_STAGE_BOTTOM_OF_PIPE_BIT;
                    break;

                default:
                    throw new NotImplementedException($"The transition for image layout {layout} is not implemented or valid.");
            }

            return accessFlags;
        }
    }

    internal static unsafe class VkPhysicalDeviceMemoryPropertiesEx
    {
        public static VkMemoryType GetMemoryType(this VkPhysicalDeviceMemoryProperties memoryProperties, uint index)
        {
            return (&memoryProperties.memoryTypes[0])[index];
        }
    }
}

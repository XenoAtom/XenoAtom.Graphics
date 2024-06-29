using static XenoAtom.Interop.vulkan;

using static XenoAtom.Graphics.Vk.VulkanUtil;
using System.Diagnostics;
using System;
using System.Runtime.CompilerServices;

namespace XenoAtom.Graphics.Vk
{
    internal unsafe class VkTexture : Texture
    {
        private VkGraphicsDevice _gd => Unsafe.As<GraphicsDevice, VkGraphicsDevice>(ref Unsafe.AsRef(in Device));
        private readonly VkImage _optimalImage;
        private readonly VkMemoryBlock _memoryBlock;
        private readonly XenoAtom.Interop.vulkan.VkBuffer _stagingBuffer;
        private PixelFormat _format; // Static for regular images -- may change for shared staging images
        private readonly uint _actualImageArrayLayers;

        // Immutable except for shared staging Textures.
        private uint _width;
        private uint _height;
        private uint _depth;

        public override uint Width => _width;

        public override uint Height => _height;

        public override uint Depth => _depth;

        public override PixelFormat Format => _format;

        public override uint MipLevels { get; }

        public override uint ArrayLayers { get; }
        public uint ActualArrayLayers => _actualImageArrayLayers;

        public override TextureUsage Usage { get; }

        public override TextureType Type { get; }

        public override TextureSampleCount SampleCount { get; }

        public VkImage OptimalDeviceImage => _optimalImage;
        public XenoAtom.Interop.vulkan.VkBuffer StagingBuffer => _stagingBuffer;
        public VkMemoryBlock Memory => _memoryBlock;

        public VkFormat VkFormat { get; }
        public VkSampleCountFlags VkSampleCount { get; }

        private readonly VkImageLayout[] _imageLayouts = [];

        public bool IsSwapchainTexture { get; }

        internal VkTexture(VkGraphicsDevice gd, in TextureDescription description) : base(gd)
        {
            _width = description.Width;
            _height = description.Height;
            _depth = description.Depth;
            MipLevels = description.MipLevels;
            ArrayLayers = description.ArrayLayers;
            bool isCubemap = ((description.Usage) & TextureUsage.Cubemap) == TextureUsage.Cubemap;
            _actualImageArrayLayers = isCubemap
                ? 6 * ArrayLayers
                : ArrayLayers;
            _format = description.Format;
            Usage = description.Usage;
            Type = description.Type;
            SampleCount = description.SampleCount;
            VkSampleCount = VkFormats.VdToVkSampleCount(SampleCount);
            VkFormat = VkFormats.VdToVkPixelFormat(Format, (description.Usage & TextureUsage.DepthStencil) == TextureUsage.DepthStencil);

            bool isStaging = (Usage & TextureUsage.Staging) == TextureUsage.Staging;

            if (!isStaging)
            {
                VkImageCreateInfo imageCI = new VkImageCreateInfo();
                imageCI.mipLevels = MipLevels;
                imageCI.arrayLayers = _actualImageArrayLayers;
                imageCI.imageType = VkFormats.VdToVkTextureType(Type);
                imageCI.extent.width = Width;
                imageCI.extent.height = Height;
                imageCI.extent.depth = Depth;
                imageCI.initialLayout = VK_IMAGE_LAYOUT_PREINITIALIZED;
                imageCI.usage = VkFormats.VdToVkTextureUsage(Usage);
                imageCI.tiling = isStaging ? VK_IMAGE_TILING_LINEAR : VK_IMAGE_TILING_OPTIMAL;
                imageCI.format = VkFormat;
                imageCI.flags = VK_IMAGE_CREATE_MUTABLE_FORMAT_BIT;

                imageCI.samples = VkSampleCount;
                if (isCubemap)
                {
                    imageCI.flags |= VK_IMAGE_CREATE_CUBE_COMPATIBLE_BIT;
                }

                uint subresourceCount = MipLevels * _actualImageArrayLayers * Depth;
                VkResult result = vkCreateImage(gd.Device, imageCI, null, out _optimalImage);
                CheckResult(result);

                VkMemoryRequirements memoryRequirements;
                bool prefersDedicatedAllocation;
                VkImageMemoryRequirementsInfo2 memReqsInfo2 = new VkImageMemoryRequirementsInfo2();
                memReqsInfo2.image = _optimalImage;
                VkMemoryRequirements2 memReqs2 = new VkMemoryRequirements2();
                VkMemoryDedicatedRequirements dedicatedReqs = new VkMemoryDedicatedRequirements();
                memReqs2.pNext = &dedicatedReqs;
                vkGetImageMemoryRequirements2(_gd.Device, &memReqsInfo2, &memReqs2);
                memoryRequirements = memReqs2.memoryRequirements;
                prefersDedicatedAllocation = dedicatedReqs.prefersDedicatedAllocation || dedicatedReqs.requiresDedicatedAllocation;

                VkMemoryBlock memoryToken = gd.MemoryManager.Allocate(
                    memoryRequirements.memoryTypeBits,
                    VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT,
                    false,
                    memoryRequirements.size,
                    memoryRequirements.alignment,
                    prefersDedicatedAllocation,
                    _optimalImage,
                    default);
                _memoryBlock = memoryToken;
                result = vkBindImageMemory(gd.Device, _optimalImage, _memoryBlock.DeviceMemory, _memoryBlock.Offset);
                CheckResult(result);

                _imageLayouts = new VkImageLayout[subresourceCount];
                for (int i = 0; i < _imageLayouts.Length; i++)
                {
                    _imageLayouts[i] = VK_IMAGE_LAYOUT_PREINITIALIZED;
                }
            }
            else // isStaging
            {
                uint depthPitch = FormatHelpers.GetDepthPitch(
                    FormatHelpers.GetRowPitch(Width, Format),
                    Height,
                    Format);
                uint stagingSize = depthPitch * Depth;
                for (uint level = 1; level < MipLevels; level++)
                {
                    Util.GetMipDimensions(this, level, out uint mipWidth, out uint mipHeight, out uint mipDepth);

                    depthPitch = FormatHelpers.GetDepthPitch(
                        FormatHelpers.GetRowPitch(mipWidth, Format),
                        mipHeight,
                        Format);

                    stagingSize += depthPitch * mipDepth;
                }
                stagingSize *= ArrayLayers;

                VkBufferCreateInfo bufferCI = new VkBufferCreateInfo();
                bufferCI.usage = VK_BUFFER_USAGE_TRANSFER_SRC_BIT | VK_BUFFER_USAGE_TRANSFER_DST_BIT;
                bufferCI.size = stagingSize;
                VkResult result = vkCreateBuffer(_gd.Device, bufferCI, null, out _stagingBuffer);
                CheckResult(result);
                //_gd.DebugLog(DebugLogLevel.Info, DebugLogKind.General, $"(StagingBuffer Texture) VkBuffer Created 0x{_stagingBuffer.Value.Handle:X16}");

                VkMemoryRequirements bufferMemReqs;
                bool prefersDedicatedAllocation;

                VkBufferMemoryRequirementsInfo2 memReqInfo2 = new VkBufferMemoryRequirementsInfo2();
                memReqInfo2.buffer = _stagingBuffer;
                VkMemoryRequirements2 memReqs2 = new VkMemoryRequirements2();
                VkMemoryDedicatedRequirements dedicatedReqs = new VkMemoryDedicatedRequirements();
                memReqs2.pNext = &dedicatedReqs;
                vkGetBufferMemoryRequirements2(_gd.Device, &memReqInfo2, &memReqs2);
                bufferMemReqs = memReqs2.memoryRequirements;
                prefersDedicatedAllocation = dedicatedReqs.prefersDedicatedAllocation || dedicatedReqs.requiresDedicatedAllocation;

                // Use "host cached" memory when available, for better performance of GPU -> CPU transfers
                var propertyFlags = VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT | VK_MEMORY_PROPERTY_HOST_COHERENT_BIT | VK_MEMORY_PROPERTY_HOST_CACHED_BIT;
                if (!gd.MemoryManager.TryFindMemoryType(bufferMemReqs.memoryTypeBits, propertyFlags, out _))
                {
                    propertyFlags ^= VK_MEMORY_PROPERTY_HOST_CACHED_BIT;
                }
                _memoryBlock = _gd.MemoryManager.Allocate(
                    bufferMemReqs.memoryTypeBits,
                    propertyFlags,
                    true,
                    bufferMemReqs.size,
                    bufferMemReqs.alignment,
                    prefersDedicatedAllocation,
                    default,
                    _stagingBuffer);

                result = vkBindBufferMemory(_gd.Device, _stagingBuffer, _memoryBlock.DeviceMemory, _memoryBlock.Offset);
                CheckResult(result);
            }

            ClearIfRenderTarget();
            TransitionIfSampled();
        }

        // Used to construct Swapchain textures.
        internal VkTexture(
            VkGraphicsDevice gd,
            uint width,
            uint height,
            uint mipLevels,
            uint arrayLayers,
            VkFormat vkFormat,
            TextureUsage usage,
            TextureSampleCount sampleCount,
            VkImage existingImage) : base(gd)
        {
            Debug.Assert(width > 0 && height > 0);
            MipLevels = mipLevels;
            _width = width;
            _height = height;
            _depth = 1;
            VkFormat = vkFormat;
            _format = VkFormats.VkToVdPixelFormat(VkFormat);
            ArrayLayers = arrayLayers;
            Usage = usage;
            Type = TextureType.Texture2D;
            SampleCount = sampleCount;
            VkSampleCount = VkFormats.VdToVkSampleCount(sampleCount);
            _optimalImage = existingImage;
            _imageLayouts = new[] { VK_IMAGE_LAYOUT_UNDEFINED };
            IsSwapchainTexture = true;

            ClearIfRenderTarget();
        }

        private void ClearIfRenderTarget()
        {
            // If the image is going to be used as a render target, we need to clear the data before its first use.
            if ((Usage & TextureUsage.RenderTarget) != 0)
            {
                _gd.ClearColorTexture(this, new VkClearColorValue(0, 0, 0, 0));
            }
            else if ((Usage & TextureUsage.DepthStencil) != 0)
            {
                _gd.ClearDepthTexture(this, new VkClearDepthStencilValue(0, 0));
            }
        }

        private void TransitionIfSampled()
        {
            if ((Usage & TextureUsage.Sampled) != 0)
            {
                _gd.TransitionImageLayout(this, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL);
            }
        }

        internal VkSubresourceLayout GetSubresourceLayout(uint subresource)
        {
            bool staging = _stagingBuffer.Value.Handle != 0;
            Util.GetMipLevelAndArrayLayer(this, subresource, out uint mipLevel, out uint arrayLayer);
            if (!staging)
            {
                VkImageAspectFlags aspect = (Usage & TextureUsage.DepthStencil) == TextureUsage.DepthStencil
                  ? (VK_IMAGE_ASPECT_DEPTH_BIT | VK_IMAGE_ASPECT_STENCIL_BIT)
                  : VK_IMAGE_ASPECT_COLOR_BIT;
                VkImageSubresource imageSubresource = new VkImageSubresource
                {
                    arrayLayer = arrayLayer,
                    mipLevel = mipLevel,
                    aspectMask = aspect,
                };

                vkGetImageSubresourceLayout(_gd.Device, _optimalImage, imageSubresource, out VkSubresourceLayout layout);
                return layout;
            }
            else
            {
                uint blockSize = FormatHelpers.IsCompressedFormat(Format) ? 4u : 1u;
                Util.GetMipDimensions(this, mipLevel, out uint mipWidth, out uint mipHeight, out uint mipDepth);
                uint rowPitch = FormatHelpers.GetRowPitch(mipWidth, Format);
                uint depthPitch = FormatHelpers.GetDepthPitch(rowPitch, mipHeight, Format);

                VkSubresourceLayout layout = new VkSubresourceLayout()
                {
                    rowPitch = rowPitch,
                    depthPitch = depthPitch,
                    arrayPitch = depthPitch,
                    size = depthPitch,
                };
                layout.offset = Util.ComputeSubresourceOffset(this, mipLevel, arrayLayer);

                return layout;
            }
        }

        internal void TransitionImageLayout(
            VkCommandBuffer cb,
            uint baseMipLevel,
            uint levelCount,
            uint baseArrayLayer,
            uint layerCount,
            VkImageLayout newLayout)
        {
            if (_stagingBuffer != default)
            {
                return;
            }

            VkImageLayout oldLayout = _imageLayouts[CalculateSubresource(baseMipLevel, baseArrayLayer)];
#if DEBUG
            for (uint level = 0; level < levelCount; level++)
            {
                for (uint layer = 0; layer < layerCount; layer++)
                {
                    if (_imageLayouts[CalculateSubresource(baseMipLevel + level, baseArrayLayer + layer)] != oldLayout)
                    {
                        throw new GraphicsException("Unexpected image layout.");
                    }
                }
            }
#endif
            if (oldLayout != newLayout)
            {
                VkImageAspectFlags aspectMask;
                if ((Usage & TextureUsage.DepthStencil) != 0)
                {
                    aspectMask = FormatHelpers.IsStencilFormat(Format)
                        ? VK_IMAGE_ASPECT_DEPTH_BIT | VK_IMAGE_ASPECT_STENCIL_BIT
                        : VK_IMAGE_ASPECT_DEPTH_BIT;
                }
                else
                {
                    aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
                }
                VulkanUtil.TransitionImageLayout(
                    cb,
                    OptimalDeviceImage,
                    baseMipLevel,
                    levelCount,
                    baseArrayLayer,
                    layerCount,
                    aspectMask,
                    _imageLayouts[CalculateSubresource(baseMipLevel, baseArrayLayer)],
                    newLayout);

                for (uint level = 0; level < levelCount; level++)
                {
                    for (uint layer = 0; layer < layerCount; layer++)
                    {
                        _imageLayouts[CalculateSubresource(baseMipLevel + level, baseArrayLayer + layer)] = newLayout;
                    }
                }
            }
        }

        internal void TransitionImageLayoutNonmatching(
            VkCommandBuffer cb,
            uint baseMipLevel,
            uint levelCount,
            uint baseArrayLayer,
            uint layerCount,
            VkImageLayout newLayout)
        {
            if (_stagingBuffer != default)
            {
                return;
            }

            for (uint level = baseMipLevel; level < baseMipLevel + levelCount; level++)
            {
                for (uint layer = baseArrayLayer; layer < baseArrayLayer + layerCount; layer++)
                {
                    uint subresource = CalculateSubresource(level, layer);
                    VkImageLayout oldLayout = _imageLayouts[subresource];

                    if (oldLayout != newLayout)
                    {
                        VkImageAspectFlags aspectMask;
                        if ((Usage & TextureUsage.DepthStencil) != 0)
                        {
                            aspectMask = FormatHelpers.IsStencilFormat(Format)
                                ? VK_IMAGE_ASPECT_DEPTH_BIT | VK_IMAGE_ASPECT_STENCIL_BIT
                                : VK_IMAGE_ASPECT_DEPTH_BIT;
                        }
                        else
                        {
                            aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
                        }
                        VulkanUtil.TransitionImageLayout(
                            cb,
                            OptimalDeviceImage,
                            level,
                            1,
                            layer,
                            1,
                            aspectMask,
                            oldLayout,
                            newLayout);

                        _imageLayouts[subresource] = newLayout;
                    }
                }
            }
        }

        internal VkImageLayout GetImageLayout(uint mipLevel, uint arrayLayer)
        {
            return _imageLayouts[CalculateSubresource(mipLevel, arrayLayer)];
        }

        internal void SetStagingDimensions(uint width, uint height, uint depth, PixelFormat format)
        {
            Debug.Assert(_stagingBuffer != default);
            Debug.Assert(Usage == TextureUsage.Staging);
            _width = width;
            _height = height;
            _depth = depth;
            _format = format;
        }

        internal override void Destroy()
        {
            DisposeTextureView();

            bool isStaging = (Usage & TextureUsage.Staging) == TextureUsage.Staging;
            if (isStaging)
            {
                //_gd.DebugLog(DebugLogLevel.Info, DebugLogKind.General, $"(StagingBuffer Texture) VkBuffer Destroyed 0x{_stagingBuffer.Value.Handle:X16}");
                vkDestroyBuffer(_gd.Device, _stagingBuffer, null);
            }
            else
            {
                vkDestroyImage(_gd.Device, _optimalImage, null);
            }

            if (_memoryBlock.DeviceMemory.Value.Handle != 0)
            {
                _gd.MemoryManager.Free(_memoryBlock);
            }
        }

        internal void SetImageLayout(uint mipLevel, uint arrayLayer, VkImageLayout layout)
        {
            _imageLayouts[CalculateSubresource(mipLevel, arrayLayer)] = layout;
        }
    }
}

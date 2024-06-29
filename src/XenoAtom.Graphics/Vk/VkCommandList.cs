using System;
using static XenoAtom.Interop.vulkan;

using static XenoAtom.Graphics.Vk.VulkanUtil;
using System.Diagnostics;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Text;
using XenoAtom.Interop;
using System.Runtime.CompilerServices;

namespace XenoAtom.Graphics.Vk
{
    internal unsafe class VkCommandList : CommandList
    {
        private new VkGraphicsDevice Device => Unsafe.As<GraphicsDevice, VkGraphicsDevice>(ref Unsafe.AsRef(in base.Device));

        private readonly VkCommandPool _pool;
        private VkCommandBuffer _cb;

        private bool _commandBufferBegun;
        private bool _commandBufferEnded;
        private VkRect2D[] _scissorRects = [];

        private VkClearValue[] _clearValues = [];
        private bool[] _validColorClearValues = [];
        private VkClearValue? _depthClearValue;
        private readonly List<VkTexture> _preDrawSampledImages = new();

        // Graphics State
        private VkFramebufferBase? _currentFramebuffer;
        private bool _currentFramebufferEverActive;
        private VkRenderPass _activeRenderPass;
        private VkPipeline? _currentGraphicsPipeline;
        private BoundResourceSetInfo[] _currentGraphicsResourceSets = [];
        private bool[] _graphicsResourceSetsChanged = [];

        private bool _newFramebuffer; // Render pass cycle state

        // Compute State
        private VkPipeline? _currentComputePipeline;
        private BoundResourceSetInfo[] _currentComputeResourceSets = [];
        private bool[] _computeResourceSetsChanged = [];

        private readonly object _commandBufferListLock = new();
        private readonly Queue<VkCommandBuffer> _availableCommandBuffers = new();
        private readonly List<VkCommandBuffer> _submittedCommandBuffers = new();

        private StagingResourceInfo? _currentStagingInfo;
        private readonly object _stagingLock = new();
        private readonly Dictionary<VkCommandBuffer, StagingResourceInfo> _submittedStagingInfos = new();
        private readonly List<StagingResourceInfo> _availableStagingInfos = new();
        private readonly List<VkBuffer> _availableStagingBuffers = new();

        private readonly PFN_vkCmdBeginDebugUtilsLabelEXT vkCmdBeginDebugUtilsLabelExt;
        private readonly PFN_vkCmdEndDebugUtilsLabelEXT vkCmdEndDebugUtilsLabelExt;
        private readonly PFN_vkCmdInsertDebugUtilsLabelEXT vkCmdInsertDebugUtilsLabelExt;


        public VkCommandPool CommandPool => _pool;

        public VkCommandBuffer CommandBuffer => _cb;

        public VkCommandList(VkGraphicsDevice gd, in CommandListDescription description)
            : base(gd, in description, gd.Features, gd.UniformBufferMinOffsetAlignment, gd.StructuredBufferMinOffsetAlignment)
        {
            var manager = Device.Adapter.Manager;
            vkCmdBeginDebugUtilsLabelExt = manager.vkCmdBeginDebugUtilsLabelExt;
            vkCmdEndDebugUtilsLabelExt = manager.vkCmdEndDebugUtilsLabelExt;
            vkCmdInsertDebugUtilsLabelExt = manager.vkCmdInsertDebugUtilsLabelExt;
            
            var poolCInfo = new VkCommandPoolCreateInfo
            {
                flags = VK_COMMAND_POOL_CREATE_RESET_COMMAND_BUFFER_BIT,
                queueFamilyIndex = gd.MainQueueIndex
            };
            VkResult result = vkCreateCommandPool(Device, poolCInfo, null, out _pool);
            CheckResult(result);

            _cb = GetNextCommandBuffer();
        }

        private VkCommandBuffer GetNextCommandBuffer()
        {
            lock (_commandBufferListLock)
            {
                if (_availableCommandBuffers.Count > 0)
                {
                    VkCommandBuffer cachedCB = _availableCommandBuffers.Dequeue();
                    VkResult resetResult = vkResetCommandBuffer(cachedCB, (VkCommandBufferResetFlagBits)0);
                    CheckResult(resetResult);
                    return cachedCB;
                }
            }

            var cbAI = new VkCommandBufferAllocateInfo
            {
                commandPool = _pool,
                commandBufferCount = 1,
                level = VK_COMMAND_BUFFER_LEVEL_PRIMARY
            };
            VkCommandBuffer cb = default;
            VkResult result = vkAllocateCommandBuffers(Device, cbAI, &cb);
            CheckResult(result);
            return cb;
        }

        public void CommandBufferSubmitted(VkCommandBuffer cb)
        {
            EnsureBegin();
            AddReference();

            lock (_stagingLock)
            {
                _submittedStagingInfos.Add(cb, _currentStagingInfo!);
            }
            _currentStagingInfo = null;
        }

        public void CommandBufferCompleted(VkCommandBuffer completedCB)
        {
            lock (_commandBufferListLock)
            {
                for (int i = 0; i < _submittedCommandBuffers.Count; i++)
                {
                    VkCommandBuffer submittedCB = _submittedCommandBuffers[i];
                    if (submittedCB == completedCB)
                    {
                        _availableCommandBuffers.Enqueue(completedCB);
                        _submittedCommandBuffers.RemoveAt(i);
                        i -= 1;
                    }
                }
            }

            lock (_stagingLock)
            {
                if (_submittedStagingInfos.TryGetValue(completedCB, out var info))
                {
                    RecycleStagingInfo(info);
                    _submittedStagingInfos.Remove(completedCB);
                }
            }

            ReleaseReference();
        }

        public override void Begin()
        {
            if (_commandBufferBegun)
            {
                throw new GraphicsException(
                    "CommandList must be in its initial state, or End() must have been called, for Begin() to be valid to call.");
            }
            if (_commandBufferEnded)
            {
                _commandBufferEnded = false;
                _cb = GetNextCommandBuffer();
                if (_currentStagingInfo != null)
                {
                    RecycleStagingInfo(_currentStagingInfo);
                }
            }

            _currentStagingInfo = GetStagingResourceInfo();

            VkCommandBufferBeginInfo beginInfo = new() { flags = VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT };
            vkBeginCommandBuffer(_cb, beginInfo);
            _commandBufferBegun = true;

            ClearCachedState();
            _currentFramebuffer = null;
            _currentGraphicsPipeline = null;
            ClearSets(_currentGraphicsResourceSets);
            Util.ClearArray(_scissorRects);

            _currentComputePipeline = null;
            ClearSets(_currentComputeResourceSets);
        }

        private protected override void ClearColorTargetCore(uint index, RgbaFloat clearColor)
        {
            EnsureBegin();

            var clearValue = new VkClearValue
            {
                color = new VkClearColorValue(clearColor.R, clearColor.G, clearColor.B, clearColor.A)
            };

            if (_activeRenderPass != default)
            {
                var clearAttachment = new VkClearAttachment
                {
                    colorAttachment = index,
                    aspectMask = VK_IMAGE_ASPECT_COLOR_BIT,
                    clearValue = clearValue
                };

                Texture colorTex = _currentFramebuffer!.ColorTargets[(int)index].Target;
                var clearRect = new VkClearRect
                {
                    baseArrayLayer = 0,
                    layerCount = 1,
                    rect = new VkRect2D(0, 0, colorTex.Width, colorTex.Height)
                };

                vkCmdClearAttachments(_cb, 1, &clearAttachment, 1, &clearRect);
            }
            else
            {
                // Queue up the clear value for the next RenderPass.
                _clearValues[index] = clearValue;
                _validColorClearValues[index] = true;
            }
        }

        private protected override void ClearDepthStencilCore(float depth, byte stencil)
        {
            EnsureBegin();

            var clearValue = new VkClearValue { depthStencil = new VkClearDepthStencilValue(depth, stencil) };

            if (_activeRenderPass != default)
            {
                if (_currentFramebuffer!.DepthTarget is null)
                {
                    throw new InvalidOperationException("No depth target set");
                }

                VkImageAspectFlags aspect = FormatHelpers.IsStencilFormat(_currentFramebuffer.DepthTarget.Value.Target.Format)
                    ? VK_IMAGE_ASPECT_DEPTH_BIT | VK_IMAGE_ASPECT_STENCIL_BIT
                    : VK_IMAGE_ASPECT_DEPTH_BIT;
                var clearAttachment = new VkClearAttachment
                {
                    aspectMask = aspect,
                    clearValue = clearValue
                };

                uint renderableWidth = _currentFramebuffer.RenderableWidth;
                uint renderableHeight = _currentFramebuffer.RenderableHeight;
                if (renderableWidth > 0 && renderableHeight > 0)
                {
                    var clearRect = new VkClearRect
                    {
                        baseArrayLayer = 0,
                        layerCount = 1,
                        rect = new VkRect2D(0, 0, renderableWidth, renderableHeight)
                    };

                    vkCmdClearAttachments(_cb, 1, &clearAttachment, 1, &clearRect);
                }
            }
            else
            {
                // Queue up the clear value for the next RenderPass.
                _depthClearValue = clearValue;
            }
        }

        private protected override void DrawCore(uint vertexCount, uint instanceCount, uint vertexStart, uint instanceStart)
        {
            PreDrawCommand();
            vkCmdDraw(_cb, vertexCount, instanceCount, vertexStart, instanceStart);
        }

        private protected override void DrawIndexedCore(uint indexCount, uint instanceCount, uint indexStart, int vertexOffset, uint instanceStart)
        {
            PreDrawCommand();
            vkCmdDrawIndexed(_cb, indexCount, instanceCount, indexStart, vertexOffset, instanceStart);
        }

        protected override void DrawIndirectCore(DeviceBuffer indirectBuffer, uint offset, uint drawCount, uint stride)
        {
            PreDrawCommand();
            VkBuffer vkBuffer = Util.AssertSubtype<DeviceBuffer, VkBuffer>(indirectBuffer);
            _currentStagingInfo!.AddResource(vkBuffer);
            vkCmdDrawIndirect(_cb, vkBuffer.DeviceBuffer, offset, drawCount, stride);
        }

        protected override void DrawIndexedIndirectCore(DeviceBuffer indirectBuffer, uint offset, uint drawCount, uint stride)
        {
            PreDrawCommand();
            VkBuffer vkBuffer = Util.AssertSubtype<DeviceBuffer, VkBuffer>(indirectBuffer);
            _currentStagingInfo!.AddResource(vkBuffer);
            vkCmdDrawIndexedIndirect(_cb, vkBuffer.DeviceBuffer, offset, drawCount, stride);
        }

        private void PreDrawCommand()
        {
            EnsureBegin();
            EnsureGraphicsPipeline();

            TransitionImages(_preDrawSampledImages, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL);
            _preDrawSampledImages.Clear();

            EnsureRenderPassActive();

            FlushNewResourceSets(
                _currentGraphicsResourceSets,
                _graphicsResourceSetsChanged,
                _currentGraphicsPipeline!.ResourceSetCount,
                VK_PIPELINE_BIND_POINT_GRAPHICS,
                _currentGraphicsPipeline.PipelineLayout);
        }

        private void FlushNewResourceSets(
            BoundResourceSetInfo[] resourceSets,
            bool[] resourceSetsChanged,
            uint resourceSetCount,
            VkPipelineBindPoint bindPoint,
            VkPipelineLayout pipelineLayout)
        {
            EnsureBegin();

            VkPipeline? pipeline = bindPoint == VK_PIPELINE_BIND_POINT_GRAPHICS ? _currentGraphicsPipeline : _currentComputePipeline;
            if (pipeline == null)
            {
                throw new InvalidOperationException($"Invalid call. The method `{nameof(SetPipeline)}` should have been called before");
            }

            VkDescriptorSet* descriptorSets = stackalloc VkDescriptorSet[(int)resourceSetCount];
            uint* dynamicOffsets = stackalloc uint[pipeline.DynamicOffsetsCount];
            uint currentBatchCount = 0;
            uint currentBatchFirstSet = 0;
            uint currentBatchDynamicOffsetCount = 0;

            for (uint currentSlot = 0; currentSlot < resourceSetCount; currentSlot++)
            {
                bool batchEnded = !resourceSetsChanged[currentSlot] || currentSlot == resourceSetCount - 1;

                if (resourceSetsChanged[currentSlot])
                {
                    resourceSetsChanged[currentSlot] = false;
                    VkResourceSet vkSet = Util.AssertSubtype<ResourceSet, VkResourceSet>(resourceSets[currentSlot].Set);
                    descriptorSets[currentBatchCount] = vkSet.DescriptorSet;
                    currentBatchCount += 1;

                    ref SmallFixedOrDynamicArray curSetOffsets = ref resourceSets[currentSlot].Offsets;
                    for (uint i = 0; i < curSetOffsets.Count; i++)
                    {
                        dynamicOffsets[currentBatchDynamicOffsetCount] = curSetOffsets.Get(i);
                        currentBatchDynamicOffsetCount += 1;
                    }

                    // Increment ref count on first use of a set.
                    _currentStagingInfo!.AddResource(vkSet);
                    for (int i = 0; i < vkSet.RefCounts.Count; i++)
                    {
                        _currentStagingInfo.AddResource(vkSet.RefCounts[i]);
                    }
                }

                if (batchEnded)
                {
                    if (currentBatchCount != 0)
                    {
                        // Flush current batch.
                        vkCmdBindDescriptorSets(
                            _cb,
                            bindPoint,
                            pipelineLayout,
                            currentBatchFirstSet,
                            currentBatchCount,
                            descriptorSets,
                            currentBatchDynamicOffsetCount,
                            dynamicOffsets);
                    }

                    currentBatchCount = 0;
                    currentBatchFirstSet = currentSlot + 1;
                }
            }
        }

        private void TransitionImages(List<VkTexture> sampledTextures, VkImageLayout layout)
        {
            for (int i = 0; i < sampledTextures.Count; i++)
            {
                VkTexture tex = sampledTextures[i];
                tex.TransitionImageLayout(_cb, 0, tex.MipLevels, 0, tex.ActualArrayLayers, layout);
            }
        }

        public override void Dispatch(uint groupCountX, uint groupCountY, uint groupCountZ)
        {
            PreDispatchCommand();

            vkCmdDispatch(_cb, groupCountX, groupCountY, groupCountZ);
        }

        private void PreDispatchCommand()
        {
            EnsureBegin();
            EnsureNoRenderPass();
            EnsureComputePipeline();

            for (uint currentSlot = 0; currentSlot < _currentComputePipeline!.ResourceSetCount; currentSlot++)
            {
                VkResourceSet vkSet = Util.AssertSubtype<ResourceSet, VkResourceSet>(
                    _currentComputeResourceSets[currentSlot].Set);

                TransitionImages(vkSet.SampledTextures, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL);
                TransitionImages(vkSet.StorageTextures, VK_IMAGE_LAYOUT_GENERAL);
                for (int texIdx = 0; texIdx < vkSet.StorageTextures.Count; texIdx++)
                {
                    VkTexture storageTex = vkSet.StorageTextures[texIdx];
                    if ((storageTex.Usage & TextureUsage.Sampled) != 0)
                    {
                        _preDrawSampledImages.Add(storageTex);
                    }
                }
            }

            FlushNewResourceSets(
                _currentComputeResourceSets,
                _computeResourceSetsChanged,
                _currentComputePipeline.ResourceSetCount,
                VK_PIPELINE_BIND_POINT_COMPUTE,
                _currentComputePipeline.PipelineLayout);
        }

        protected override void DispatchIndirectCore(DeviceBuffer indirectBuffer, uint offset)
        {
            PreDispatchCommand();

            VkBuffer vkBuffer = Util.AssertSubtype<DeviceBuffer, VkBuffer>(indirectBuffer);
            _currentStagingInfo!.AddResource(vkBuffer);
            vkCmdDispatchIndirect(_cb, vkBuffer.DeviceBuffer, offset);
        }

        protected override void ResolveTextureCore(Texture source, Texture destination)
        {
            EnsureBegin();

            if (_activeRenderPass != default)
            {
                EndCurrentRenderPass();
            }

            VkTexture vkSource = Util.AssertSubtype<Texture, VkTexture>(source);
            _currentStagingInfo!.AddResource(vkSource);
            VkTexture vkDestination = Util.AssertSubtype<Texture, VkTexture>(destination);
            _currentStagingInfo.AddResource(vkDestination);
            VkImageAspectFlags aspectFlags = ((source.Usage & TextureUsage.DepthStencil) == TextureUsage.DepthStencil)
                ? VK_IMAGE_ASPECT_DEPTH_BIT | VK_IMAGE_ASPECT_STENCIL_BIT
                : VK_IMAGE_ASPECT_COLOR_BIT;
            var region = new VkImageResolve
            {
                extent = new VkExtent3D { width = source.Width, height = source.Height, depth = source.Depth },
                srcSubresource = new VkImageSubresourceLayers { layerCount = 1, aspectMask = aspectFlags },
                dstSubresource = new VkImageSubresourceLayers { layerCount = 1, aspectMask = aspectFlags }
            };

            vkSource.TransitionImageLayout(_cb, 0, 1, 0, 1, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL);
            vkDestination.TransitionImageLayout(_cb, 0, 1, 0, 1, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL);

            vkCmdResolveImage(
                _cb,
                vkSource.OptimalDeviceImage,
                 VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,
                vkDestination.OptimalDeviceImage,
                VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,
                1,
                &region);

            if ((vkDestination.Usage & TextureUsage.Sampled) != 0)
            {
                vkDestination.TransitionImageLayout(_cb, 0, 1, 0, 1, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL);
            }
        }

        public override void End()
        {
            if (!_commandBufferBegun)
            {
                throw new GraphicsException("CommandBuffer must have been started before End() may be called.");
            }

            _commandBufferBegun = false;
            _commandBufferEnded = true;

            if (!_currentFramebufferEverActive && _currentFramebuffer != null)
            {
                BeginCurrentRenderPass();
            }
            if (_activeRenderPass != default)
            {
                EndCurrentRenderPass();
                _currentFramebuffer!.TransitionToFinalLayout(_cb);
            }

            vkEndCommandBuffer(_cb);

            lock (_submittedCommandBuffers)
            {
                _submittedCommandBuffers.Add(_cb);
            }
        }

        protected override void SetFramebufferCore(Framebuffer fb)
        {
            EnsureBegin();

            if (_activeRenderPass != default)
            {
                EndCurrentRenderPass();
            }
            else if (!_currentFramebufferEverActive && _currentFramebuffer != null)
            {
                // This forces any queued up texture clears to be emitted.
                BeginCurrentRenderPass();
                EndCurrentRenderPass();
            }

            if (_currentFramebuffer != null)
            {
                _currentFramebuffer.TransitionToFinalLayout(_cb);
            }

            VkFramebufferBase vkFB = Util.AssertSubtype<Framebuffer, VkFramebufferBase>(fb);
            // We need to make sure that a Framebuffer is in the correct layout before we start rendering to it.
            vkFB.TransitionToIntermediateLayout(_cb);

            _currentFramebuffer = vkFB;
            _currentFramebufferEverActive = false;
            _newFramebuffer = true;
            Util.EnsureArrayMinimumSize(ref _scissorRects, Math.Max(1, (uint)vkFB.ColorTargets.Length));
            uint clearValueCount = (uint)vkFB.ColorTargets.Length;
            Util.EnsureArrayMinimumSize(ref _clearValues, clearValueCount + 1); // Leave an extra space for the depth value (tracked separately).
            Util.ClearArray(_validColorClearValues);
            Util.EnsureArrayMinimumSize(ref _validColorClearValues, clearValueCount);
            _currentStagingInfo!.AddResource(vkFB);

            if (fb is VkSwapchainFramebuffer scFB)
            {
                _currentStagingInfo.AddResource(scFB.Swapchain);
            }
        }

        private void EnsureRenderPassActive()
        {
            if (_activeRenderPass == default)
            {
                BeginCurrentRenderPass();
            }
        }

        private void EnsureNoRenderPass()
        {
            if (_activeRenderPass != default)
            {
                EndCurrentRenderPass();
            }
        }

        private void BeginCurrentRenderPass()
        {
            Debug.Assert(_activeRenderPass == default);
            Debug.Assert(_currentFramebuffer != null);
            _currentFramebufferEverActive = true;

            uint attachmentCount = _currentFramebuffer.AttachmentCount;
            bool haveAnyAttachments = _currentFramebuffer.ColorTargets.Length > 0 || _currentFramebuffer.DepthTarget != null;
            bool haveAllClearValues = _depthClearValue.HasValue || _currentFramebuffer.DepthTarget == null;
            bool haveAnyClearValues = _depthClearValue.HasValue;
            for (int i = 0; i < _currentFramebuffer.ColorTargets.Length; i++)
            {
                if (!_validColorClearValues[i])
                {
                    haveAllClearValues = false;
                }
                else
                {
                    haveAnyClearValues = true;
                }
            }

            var renderPassBI = new VkRenderPassBeginInfo
            {
                renderArea = new VkRect2D(0, 0, _currentFramebuffer.RenderableWidth, _currentFramebuffer.RenderableHeight),
                framebuffer = _currentFramebuffer.CurrentFramebuffer
            };

            if (!haveAnyAttachments || !haveAllClearValues)
            {
                renderPassBI.renderPass = _newFramebuffer
                    ? _currentFramebuffer.RenderPassNoClear_Init
                    : _currentFramebuffer.RenderPassNoClear_Load;
                vkCmdBeginRenderPass(_cb, renderPassBI, VK_SUBPASS_CONTENTS_INLINE);
                _activeRenderPass = renderPassBI.renderPass;

                if (haveAnyClearValues)
                {
                    if (_depthClearValue.HasValue)
                    {
                        ClearDepthStencilCore(_depthClearValue.Value.depthStencil.depth, (byte)_depthClearValue.Value.depthStencil.stencil);
                        _depthClearValue = null;
                    }

                    for (uint i = 0; i < _currentFramebuffer.ColorTargets.Length; i++)
                    {
                        if (_validColorClearValues[i])
                        {
                            _validColorClearValues[i] = false;
                            VkClearValue vkClearValue = _clearValues[i];
                            var clearColor = new RgbaFloat(
                                vkClearValue.color.float32[0],
                                vkClearValue.color.float32[1],
                                vkClearValue.color.float32[2],
                                vkClearValue.color.float32[3]);
                            ClearColorTarget(i, clearColor);
                        }
                    }
                }
            }
            else
            {
                // We have clear values for every attachment.
                renderPassBI.renderPass = _currentFramebuffer.RenderPassClear;
                fixed (VkClearValue* clearValuesPtr = &_clearValues[0])
                {
                    renderPassBI.clearValueCount = attachmentCount;
                    renderPassBI.pClearValues = clearValuesPtr;
                    if (_depthClearValue.HasValue)
                    {
                        _clearValues[_currentFramebuffer.ColorTargets.Length] = _depthClearValue.Value;
                        _depthClearValue = null;
                    }
                    vkCmdBeginRenderPass(_cb, renderPassBI, VK_SUBPASS_CONTENTS_INLINE);
                    _activeRenderPass = _currentFramebuffer.RenderPassClear;
                    Util.ClearArray(_validColorClearValues);
                }
            }

            _newFramebuffer = false;
        }

        private void EndCurrentRenderPass()
        {
            Debug.Assert(_activeRenderPass != default);
            vkCmdEndRenderPass(_cb);
            _activeRenderPass = default;

            // Place a barrier between RenderPasses, so that color / depth outputs
            // can be read in subsequent passes.
            vkCmdPipelineBarrier(
                _cb,
                VK_PIPELINE_STAGE_BOTTOM_OF_PIPE_BIT,
                VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT,
                (VkDependencyFlagBits)0,
                0,
                null,
                0,
                null,
                0,
                null);
        }

        private protected override void SetVertexBufferCore(uint index, DeviceBuffer buffer, uint offset)
        {
            EnsureBegin();

            VkBuffer vkBuffer = Util.AssertSubtype<DeviceBuffer, VkBuffer>(buffer);
            XenoAtom.Interop.vulkan.VkBuffer deviceBuffer = vkBuffer.DeviceBuffer;
            VkDeviceSize offset64 = offset;
            vkCmdBindVertexBuffers(_cb, index, 1, &deviceBuffer, &offset64);
            _currentStagingInfo!.AddResource(vkBuffer);
        }

        private protected override void SetIndexBufferCore(DeviceBuffer buffer, IndexFormat format, uint offset)
        {
            EnsureBegin();

            VkBuffer vkBuffer = Util.AssertSubtype<DeviceBuffer, VkBuffer>(buffer);
            vkCmdBindIndexBuffer(_cb, vkBuffer.DeviceBuffer, offset, VkFormats.VdToVkIndexFormat(format));
            _currentStagingInfo!.AddResource(vkBuffer);
        }

        private protected override void SetPipelineCore(Pipeline pipeline)
        {
            EnsureBegin();

            VkPipeline vkPipeline = Util.AssertSubtype<Pipeline, VkPipeline>(pipeline);
            if (!pipeline.IsComputePipeline && _currentGraphicsPipeline != pipeline)
            {
                Util.EnsureArrayMinimumSize(ref _currentGraphicsResourceSets, vkPipeline.ResourceSetCount);
                ClearSets(_currentGraphicsResourceSets);
                Util.EnsureArrayMinimumSize(ref _graphicsResourceSetsChanged, vkPipeline.ResourceSetCount);
                vkCmdBindPipeline(_cb, VK_PIPELINE_BIND_POINT_GRAPHICS, vkPipeline.DevicePipeline);
                _currentGraphicsPipeline = vkPipeline;
            }
            else if (pipeline.IsComputePipeline && _currentComputePipeline != pipeline)
            {
                Util.EnsureArrayMinimumSize(ref _currentComputeResourceSets, vkPipeline.ResourceSetCount);
                ClearSets(_currentComputeResourceSets);
                Util.EnsureArrayMinimumSize(ref _computeResourceSetsChanged, vkPipeline.ResourceSetCount);
                vkCmdBindPipeline(_cb, VK_PIPELINE_BIND_POINT_COMPUTE, vkPipeline.DevicePipeline);
                _currentComputePipeline = vkPipeline;
            }

            _currentStagingInfo!.AddResource(vkPipeline);
        }

        private void ClearSets(BoundResourceSetInfo[] boundSets)
        {
            foreach (BoundResourceSetInfo boundSetInfo in boundSets)
            {
                boundSetInfo.Offsets.Dispose();
            }
            Util.ClearArray(boundSets);
        }

        protected override void SetGraphicsResourceSetCore(uint slot, ResourceSet rs, uint dynamicOffsetsCount, ref uint dynamicOffsets)
        {
            EnsureGraphicsPipeline();

            if (!_currentGraphicsResourceSets[slot].Equals(rs, dynamicOffsetsCount, ref dynamicOffsets))
            {
                _currentGraphicsResourceSets[slot].Offsets.Dispose();
                _currentGraphicsResourceSets[slot] = new BoundResourceSetInfo(rs, dynamicOffsetsCount, ref dynamicOffsets);
                _graphicsResourceSetsChanged[slot] = true;
                Util.AssertSubtype<ResourceSet, VkResourceSet>(rs);
            }
        }

        protected override void SetComputeResourceSetCore(uint slot, ResourceSet rs, uint dynamicOffsetsCount, ref uint dynamicOffsets)
        {
            EnsureComputePipeline();

            if (!_currentComputeResourceSets[slot].Equals(rs, dynamicOffsetsCount, ref dynamicOffsets))
            {
                _currentComputeResourceSets[slot].Offsets.Dispose();
                _currentComputeResourceSets[slot] = new BoundResourceSetInfo(rs, dynamicOffsetsCount, ref dynamicOffsets);
                _computeResourceSetsChanged[slot] = true;
                Util.AssertSubtype<ResourceSet, VkResourceSet>(rs);
            }
        }

        public override void SetScissorRect(uint index, uint x, uint y, uint width, uint height)
        {
            EnsureBegin();
            
            if (index == 0 || Device.Features.MultipleViewports)
            {
                VkRect2D scissor = new VkRect2D((int)x, (int)y, width, height);
                if (_scissorRects[index] != scissor)
                {
                    _scissorRects[index] = scissor;
                    vkCmdSetScissor(_cb, index, 1, &scissor);
                }
            }
        }

        public override void SetViewport(uint index, ref Viewport viewport)
        {
            EnsureBegin();
            
            if (index == 0 || Device.Features.MultipleViewports)
            {
                float vpY = Device.IsClipSpaceYInverted
                    ? viewport.Y
                    : viewport.Height + viewport.Y;
                float vpHeight = Device.IsClipSpaceYInverted
                    ? viewport.Height
                    : -viewport.Height;

                VkViewport vkViewport = new VkViewport
                {
                    x = viewport.X,
                    y = vpY,
                    width = viewport.Width,
                    height = vpHeight,
                    minDepth = viewport.MinDepth,
                    maxDepth = viewport.MaxDepth
                };

                vkCmdSetViewport(_cb, index, 1, &vkViewport);
            }
        }

        private protected override void UpdateBufferCore(DeviceBuffer buffer, uint bufferOffsetInBytes, IntPtr source, uint sizeInBytes)
        {
            EnsureBegin();

            VkBuffer stagingBuffer = GetStagingBuffer(sizeInBytes);
            Device.UpdateBuffer(stagingBuffer, 0, source, sizeInBytes);
            CopyBuffer(stagingBuffer, 0, buffer, bufferOffsetInBytes, sizeInBytes);
        }

        protected override void CopyBufferCore(
            DeviceBuffer source,
            uint sourceOffset,
            DeviceBuffer destination,
            uint destinationOffset,
            uint sizeInBytes)
        {
            EnsureBegin();
            EnsureNoRenderPass();

            VkBuffer srcVkBuffer = Util.AssertSubtype<DeviceBuffer, VkBuffer>(source);
            _currentStagingInfo!.AddResource(srcVkBuffer);
            VkBuffer dstVkBuffer = Util.AssertSubtype<DeviceBuffer, VkBuffer>(destination);
            _currentStagingInfo.AddResource(dstVkBuffer);

            VkBufferCopy region = new VkBufferCopy
            {
                srcOffset = sourceOffset,
                dstOffset = destinationOffset,
                size = sizeInBytes
            };

            vkCmdCopyBuffer(_cb, srcVkBuffer.DeviceBuffer, dstVkBuffer.DeviceBuffer, 1, &region);

            bool needToProtectUniform = destination.Usage.HasFlag(BufferUsage.UniformBuffer);

            VkMemoryBarrier barrier = new();
            barrier.srcAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
            barrier.dstAccessMask = needToProtectUniform ? VK_ACCESS_UNIFORM_READ_BIT : VK_ACCESS_VERTEX_ATTRIBUTE_READ_BIT;
            barrier.pNext = null;
            vkCmdPipelineBarrier(
                _cb,
                VK_PIPELINE_STAGE_TRANSFER_BIT, needToProtectUniform ?
                    VK_PIPELINE_STAGE_VERTEX_SHADER_BIT | VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT |
                    VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT | VK_PIPELINE_STAGE_GEOMETRY_SHADER_BIT |
                    VK_PIPELINE_STAGE_TESSELLATION_CONTROL_SHADER_BIT | VkPipelineStageFlagBits.VK_PIPELINE_STAGE_TESSELLATION_EVALUATION_SHADER_BIT
                    : VK_PIPELINE_STAGE_VERTEX_INPUT_BIT,
                (VkDependencyFlagBits)0,
                1, &barrier,
                0, null,
                0, null);
        }

        protected override void CopyTextureCore(
            Texture source,
            uint srcX, uint srcY, uint srcZ,
            uint srcMipLevel,
            uint srcBaseArrayLayer,
            Texture destination,
            uint dstX, uint dstY, uint dstZ,
            uint dstMipLevel,
            uint dstBaseArrayLayer,
            uint width, uint height, uint depth,
            uint layerCount)
        {
            EnsureBegin();
            EnsureNoRenderPass();
            CopyTextureCore_VkCommandBuffer(
                _cb,
                source, srcX, srcY, srcZ, srcMipLevel, srcBaseArrayLayer,
                destination, dstX, dstY, dstZ, dstMipLevel, dstBaseArrayLayer,
                width, height, depth, layerCount);

            VkTexture srcVkTexture = Util.AssertSubtype<Texture, VkTexture>(source);
            _currentStagingInfo!.AddResource(srcVkTexture);
            VkTexture dstVkTexture = Util.AssertSubtype<Texture, VkTexture>(destination);
            _currentStagingInfo.AddResource(dstVkTexture);
        }

        internal static void CopyTextureCore_VkCommandBuffer(
            VkCommandBuffer cb,
            Texture source,
            uint srcX, uint srcY, uint srcZ,
            uint srcMipLevel,
            uint srcBaseArrayLayer,
            Texture destination,
            uint dstX, uint dstY, uint dstZ,
            uint dstMipLevel,
            uint dstBaseArrayLayer,
            uint width, uint height, uint depth,
            uint layerCount)
        {
            VkTexture srcVkTexture = Util.AssertSubtype<Texture, VkTexture>(source);
            VkTexture dstVkTexture = Util.AssertSubtype<Texture, VkTexture>(destination);

            bool sourceIsStaging = (source.Usage & TextureUsage.Staging) == TextureUsage.Staging;
            bool destIsStaging = (destination.Usage & TextureUsage.Staging) == TextureUsage.Staging;

            if (!sourceIsStaging && !destIsStaging)
            {
                var srcAspect = (srcVkTexture.Usage & TextureUsage.DepthStencil) == TextureUsage.DepthStencil
                    ? (FormatHelpers.IsStencilFormat(srcVkTexture.Format)
                        ? VK_IMAGE_ASPECT_DEPTH_BIT | VK_IMAGE_ASPECT_STENCIL_BIT
                        : VK_IMAGE_ASPECT_DEPTH_BIT)
                    : VK_IMAGE_ASPECT_COLOR_BIT;

                var dstAspect = (dstVkTexture.Usage & TextureUsage.DepthStencil) == TextureUsage.DepthStencil
                    ? (FormatHelpers.IsStencilFormat(dstVkTexture.Format)
                        ? VK_IMAGE_ASPECT_DEPTH_BIT | VK_IMAGE_ASPECT_STENCIL_BIT
                        : VK_IMAGE_ASPECT_DEPTH_BIT)
                    : VK_IMAGE_ASPECT_COLOR_BIT;

                if (srcAspect != dstAspect)
                {
                    throw new InvalidOperationException($"Source texture with aspect `{srcAspect}` and destination texture with aspect `{dstAspect}` must have the same aspect.");
                }

                VkImageSubresourceLayers srcSubresource = new VkImageSubresourceLayers
                {
                    aspectMask = srcAspect,
                    layerCount = layerCount,
                    mipLevel = srcMipLevel,
                    baseArrayLayer = srcBaseArrayLayer
                };


                VkImageSubresourceLayers dstSubresource = new VkImageSubresourceLayers
                {
                    aspectMask = dstAspect,
                    layerCount = layerCount,
                    mipLevel = dstMipLevel,
                    baseArrayLayer = dstBaseArrayLayer
                };

                VkImageCopy region = new VkImageCopy
                {
                    srcOffset = new VkOffset3D { x = (int)srcX, y = (int)srcY, z = (int)srcZ },
                    dstOffset = new VkOffset3D { x = (int)dstX, y = (int)dstY, z = (int)dstZ },
                    srcSubresource = srcSubresource,
                    dstSubresource = dstSubresource,
                    extent = new VkExtent3D { width = width, height = height, depth = depth }
                };

                srcVkTexture.TransitionImageLayout(
                    cb,
                    srcMipLevel,
                    1,
                    srcBaseArrayLayer,
                    layerCount,
                    VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL);

                dstVkTexture.TransitionImageLayout(
                    cb,
                    dstMipLevel,
                    1,
                    dstBaseArrayLayer,
                    layerCount,
                    VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL);

                vkCmdCopyImage(
                    cb,
                    srcVkTexture.OptimalDeviceImage,
                    VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,
                    dstVkTexture.OptimalDeviceImage,
                    VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,
                    1,
                    &region);

                if ((srcVkTexture.Usage & TextureUsage.Sampled) != 0)
                {
                    srcVkTexture.TransitionImageLayout(
                        cb,
                        srcMipLevel,
                        1,
                        srcBaseArrayLayer,
                        layerCount,
                        VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL);
                }

                if ((dstVkTexture.Usage & TextureUsage.Sampled) != 0)
                {
                    dstVkTexture.TransitionImageLayout(
                        cb,
                        dstMipLevel,
                        1,
                        dstBaseArrayLayer,
                        layerCount,
                        VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL);
                }
            }
            else if (sourceIsStaging && !destIsStaging)
            {
                XenoAtom.Interop.vulkan.VkBuffer srcBuffer = srcVkTexture.StagingBuffer;
                VkSubresourceLayout srcLayout = srcVkTexture.GetSubresourceLayout(
                    srcVkTexture.CalculateSubresource(srcMipLevel, srcBaseArrayLayer));
                VkImage dstImage = dstVkTexture.OptimalDeviceImage;
                dstVkTexture.TransitionImageLayout(
                    cb,
                    dstMipLevel,
                    1,
                    dstBaseArrayLayer,
                    layerCount,
                    VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL);

                VkImageSubresourceLayers dstSubresource = new VkImageSubresourceLayers
                {
                    aspectMask = VK_IMAGE_ASPECT_COLOR_BIT,
                    layerCount = layerCount,
                    mipLevel = dstMipLevel,
                    baseArrayLayer = dstBaseArrayLayer
                };

                Util.GetMipDimensions(srcVkTexture, srcMipLevel, out uint mipWidth, out uint mipHeight, out _);
                uint blockSize = FormatHelpers.IsCompressedFormat(srcVkTexture.Format) ? 4u : 1u;
                uint bufferRowLength = Math.Max(mipWidth, blockSize);
                uint bufferImageHeight = Math.Max(mipHeight, blockSize);
                uint compressedX = srcX / blockSize;
                uint compressedY = srcY / blockSize;
                uint blockSizeInBytes = blockSize == 1
                    ? FormatSizeHelpers.GetSizeInBytes(srcVkTexture.Format)
                    : FormatHelpers.GetBlockSizeInBytes(srcVkTexture.Format);
                uint rowPitch = FormatHelpers.GetRowPitch(bufferRowLength, srcVkTexture.Format);
                uint depthPitch = FormatHelpers.GetDepthPitch(rowPitch, bufferImageHeight, srcVkTexture.Format);

                uint copyWidth = Math.Min(width, mipWidth);
                uint copyheight = Math.Min(height, mipHeight);

                VkBufferImageCopy regions = new VkBufferImageCopy
                {
                    bufferOffset = srcLayout.offset
                        + (srcZ * depthPitch)
                        + (compressedY * rowPitch)
                        + (compressedX * blockSizeInBytes),
                    bufferRowLength = bufferRowLength,
                    bufferImageHeight = bufferImageHeight,
                    imageExtent = new VkExtent3D { width = copyWidth, height = copyheight, depth = depth },
                    imageOffset = new VkOffset3D { x = (int)dstX, y = (int)dstY, z = (int)dstZ },
                    imageSubresource = dstSubresource
                };

                vkCmdCopyBufferToImage(cb, srcBuffer, dstImage, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, 1, &regions);

                if ((dstVkTexture.Usage & TextureUsage.Sampled) != 0)
                {
                    dstVkTexture.TransitionImageLayout(
                        cb,
                        dstMipLevel,
                        1,
                        dstBaseArrayLayer,
                        layerCount,
                        VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL);
                }
            }
            else if (!sourceIsStaging && destIsStaging)
            {
                VkImage srcImage = srcVkTexture.OptimalDeviceImage;
                srcVkTexture.TransitionImageLayout(
                    cb,
                    srcMipLevel,
                    1,
                    srcBaseArrayLayer,
                    layerCount,
                    VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL);

                XenoAtom.Interop.vulkan.VkBuffer dstBuffer = dstVkTexture.StagingBuffer;

                VkImageAspectFlags aspect = (srcVkTexture.Usage & TextureUsage.DepthStencil) != 0
                    ? VK_IMAGE_ASPECT_DEPTH_BIT
                    : VK_IMAGE_ASPECT_COLOR_BIT;

                Util.GetMipDimensions(dstVkTexture, dstMipLevel, out uint mipWidth, out uint mipHeight, out _);
                uint blockSize = FormatHelpers.IsCompressedFormat(srcVkTexture.Format) ? 4u : 1u;
                uint bufferRowLength = Math.Max(mipWidth, blockSize);
                uint bufferImageHeight = Math.Max(mipHeight, blockSize);
                uint compressedDstX = dstX / blockSize;
                uint compressedDstY = dstY / blockSize;
                uint blockSizeInBytes = blockSize == 1
                    ? FormatSizeHelpers.GetSizeInBytes(dstVkTexture.Format)
                    : FormatHelpers.GetBlockSizeInBytes(dstVkTexture.Format);
                uint rowPitch = FormatHelpers.GetRowPitch(bufferRowLength, dstVkTexture.Format);
                uint depthPitch = FormatHelpers.GetDepthPitch(rowPitch, bufferImageHeight, dstVkTexture.Format);

                var layers = stackalloc VkBufferImageCopy[(int)layerCount];
                for(uint layer = 0; layer < layerCount; layer++)
                {
                    VkSubresourceLayout dstLayout = dstVkTexture.GetSubresourceLayout(
                        dstVkTexture.CalculateSubresource(dstMipLevel, dstBaseArrayLayer + layer));

                    VkImageSubresourceLayers srcSubresource = new VkImageSubresourceLayers
                    {
                        aspectMask = aspect,
                        layerCount = 1,
                        mipLevel = srcMipLevel,
                        baseArrayLayer = srcBaseArrayLayer + layer
                    };

                    VkBufferImageCopy region = new VkBufferImageCopy
                    {
                        bufferRowLength = bufferRowLength,
                        bufferImageHeight = bufferImageHeight,
                        bufferOffset = dstLayout.offset
                            + (dstZ * depthPitch)
                            + (compressedDstY * rowPitch)
                            + (compressedDstX * blockSizeInBytes),
                        imageExtent = new VkExtent3D { width = width, height = height, depth = depth },
                        imageOffset = new VkOffset3D { x = (int)srcX, y = (int)srcY, z = (int)srcZ },
                        imageSubresource = srcSubresource
                    };

                    layers[layer] = region;
                }

                vkCmdCopyImageToBuffer(cb, srcImage, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, dstBuffer, layerCount, layers);

                if ((srcVkTexture.Usage & TextureUsage.Sampled) != 0)
                {
                    srcVkTexture.TransitionImageLayout(
                        cb,
                        srcMipLevel,
                        1,
                        srcBaseArrayLayer,
                        layerCount,
                        VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL);
                }
            }
            else
            {
                Debug.Assert(sourceIsStaging && destIsStaging);
                XenoAtom.Interop.vulkan.VkBuffer srcBuffer = srcVkTexture.StagingBuffer;
                VkSubresourceLayout srcLayout = srcVkTexture.GetSubresourceLayout(
                    srcVkTexture.CalculateSubresource(srcMipLevel, srcBaseArrayLayer));
                XenoAtom.Interop.vulkan.VkBuffer dstBuffer = dstVkTexture.StagingBuffer;
                VkSubresourceLayout dstLayout = dstVkTexture.GetSubresourceLayout(
                    dstVkTexture.CalculateSubresource(dstMipLevel, dstBaseArrayLayer));

                uint zLimit = Math.Max(depth, layerCount);
                if (!FormatHelpers.IsCompressedFormat(source.Format))
                {
                    uint pixelSize = FormatSizeHelpers.GetSizeInBytes(srcVkTexture.Format);
                    for (uint zz = 0; zz < zLimit; zz++)
                    {
                        for (uint yy = 0; yy < height; yy++)
                        {
                            VkBufferCopy region = new VkBufferCopy
                            {
                                srcOffset = srcLayout.offset
                                    + srcLayout.depthPitch * (zz + srcZ)
                                    + srcLayout.rowPitch * (yy + srcY)
                                    + pixelSize * srcX,
                                dstOffset = dstLayout.offset
                                    + dstLayout.depthPitch * (zz + dstZ)
                                    + dstLayout.rowPitch * (yy + dstY)
                                    + pixelSize * dstX,
                                size = width * pixelSize,
                            };

                            vkCmdCopyBuffer(cb, srcBuffer, dstBuffer, 1, &region);
                        }
                    }
                }
                else // IsCompressedFormat
                {
                    uint denseRowSize = FormatHelpers.GetRowPitch(width, source.Format);
                    uint numRows = FormatHelpers.GetNumRows(height, source.Format);
                    uint compressedSrcX = srcX / 4;
                    uint compressedSrcY = srcY / 4;
                    uint compressedDstX = dstX / 4;
                    uint compressedDstY = dstY / 4;
                    uint blockSizeInBytes = FormatHelpers.GetBlockSizeInBytes(source.Format);

                    for (uint zz = 0; zz < zLimit; zz++)
                    {
                        for (uint row = 0; row < numRows; row++)
                        {
                            VkBufferCopy region = new VkBufferCopy
                            {
                                srcOffset = srcLayout.offset
                                    + srcLayout.depthPitch * (zz + srcZ)
                                    + srcLayout.rowPitch * (row + compressedSrcY)
                                    + blockSizeInBytes * compressedSrcX,
                                dstOffset = dstLayout.offset
                                    + dstLayout.depthPitch * (zz + dstZ)
                                    + dstLayout.rowPitch * (row + compressedDstY)
                                    + blockSizeInBytes * compressedDstX,
                                size = denseRowSize,
                            };

                            vkCmdCopyBuffer(cb, srcBuffer, dstBuffer, 1, &region);
                        }
                    }

                }
            }
        }

        private protected override void GenerateMipmapsCore(Texture texture)
        {
            EnsureBegin();
            EnsureNoRenderPass();
            VkTexture vkTex = Util.AssertSubtype<Texture, VkTexture>(texture);
            _currentStagingInfo!.AddResource(vkTex);

            uint layerCount = vkTex.ArrayLayers;
            if ((vkTex.Usage & TextureUsage.Cubemap) != 0)
            {
                layerCount *= 6;
            }

            VkImageBlit region;

            uint width = vkTex.Width;
            uint height = vkTex.Height;
            uint depth = vkTex.Depth;
            for (uint level = 1; level < vkTex.MipLevels; level++)
            {
                vkTex.TransitionImageLayoutNonmatching(_cb, level - 1, 1, 0, layerCount, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL);
                vkTex.TransitionImageLayoutNonmatching(_cb, level, 1, 0, layerCount, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL);

                VkImage deviceImage = vkTex.OptimalDeviceImage;
                uint mipWidth = Math.Max(width >> 1, 1);
                uint mipHeight = Math.Max(height >> 1, 1);
                uint mipDepth = Math.Max(depth >> 1, 1);

                region.srcSubresource = new VkImageSubresourceLayers
                {
                    aspectMask = VK_IMAGE_ASPECT_COLOR_BIT,
                    baseArrayLayer = 0,
                    layerCount = layerCount,
                    mipLevel = level - 1
                };
                region.srcOffsets[0] = new VkOffset3D();
                region.srcOffsets[1] = new VkOffset3D((int)width, (int)height, (int)depth);
                region.dstOffsets[0] = new VkOffset3D();

                region.dstSubresource = new VkImageSubresourceLayers
                {
                    aspectMask = VK_IMAGE_ASPECT_COLOR_BIT,
                    baseArrayLayer = 0,
                    layerCount = layerCount,
                    mipLevel = level
                };

                region.dstOffsets[1] = new VkOffset3D { x = (int)mipWidth, y = (int)mipHeight, z = (int)mipDepth };
                vkCmdBlitImage(
                    _cb,
                    deviceImage, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,
                    deviceImage, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,
                    1, &region,
                    Device.GetFormatFilter(vkTex.VkFormat));

                width = mipWidth;
                height = mipHeight;
                depth = mipDepth;
            }

            if ((vkTex.Usage & TextureUsage.Sampled) != 0)
            {
                vkTex.TransitionImageLayoutNonmatching(_cb, 0, vkTex.MipLevels, 0, layerCount, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL);
            }
        }

        [Conditional("DEBUG")]
        private void DebugFullPipelineBarrier()
        {
            VkMemoryBarrier memoryBarrier = new VkMemoryBarrier();
            memoryBarrier.srcAccessMask = VK_ACCESS_INDIRECT_COMMAND_READ_BIT |
                   VK_ACCESS_INDEX_READ_BIT |
                   VK_ACCESS_VERTEX_ATTRIBUTE_READ_BIT |
                   VK_ACCESS_UNIFORM_READ_BIT |
                   VK_ACCESS_INPUT_ATTACHMENT_READ_BIT |
                   VK_ACCESS_SHADER_READ_BIT |
                   VK_ACCESS_SHADER_WRITE_BIT |
                   VK_ACCESS_COLOR_ATTACHMENT_READ_BIT |
                   VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT |
                   VK_ACCESS_DEPTH_STENCIL_ATTACHMENT_READ_BIT |
                   VK_ACCESS_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT |
                   VK_ACCESS_TRANSFER_READ_BIT |
                   VK_ACCESS_TRANSFER_WRITE_BIT |
                   VK_ACCESS_HOST_READ_BIT |
                   VK_ACCESS_HOST_WRITE_BIT;
            memoryBarrier.dstAccessMask = VK_ACCESS_INDIRECT_COMMAND_READ_BIT |
                   VK_ACCESS_INDEX_READ_BIT |
                   VK_ACCESS_VERTEX_ATTRIBUTE_READ_BIT |
                   VK_ACCESS_UNIFORM_READ_BIT |
                   VK_ACCESS_INPUT_ATTACHMENT_READ_BIT |
                   VK_ACCESS_SHADER_READ_BIT |
                   VK_ACCESS_SHADER_WRITE_BIT |
                   VK_ACCESS_COLOR_ATTACHMENT_READ_BIT |
                   VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT |
                   VK_ACCESS_DEPTH_STENCIL_ATTACHMENT_READ_BIT |
                   VK_ACCESS_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT |
                   VK_ACCESS_TRANSFER_READ_BIT |
                   VK_ACCESS_TRANSFER_WRITE_BIT |
                   VK_ACCESS_HOST_READ_BIT |
                   VK_ACCESS_HOST_WRITE_BIT;

            vkCmdPipelineBarrier(
                _cb,
                VK_PIPELINE_STAGE_ALL_COMMANDS_BIT, // srcStageMask
                VK_PIPELINE_STAGE_ALL_COMMANDS_BIT, // dstStageMask
                (VkDependencyFlagBits)0,
                1,                                  // memoryBarrierCount
                &memoryBarrier,                     // pMemoryBarriers
                0, null,
                0, null);
        }

        private VkBuffer GetStagingBuffer(uint size)
        {
            EnsureBegin();

            lock (_stagingLock)
            {
                VkBuffer? ret = null;
                foreach (VkBuffer buffer in _availableStagingBuffers)
                {
                    if (buffer.SizeInBytes >= size)
                    {
                        ret = buffer;
                        _availableStagingBuffers.Remove(buffer);
                        break;
                    }
                }
                if (ret == null)
                {
                    ret = (VkBuffer)Device.CreateBuffer(new BufferDescription(size, BufferUsage.Staging));
                    ret.Name = $"Staging Buffer (CommandList {Name})";
                }

                _currentStagingInfo!.BuffersUsed.Add(ret);
                return ret;
            }
        }

        public override void PushDebugGroup(ReadOnlySpanUtf8 name, in RgbaFloat color = default)
        {
            var func = vkCmdBeginDebugUtilsLabelExt;
            if (func == default) { return; }

            var label = new VkDebugUtilsLabelEXT
            {
                pLabelName = (byte*)name
            };
            *(RgbaFloat*)label.color = color;
            func.Invoke(_cb, &label);
        }

        public override void PopDebugGroup()
        {
            var func = vkCmdEndDebugUtilsLabelExt;
            if (func == default) { return; }

            func.Invoke(_cb);
        }

        public override void InsertDebugMarker(ReadOnlySpanUtf8 name, in RgbaFloat color = default)
        {
            var func = vkCmdInsertDebugUtilsLabelExt;
            if (func == default) { return; }

            var label = new VkDebugUtilsLabelEXT
            {
                pLabelName = (byte*)name
            };
            *(RgbaFloat*)label.color = color;
            func.Invoke(_cb, &label);
        }

        internal override void Destroy()
        {
            if (_currentStagingInfo is not null)
            {
                RecycleStagingInfo(_currentStagingInfo);
                _currentStagingInfo = null;
            }
            
            vkDestroyCommandPool(Device, _pool, null);

            Debug.Assert(_submittedStagingInfos.Count == 0);

            foreach (VkBuffer buffer in _availableStagingBuffers)
            {
                buffer.Dispose();
            }
        }

        private StagingResourceInfo GetStagingResourceInfo()
        {
            lock (_stagingLock)
            {
                StagingResourceInfo ret;
                int availableCount = _availableStagingInfos.Count;
                if (availableCount > 0)
                {
                    ret = _availableStagingInfos[availableCount - 1];
                    _availableStagingInfos.RemoveAt(availableCount - 1);
                }
                else
                {
                    ret = new StagingResourceInfo();
                }

                return ret;
            }
        }

        private void RecycleStagingInfo(StagingResourceInfo info)
        {
            lock (_stagingLock)
            {
                foreach (VkBuffer buffer in info.BuffersUsed)
                {
                    _availableStagingBuffers.Add(buffer);
                }

                foreach (ResourceRefCount rrc in info.Resources)
                {
                    rrc.Decrement();
                }

                info.Clear();

                _availableStagingInfos.Add(info);
            }
        }

        private void EnsureBegin()
        {
            if (_currentStagingInfo is null)
            {
                throw new InvalidOperationException($"Invalid call. The `{nameof(Begin)}` method should have been called before.");
            }
        }
        private void EnsureGraphicsPipeline()
        {
            if (_currentGraphicsPipeline is null)
            {
                throw new InvalidOperationException($"Invalid call. Current Graphics Pipeline is not set. The `{nameof(SetPipeline)}` method should have been called before.");
            }
        }

        private void EnsureComputePipeline()
        {
            if (_currentComputePipeline is null)
            {
                throw new InvalidOperationException($"Invalid call. Current Compute Pipeline is not set. The `{nameof(SetPipeline)}` method should have been called before.");
            }
        }
        
        private class StagingResourceInfo
        {
            public readonly List<VkBuffer> BuffersUsed = new ();

            public readonly HashSet<ResourceRefCount> Resources = new();

            public void AddResource(ResourceRefCount refCount)
            {
                if (Resources.Add(refCount))
                {
                    refCount.Increment();
                }
            }

            public void Clear()
            {
                BuffersUsed.Clear();
                Resources.Clear();
            }
        }
    }
}

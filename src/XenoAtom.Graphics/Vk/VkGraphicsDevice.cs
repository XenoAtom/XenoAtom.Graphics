using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using static XenoAtom.Interop.vulkan;
using XenoAtom.Interop;


namespace XenoAtom.Graphics.Vk
{
    internal sealed unsafe partial class VkGraphicsDevice : GraphicsDevice
    {
        private readonly VkPhysicalDevice _vkPhysicalDevice;
        private readonly VkInstance _vkInstance;
        private readonly bool _isDebugActivated;
        private readonly VkDeviceMemoryManager _memoryManager;
        private readonly VkDevice _vkDevice;
        private readonly uint _mainQueueFamilyIndex;
        private readonly VkCommandPool _graphicsCommandPool;
        private readonly object _graphicsCommandPoolLock = new object();
        private readonly VkQueue _vkGraphicsQueue;
        private readonly Action<GraphicsObject>? _onResourceCreated;

        public readonly object GraphicsQueueLock = new object();

        private readonly ConcurrentDictionary<VkFormat, VkFilter> _filters = new ConcurrentDictionary<VkFormat, VkFilter>();
        private readonly GraphicsDeviceBackendInfoVulkan _vulkanInfo;

        private const int SharedCommandPoolCount = 4;
        private readonly Stack<SharedCommandPool> _sharedGraphicsCommandPools = new Stack<SharedCommandPool>();
        private readonly VkDescriptorPoolManager _descriptorPoolManager;
        private readonly bool _standardClipYDirection;

        // Staging Resources
        private const uint MinStagingBufferSize = 64;
        private const uint MaxStagingBufferSize = 512;

        private readonly object _stagingResourcesLock = new object();
        private readonly List<VkTexture> _availableStagingTextures = new List<VkTexture>();
        private readonly List<VkBuffer> _availableStagingBuffers = new List<VkBuffer>();

        private readonly Dictionary<VkCommandBuffer, VkTexture> _submittedStagingTextures
            = new Dictionary<VkCommandBuffer, VkTexture>();
        private readonly Dictionary<VkCommandBuffer, VkBuffer> _submittedStagingBuffers
            = new Dictionary<VkCommandBuffer, VkBuffer>();
        private readonly Dictionary<VkCommandBuffer, SharedCommandPool> _submittedSharedCommandPools
            = new Dictionary<VkCommandBuffer, SharedCommandPool>();

        public ref readonly VkPhysicalDeviceMemoryProperties PhysicalDeviceMemProperties => ref Adapter.PhysicalDeviceMemProperties;

        public override GraphicsBackend BackendType => GraphicsBackend.Vulkan;

        public override bool IsUvOriginTopLeft => true;

        public override bool IsDepthRangeZeroToOne => true;

        public override bool IsClipSpaceYInverted => !_standardClipYDirection;

        /// <summary>
        /// Gets a simple point-filtered <see cref="Sampler"/> object owned by this instance.
        /// This object is created with <see cref="SamplerDescription.Point"/>.
        /// </summary>
        public override Sampler PointSampler { get; }

        /// <summary>
        /// Gets a simple linear-filtered <see cref="Sampler"/> object owned by this instance.
        /// This object is created with <see cref="SamplerDescription.Linear"/>.
        /// </summary>
        public override Sampler LinearSampler { get; }


        public override Sampler? AnisotropicSampler4x { get; }


        public override GraphicsDeviceFeatures Features { get; }
        public override GraphicsDeviceBackendInfo BackendInfo => _vulkanInfo;

        public VkInstance VkInstance => _vkInstance;

        public VkDevice VkDevice => _vkDevice;
        public VkPhysicalDevice VkPhysicalDevice => _vkPhysicalDevice;
        public VkQueue VkGraphicsQueue => _vkGraphicsQueue;
        public uint MainQueueFamilyIndex => _mainQueueFamilyIndex;
        public VkDeviceMemoryManager MemoryManager => _memoryManager;
        public VkDescriptorPoolManager DescriptorPoolManager => _descriptorPoolManager;
        //public PFN_vkCreateMetalSurfaceEXT CreateMetalSurfaceEXT => _createMetalSurfaceEXT;

        public readonly PFN_vkQueuePresentKHR _vkQueuePresentKHR;
        //private readonly PFN_vkCreateMetalSurfaceEXT _createMetalSurfaceEXT;

        public readonly PFN_vkAcquireNextImageKHR vkAcquireNextImageKHR;
        public readonly PFN_vkGetPhysicalDeviceSurfaceCapabilitiesKHR vkGetPhysicalDeviceSurfaceCapabilitiesKHR;
        public readonly PFN_vkGetPhysicalDeviceSurfaceFormatsKHR vkGetPhysicalDeviceSurfaceFormatsKHR;
        public readonly PFN_vkGetPhysicalDeviceSurfacePresentModesKHR vkGetPhysicalDeviceSurfacePresentModesKHR;
        public readonly PFN_vkCreateSwapchainKHR vkCreateSwapchainKHR;
        public readonly PFN_vkDestroySwapchainKHR vkDestroySwapchainKHR;
        public readonly PFN_vkDestroySurfaceKHR vkDestroySurfaceKHR;
        public readonly PFN_vkGetSwapchainImagesKHR vkGetSwapchainImagesKHR;

        private readonly object _submittedFencesLock = new object();
        private readonly ConcurrentQueue<XenoAtom.Interop.vulkan.VkFence> _availableSubmissionFences = new ConcurrentQueue<XenoAtom.Interop.vulkan.VkFence>();
        private readonly List<FenceSubmissionInfo> _submittedFences = new List<FenceSubmissionInfo>();

        private readonly PFN_vkSetDebugUtilsObjectNameEXT _vkSetDebugUtilsObjectNameEXT;

        public new VkGraphicsAdapter Adapter => Unsafe.As<GraphicsAdapter, VkGraphicsAdapter>(ref Unsafe.AsRef(in base.Adapter));

        public VkGraphicsManager Manager => Adapter.Manager;

        public VkGraphicsDevice(VkGraphicsAdapter adapter, GraphicsDeviceOptions options) : base(adapter)
        {
            // Just use the first one.
            _vkPhysicalDevice = Adapter.PhysicalDevice;
            _vkInstance = Manager.Instance;
            _isDebugActivated = Manager.IsDebugActivated;
            _vkSetDebugUtilsObjectNameEXT = Manager._vkSetDebugUtilsObjectNameEXT;
            _onResourceCreated = options.OnResourceCreated;

            // ---------------------------------------------------------------
            // Get QueueFamilyIndices
            // ---------------------------------------------------------------
            uint queueFamilyCount = 0;
            vkGetPhysicalDeviceQueueFamilyProperties(_vkPhysicalDevice, out queueFamilyCount);
            VkQueueFamilyProperties[] qfp = new VkQueueFamilyProperties[queueFamilyCount];
            vkGetPhysicalDeviceQueueFamilyProperties(_vkPhysicalDevice, qfp);

            bool foundMainQueue = false;
            const VkQueueFlags requiredQueue = VK_QUEUE_GRAPHICS_BIT | VK_QUEUE_COMPUTE_BIT;
            for (uint i = 0; i < qfp.Length; i++)
            {
                ref var queueFamily = ref qfp[i];
                var queueFlags = (VkQueueFlags)queueFamily.queueFlags;

                if ((queueFlags & requiredQueue) == requiredQueue)
                {
                    _mainQueueFamilyIndex = i;
                    foundMainQueue = true;
                    break;
                }
            }

            if (!foundMainQueue)
            {
                throw new GraphicsException("No suitable queue family found.");
            }

            // ---------------------------------------------------------------
            // Create LogicalDevice
            // ---------------------------------------------------------------

            VkDeviceQueueCreateInfo queueCreateInfo = new VkDeviceQueueCreateInfo();
            queueCreateInfo.queueFamilyIndex = _mainQueueFamilyIndex;
            queueCreateInfo.queueCount = 1;
            float priority = 1f;
            queueCreateInfo.pQueuePriorities = &priority;
            
            VkPhysicalDeviceFeatures deviceFeatures = Adapter.PhysicalDeviceFeatures;

            VkExtensionProperties[] props = GetDeviceExtensionProperties();

            var requiredDeviceExtensions = new HashSet<ReadOnlyMemoryUtf8>(options.VulkanDeviceOptions.DeviceExtensions);

            // Enforce the presence of these extensions
            requiredDeviceExtensions.Add(VK_KHR_DRIVER_PROPERTIES_EXTENSION_NAME);
            requiredDeviceExtensions.Add(VK_KHR_SWAPCHAIN_EXTENSION_NAME);

            var activeExtensions = new nint[props.Length];
            uint activeExtensionCount = 0;

            bool is_VK_EXT_SUBGROUP_SIZE_CONTROL_EXTENSION_NAME_available = false;

            fixed (VkExtensionProperties* properties = props)
            {
                for (int property = 0; property < props.Length; property++)
                {
                    var extensionName = new ReadOnlyMemoryUtf8(properties[property].extensionName);
                    if (extensionName == VK_KHR_SWAPCHAIN_EXTENSION_NAME)
                    {
                        activeExtensions[activeExtensionCount++] = (IntPtr)properties[property].extensionName;
                        requiredDeviceExtensions.Remove(extensionName);
                    }
                    else if (options.PreferStandardClipSpaceYDirection && extensionName == VK_KHR_MAINTENANCE1_EXTENSION_NAME)
                    {
                        activeExtensions[activeExtensionCount++] = (IntPtr)properties[property].extensionName;
                        requiredDeviceExtensions.Remove(extensionName);
                        _standardClipYDirection = true;
                    }
                    else if (extensionName == VK_KHR_DRIVER_PROPERTIES_EXTENSION_NAME)
                    {
                        activeExtensions[activeExtensionCount++] = (IntPtr)properties[property].extensionName;
                        requiredDeviceExtensions.Remove(extensionName);
                    }
                    else if (extensionName == VK_KHR_PORTABILITY_SUBSET_EXTENSION_NAME)
                    {
                        activeExtensions[activeExtensionCount++] = (IntPtr)properties[property].extensionName;
                        requiredDeviceExtensions.Remove(extensionName);
                    }
                    else if (requiredDeviceExtensions.Remove(extensionName))
                    {
                        activeExtensions[activeExtensionCount++] = (IntPtr)properties[property].extensionName;
                    }
                    else if (extensionName == VK_EXT_SUBGROUP_SIZE_CONTROL_EXTENSION_NAME)
                    {
                        // Compute Shader extensions (Vulkan 1.3)
                        activeExtensions[activeExtensionCount++] = (IntPtr)properties[property].extensionName;
                        is_VK_EXT_SUBGROUP_SIZE_CONTROL_EXTENSION_NAME_available = true;
                    }
                    else if (extensionName == VK_KHR_MAINTENANCE_4_EXTENSION_NAME)
                    {
                        // Required for the validation layer in 1.2 while it is 1.3.
                        activeExtensions[activeExtensionCount++] = (IntPtr)properties[property].extensionName;
                    }
                }
            }

            if (requiredDeviceExtensions.Count != 0)
            {
                string missingList = string.Join(", ", requiredDeviceExtensions);
                throw new GraphicsException($"The following Vulkan device extensions were not available: {missingList}");
            }


            StackList<nint> layerNames = new StackList<IntPtr>();
            if (Adapter.Manager.ValidationSupported)
            {
                layerNames.Add((nint)(byte*)VK_LAYER_KHRONOS_VALIDATION_EXTENSION_NAME);
            }

            var deviceCreateInfo = new VkDeviceCreateInfo
            {
                queueCreateInfoCount = 1,
                pQueueCreateInfos = &queueCreateInfo,
                pEnabledFeatures = &deviceFeatures,
                enabledLayerCount = layerNames.Count
            };

            if (layerNames.Count > 0)
            {
                deviceCreateInfo.ppEnabledLayerNames = (byte**)layerNames.Data;
            }

            // ------------------
            // TODO: TEMP LIST of features that we are forcing/enabling
            // BEGIN
            VkPhysicalDeviceVulkan11Features vulkan11Features = new()
            {
                sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_1_FEATURES,
                storageBuffer16BitAccess = VK_TRUE
            };


            VkPhysicalDeviceSubgroupSizeControlFeatures subgroupSizeControlFeatures = new()
            {
                sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SUBGROUP_SIZE_CONTROL_FEATURES,
                subgroupSizeControl = VK_TRUE,
                computeFullSubgroups = false // Not sure if this is needed
            };

            if (is_VK_EXT_SUBGROUP_SIZE_CONTROL_EXTENSION_NAME_available)
            {
                vulkan11Features.pNext = &subgroupSizeControlFeatures;
            }

            VkPhysicalDeviceVulkan12Features vulkan12Features = new()
            {
                sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_2_FEATURES,
                pNext = &vulkan11Features,
                bufferDeviceAddress = VK_TRUE,
                shaderFloat16 = VK_TRUE,
                shaderInt8 = VK_TRUE,
                scalarBlockLayout = VK_TRUE,
                vulkanMemoryModel = VK_TRUE,
                vulkanMemoryModelDeviceScope = VK_TRUE
            };
            
            //VkPhysicalDeviceVulkan13Features vulkan13Features = new()
            //{
            //    sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_3_FEATURES,
            //    maintenance4 = VK_TRUE,
            //    subgroupSizeControl = VK_TRUE,
            //    pNext = &vulkan12Features
            //};

            // END
            // ------------------

            // Create the logical device
            fixed (IntPtr* activeExtensionsPtr = activeExtensions)
            {
                deviceCreateInfo.enabledExtensionCount = activeExtensionCount;
                deviceCreateInfo.ppEnabledExtensionNames = (byte**)activeExtensionsPtr;
                deviceCreateInfo.pNext = &vulkan12Features;

                vkCreateDevice(_vkPhysicalDevice, deviceCreateInfo, null, out _vkDevice)
                    .VkCheck("Unable to create device.");
            }

            vkGetDeviceQueue(_vkDevice, _mainQueueFamilyIndex, 0, out _vkGraphicsQueue);

            // VK_KHR_surface
            vkGetPhysicalDeviceSurfaceCapabilitiesKHR = vkGetInstanceProcAddr<PFN_vkGetPhysicalDeviceSurfaceCapabilitiesKHR>(VkInstance);
            vkGetPhysicalDeviceSurfaceFormatsKHR = vkGetInstanceProcAddr<PFN_vkGetPhysicalDeviceSurfaceFormatsKHR>(VkInstance);
            vkGetPhysicalDeviceSurfacePresentModesKHR = vkGetInstanceProcAddr<PFN_vkGetPhysicalDeviceSurfacePresentModesKHR>(VkInstance);
            vkDestroySurfaceKHR = vkGetInstanceProcAddr<PFN_vkDestroySurfaceKHR>(VkInstance);
            
            // VK_KHR_swapchain
            vkAcquireNextImageKHR = vkGetDeviceProcAddr<PFN_vkAcquireNextImageKHR>(_vkDevice);
            vkCreateSwapchainKHR = vkGetDeviceProcAddr<PFN_vkCreateSwapchainKHR>(VkDevice);
            vkDestroySwapchainKHR = vkGetDeviceProcAddr<PFN_vkDestroySwapchainKHR>(VkDevice);
            _vkQueuePresentKHR = vkGetDeviceProcAddr<PFN_vkQueuePresentKHR>(VkDevice);
            vkGetSwapchainImagesKHR = vkGetDeviceProcAddr<PFN_vkGetSwapchainImagesKHR>(VkDevice);
            
            _memoryManager = new VkDeviceMemoryManager(
                _vkDevice,
                _vkPhysicalDevice,
                Adapter.PhysicalDeviceProperties,
                Adapter.PhysicalDeviceMemProperties);

            var subgroupSize = Adapter.PhysicalDeviceVulkan11Properties.subgroupSize;
            var minSubgroupSize = subgroupSize;
            var maxSubgroupSize = minSubgroupSize;
            if (Adapter.PhysicalDeviceSubgroupSizeControlFeatures.subgroupSizeControl)
            {
                minSubgroupSize = Adapter.PhysicalDeviceSubgroupSizeControlProperties.minSubgroupSize;
                maxSubgroupSize = Adapter.PhysicalDeviceSubgroupSizeControlProperties.maxSubgroupSize;
            }
            
            ref readonly var physicalDeviceFeatures = ref Adapter.PhysicalDeviceFeatures;
            Features = new GraphicsDeviceFeatures(
                computeShader: true,
                geometryShader: physicalDeviceFeatures.geometryShader,
                tessellationShaders: physicalDeviceFeatures.tessellationShader,
                multipleViewports: physicalDeviceFeatures.multiViewport,
                samplerLodBias: true,
                drawBaseVertex: true,
                drawBaseInstance: true,
                drawIndirect: true,
                drawIndirectBaseInstance: physicalDeviceFeatures.drawIndirectFirstInstance,
                fillModeWireframe: physicalDeviceFeatures.fillModeNonSolid,
                samplerAnisotropy: physicalDeviceFeatures.samplerAnisotropy,
                depthClipDisable: physicalDeviceFeatures.depthClamp,
                texture1D: true,
                independentBlend: physicalDeviceFeatures.independentBlend,
                structuredBuffer: true,
                subsetTextureView: true,
                commandListDebugMarkers: Adapter.Manager.IsDebugActivated,
                bufferRangeBinding: true,
                shaderFloat64: physicalDeviceFeatures.shaderFloat64)
            {
                SubgroupSize = subgroupSize,
                MinSubgroupSize = minSubgroupSize,
                MaxSubgroupSize = maxSubgroupSize
            };

            _descriptorPoolManager = new VkDescriptorPoolManager(this);
            CreateGraphicsCommandPool(out _graphicsCommandPool);
            for (int i = 0; i < SharedCommandPoolCount; i++)
            {
                _sharedGraphicsCommandPools.Push(new SharedCommandPool(this, true));
            }

            _vulkanInfo = new GraphicsDeviceBackendInfoVulkan(this);

            PointSampler = CreateSampler(SamplerDescription.Point);
            LinearSampler = CreateSampler(SamplerDescription.Linear);
            if (Features.SamplerAnisotropy)
            {
                AnisotropicSampler4x = CreateSampler(SamplerDescription.Aniso4x);
            }
        }

        private protected override void SubmitCommandsCore(CommandList cl, Fence? fence)
        {
            SubmitCommandList(cl, 0, null, 0, null, fence);
        }

        private void SubmitCommandList(
            CommandList cl,
            uint waitSemaphoreCount,
            VkSemaphore* waitSemaphoresPtr,
            uint signalSemaphoreCount,
            VkSemaphore* signalSemaphoresPtr,
            Fence? fence)
        {
            VkCommandList vkCL = Util.AssertSubtype<CommandList, VkCommandList>(cl);
            VkCommandBuffer vkCB = vkCL.CommandBuffer;

            vkCL.CommandBufferSubmitted(vkCB);
            SubmitCommandBuffer(vkCL, vkCB, waitSemaphoreCount, waitSemaphoresPtr, signalSemaphoreCount, signalSemaphoresPtr, fence);
        }

        private void SubmitCommandBuffer(
            VkCommandList? vkCL,
            VkCommandBuffer vkCB,
            uint waitSemaphoreCount,
            VkSemaphore* waitSemaphoresPtr,
            uint signalSemaphoreCount,
            VkSemaphore* signalSemaphoresPtr,
            Fence? fence)
        {
            CheckSubmittedFences();

            VkSubmitInfo si = new VkSubmitInfo();
            si.commandBufferCount = 1;
            si.pCommandBuffers = &vkCB;
            VkPipelineStageFlags waitDstStageMask = VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT;
            si.pWaitDstStageMask = &waitDstStageMask;

            si.pWaitSemaphores = waitSemaphoresPtr;
            si.waitSemaphoreCount = waitSemaphoreCount;
            si.pSignalSemaphores = signalSemaphoresPtr;
            si.signalSemaphoreCount = signalSemaphoreCount;

            XenoAtom.Interop.vulkan.VkFence vkFence = default;
            XenoAtom.Interop.vulkan.VkFence submissionFence = default;
            if (fence != null)
            {
                vkFence = Util.AssertSubtype<Fence, VkFence>(fence!).DeviceFence;
                submissionFence = GetFreeSubmissionFence();
            }
            else
            {
                vkFence = GetFreeSubmissionFence();
                submissionFence = vkFence;
            }

            lock (GraphicsQueueLock)
            {
                vkQueueSubmit(_vkGraphicsQueue, 1, &si, vkFence)
                    .VkCheck("Error while submitting command buffer");

                if (fence != null)
                {
                    vkQueueSubmit(_vkGraphicsQueue, 0, null, submissionFence)
                        .VkCheck("Error while submitting command buffer with fence");
                }
            }

            lock (_submittedFencesLock)
            {
                _submittedFences.Add(new FenceSubmissionInfo(submissionFence, vkCL, vkCB));
            }
        }

        private void CheckSubmittedFences()
        {
            lock (_submittedFencesLock)
            {
                for (int i = 0; i < _submittedFences.Count; i++)
                {
                    FenceSubmissionInfo fsi = _submittedFences[i];
                    if (vkGetFenceStatus(_vkDevice, fsi.Fence) == VK_SUCCESS)
                    {
                        CompleteFenceSubmission(fsi);
                        _submittedFences.RemoveAt(i);
                        i -= 1;
                    }
                    else
                    {
                        break; // Submissions are in order; later submissions cannot complete if this one hasn't.
                    }
                }
            }
        }

        private void CompleteFenceSubmission(FenceSubmissionInfo fsi)
        {
            XenoAtom.Interop.vulkan.VkFence fence = fsi.Fence;
            VkCommandBuffer completedCB = fsi.CommandBuffer;
            fsi.CommandList?.CommandBufferCompleted(completedCB);
            vkResetFences(_vkDevice, 1, &fence)
                .VkCheck("Unable to reset fence");
            ReturnSubmissionFence(fence);
            lock (_stagingResourcesLock)
            {
                if (_submittedStagingTextures.TryGetValue(completedCB, out var stagingTex))
                {
                    _submittedStagingTextures.Remove(completedCB);
                    _availableStagingTextures.Add(stagingTex);
                }
                if (_submittedStagingBuffers.TryGetValue(completedCB, out var stagingBuffer))
                {
                    _submittedStagingBuffers.Remove(completedCB);
                    if (stagingBuffer.SizeInBytes <= MaxStagingBufferSize)
                    {
                        _availableStagingBuffers.Add(stagingBuffer);
                    }
                    else
                    {
                        stagingBuffer.Dispose();
                    }
                }
                if (_submittedSharedCommandPools.TryGetValue(completedCB, out var sharedPool))
                {
                    _submittedSharedCommandPools.Remove(completedCB);
                    lock (_graphicsCommandPoolLock)
                    {
                        if (sharedPool.IsCached)
                        {
                            _sharedGraphicsCommandPools.Push(sharedPool);
                        }
                        else
                        {
                            sharedPool.Destroy();
                        }
                    }
                }
            }
        }

        private void ReturnSubmissionFence(XenoAtom.Interop.vulkan.VkFence fence)
        {
            _availableSubmissionFences.Enqueue(fence);
        }

        private XenoAtom.Interop.vulkan.VkFence GetFreeSubmissionFence()
        {
            if (_availableSubmissionFences.TryDequeue(out XenoAtom.Interop.vulkan.VkFence availableFence))
            {
                return availableFence;
            }
            else
            {
                VkFenceCreateInfo fenceCI = new VkFenceCreateInfo();
                vkCreateFence(_vkDevice, fenceCI, null, out XenoAtom.Interop.vulkan.VkFence newFence)
                    .VkCheck("Unable to create fence");
                return newFence;
            }
        }

        internal override void SetResourceName(GraphicsDeviceObject resource, string? name)
        {
            if (!_isDebugActivated)
            {
                return;
            }

            switch (resource)
            {
                case VkBuffer buffer:
                    SetDebugMarkerName(VK_OBJECT_TYPE_BUFFER, (ulong)buffer.DeviceBuffer.Value.Handle, name);
                    break;
                case VkCommandList commandList:
                    SetDebugMarkerName(
                        VK_OBJECT_TYPE_COMMAND_BUFFER,
                        (ulong)commandList.CommandBuffer.Value.Handle,
                        $"{name}_CommandBuffer");
                    SetDebugMarkerName(
                        VK_OBJECT_TYPE_COMMAND_POOL,
                        (ulong)commandList.CommandPool.Value.Handle,
                        $"{name}_CommandPool");
                    break;
                case VkFramebuffer framebuffer:
                    SetDebugMarkerName(
                        VK_OBJECT_TYPE_FRAMEBUFFER,
                        (ulong)framebuffer.CurrentFramebuffer.Value.Handle,
                        name);
                    break;
                case VkPipeline pipeline:
                    SetDebugMarkerName(VK_OBJECT_TYPE_PIPELINE, (ulong)pipeline.DevicePipeline.Value.Handle, name);
                    SetDebugMarkerName(VK_OBJECT_TYPE_PIPELINE_LAYOUT, (ulong)pipeline.PipelineLayout.Value.Handle, name);
                    break;
                case VkResourceLayout resourceLayout:
                    SetDebugMarkerName(
                        VK_OBJECT_TYPE_DESCRIPTOR_SET_LAYOUT,
                        (ulong)resourceLayout.DescriptorSetLayout.Value.Handle,
                        name);
                    break;
                case VkResourceSet resourceSet:
                    SetDebugMarkerName(VK_OBJECT_TYPE_DESCRIPTOR_SET, (ulong)resourceSet.DescriptorSet.Value.Handle, name);
                    break;
                case VkSampler sampler:
                    SetDebugMarkerName(VK_OBJECT_TYPE_SAMPLER, (ulong)sampler.DeviceSampler.Value.Handle, name);
                    break;
                case VkShader shader:
                    SetDebugMarkerName(VK_OBJECT_TYPE_SHADER_MODULE, (ulong)shader.ShaderModule.Value.Handle, name);
                    break;
                case VkTexture tex:
                    SetDebugMarkerName(VK_OBJECT_TYPE_IMAGE, (ulong)tex.OptimalDeviceImage.Value.Handle, name);
                    break;
                case VkTextureView texView:
                    SetDebugMarkerName(VK_OBJECT_TYPE_IMAGE_VIEW, (ulong)texView.ImageView.Value.Handle, name);
                    break;
                case VkFence fence:
                    SetDebugMarkerName(VK_OBJECT_TYPE_FENCE, (ulong)fence.DeviceFence.Value.Handle, name);
                    break;
                case VkSwapchain sc:
                    SetDebugMarkerName(VK_OBJECT_TYPE_SWAPCHAIN_KHR, (ulong)sc.DeviceSwapchain.Value.Handle, name);
                    break;
                default:
                    break;
            }
        }

        private void SetDebugMarkerName(VkObjectType objectType, ulong target, string? name)
        {
            Debug.Assert(_vkSetDebugUtilsObjectNameEXT != default);

            name ??= string.Empty;

            var nameInfo = new VkDebugUtilsObjectNameInfoEXT
            {
                objectType = objectType,
                objectHandle = target,

            };

            int byteCount = Encoding.UTF8.GetByteCount(name);
            byte* utf8Ptr = stackalloc byte[byteCount + 1];
            fixed (char* namePtr = name)
            {
                Encoding.UTF8.GetBytes(namePtr, name.Length, utf8Ptr, byteCount);
            }
            utf8Ptr[byteCount] = 0;

            nameInfo.pObjectName = utf8Ptr;
            _vkSetDebugUtilsObjectNameEXT.Invoke(_vkDevice, &nameInfo)
                .VkCheck("Unable to set debug marker object name");
        }

        public bool HasSurfaceExtension(ReadOnlyMemoryUtf8 extension)
        {
            return Manager.HasSurfaceExtension(extension);
        }
    
        public VkExtensionProperties[] GetDeviceExtensionProperties()
        {
            vkEnumerateDeviceExtensionProperties(_vkPhysicalDevice, default, out var propertyCount)
                .VkCheck("Unable to enumerate device extensions");
            VkExtensionProperties[] props = new VkExtensionProperties[(int)propertyCount];
            vkEnumerateDeviceExtensionProperties(_vkPhysicalDevice, default, props)
                .VkCheck("Unable to enumerate device extensions");
            return props;
        }

        private void CreateGraphicsCommandPool(out VkCommandPool commandPool)
        {
            VkCommandPoolCreateInfo commandPoolCI = new()
            {
                flags = VK_COMMAND_POOL_CREATE_RESET_COMMAND_BUFFER_BIT,
                queueFamilyIndex = _mainQueueFamilyIndex
            };
            vkCreateCommandPool(_vkDevice, commandPoolCI, null, out commandPool)
                .VkCheck("Unable to create Graphics Command Pool");
        }

        protected override MappedResource MapCore(IMappableResource resource, MapMode mode, uint subresource)
        {
            VkDeviceMemoryChunkRange memoryBlock;
            IntPtr mappedPtr = IntPtr.Zero;
            uint sizeInBytes;
            uint offset = 0;
            uint rowPitch = 0;
            uint depthPitch = 0;
            if (resource is VkBuffer buffer)
            {
                memoryBlock = buffer.Memory;
                sizeInBytes = buffer.SizeInBytes;
            }
            else
            {
                VkTexture texture = Util.AssertSubtype<IMappableResource, VkTexture>(resource);
                VkSubresourceLayout layout = texture.GetSubresourceLayout(subresource);
                memoryBlock = texture.Memory;
                sizeInBytes = (uint)layout.size;
                offset = (uint)layout.offset;
                rowPitch = (uint)layout.rowPitch;
                depthPitch = (uint)layout.depthPitch;
            }

            if (memoryBlock.DeviceMemory.Value.Handle != 0)
            {
                mappedPtr = _memoryManager.Map(memoryBlock);
            }

            byte* dataPtr = (byte*)mappedPtr.ToPointer() + offset;
            return new MappedResource(
                resource,
                mode,
                (IntPtr)dataPtr,
                sizeInBytes,
                subresource,
                rowPitch,
                depthPitch);
        }

        protected override void UnmapCore(IMappableResource resource, uint subresource)
        {
            VkDeviceMemoryChunkRange memoryBlock;
            if (resource is VkBuffer buffer)
            {
                memoryBlock = buffer.Memory;
            }
            else
            {
                VkTexture tex = Util.AssertSubtype<IMappableResource, VkTexture>(resource);
                memoryBlock = tex.Memory;
            }

            if (memoryBlock.DeviceMemory.Value.Handle != 0)
            {
                _memoryManager.Unmap(memoryBlock);
            }
        }

        private protected override void WaitForIdleCore()
        {
            lock (GraphicsQueueLock)
            {
                vkQueueWaitIdle(_vkGraphicsQueue);
            }

            CheckSubmittedFences();
        }

        public override TextureSampleCount GetSampleCountLimit(PixelFormat format, bool depthFormat)
        {
            VkImageUsageFlags usageFlags = VK_IMAGE_USAGE_SAMPLED_BIT;
            usageFlags |= depthFormat ? VK_IMAGE_USAGE_DEPTH_STENCIL_ATTACHMENT_BIT : VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT;

            vkGetPhysicalDeviceImageFormatProperties(
                _vkPhysicalDevice,
                VkFormats.VdToVkPixelFormat(format),
                VK_IMAGE_TYPE_2D,
                VK_IMAGE_TILING_OPTIMAL,
                usageFlags,
                (VkImageCreateFlags)0,
                out VkImageFormatProperties formatProperties);

            VkSampleCountFlags vkSampleCounts = formatProperties.sampleCounts;
            if ((vkSampleCounts & VK_SAMPLE_COUNT_32_BIT) == VK_SAMPLE_COUNT_32_BIT)
            {
                return TextureSampleCount.Count32;
            }
            else if ((vkSampleCounts & VK_SAMPLE_COUNT_16_BIT) == VK_SAMPLE_COUNT_16_BIT)
            {
                return TextureSampleCount.Count16;
            }
            else if ((vkSampleCounts & VK_SAMPLE_COUNT_8_BIT) == VK_SAMPLE_COUNT_8_BIT)
            {
                return TextureSampleCount.Count8;
            }
            else if ((vkSampleCounts & VK_SAMPLE_COUNT_4_BIT) == VK_SAMPLE_COUNT_4_BIT)
            {
                return TextureSampleCount.Count4;
            }
            else if ((vkSampleCounts & VK_SAMPLE_COUNT_2_BIT) == VK_SAMPLE_COUNT_2_BIT)
            {
                return TextureSampleCount.Count2;
            }

            return TextureSampleCount.Count1;
        }

        private protected override bool GetPixelFormatSupportCore(
            PixelFormat format,
            TextureKind kind,
            TextureUsage usage,
            out PixelFormatProperties properties)
        {
            VkFormat vkFormat = VkFormats.VdToVkPixelFormat(format, (usage & TextureUsage.DepthStencil) != 0);
            VkImageType vkType = VkFormats.VdToVkTextureType(kind);
            VkImageTiling tiling = usage == TextureUsage.Staging ? VK_IMAGE_TILING_LINEAR : VK_IMAGE_TILING_OPTIMAL;
            VkImageUsageFlags vkUsage = VkFormats.VdToVkTextureUsage(usage);

            VkResult result = vkGetPhysicalDeviceImageFormatProperties(
                _vkPhysicalDevice,
                vkFormat,
                vkType,
                tiling,
                vkUsage,
                (VkImageCreateFlags)0,
                out VkImageFormatProperties vkProps);

            if (result == VK_ERROR_FORMAT_NOT_SUPPORTED)
            {
                properties = default;
                return false;
            }
            result.VkCheck("Unable to get pixel image format properties");

            properties = new PixelFormatProperties(
               vkProps.maxExtent.width,
               vkProps.maxExtent.height,
               vkProps.maxExtent.depth,
               vkProps.maxMipLevels,
               vkProps.maxArrayLayers,
               (uint)vkProps.sampleCounts);
            return true;
        }

        private protected override void DumpStatisticsCore(StringBuilder builder)
        {
            _memoryManager.DumpStatistics(builder);
        }

        internal VkFilter GetFormatFilter(VkFormat format)
        {
            if (!_filters.TryGetValue(format, out VkFilter filter))
            {
                vkGetPhysicalDeviceFormatProperties(_vkPhysicalDevice, format, out VkFormatProperties vkFormatProps);
                filter = (vkFormatProps.optimalTilingFeatures & VK_FORMAT_FEATURE_SAMPLED_IMAGE_FILTER_LINEAR_BIT) != 0
                    ? VK_FILTER_LINEAR
                    : VK_FILTER_NEAREST;
                _filters.TryAdd(format, filter);
            }

            return filter;
        }

        private protected override void UpdateBufferCore(DeviceBuffer buffer, uint bufferOffsetInBytes, IntPtr source, uint sizeInBytes)
        {
            VkBuffer vkBuffer = Util.AssertSubtype<DeviceBuffer, VkBuffer>(buffer);
            VkBuffer? copySrcVkBuffer = null;
            IntPtr mappedPtr;
            byte* destPtr;
            bool isPersistentMapped = vkBuffer.Memory.IsPersistentMapped;
            if (isPersistentMapped)
            {
                mappedPtr = (IntPtr)vkBuffer.Memory.MappedPointerWithOffset;
                destPtr = (byte*)mappedPtr + bufferOffsetInBytes;
            }
            else
            {
                copySrcVkBuffer = GetFreeStagingBuffer(sizeInBytes);
                mappedPtr = (IntPtr)copySrcVkBuffer.Memory.MappedPointerWithOffset;
                destPtr = (byte*)mappedPtr;
            }

            Unsafe.CopyBlock(destPtr, source.ToPointer(), sizeInBytes);

            if (!isPersistentMapped)
            {
                SharedCommandPool pool = GetFreeCommandPool();
                VkCommandBuffer cb = pool.BeginNewCommandBuffer();

                VkBufferCopy copyRegion = new VkBufferCopy
                {
                    dstOffset = bufferOffsetInBytes,
                    size = sizeInBytes
                };
                vkCmdCopyBuffer(cb, copySrcVkBuffer!.DeviceBuffer, vkBuffer.DeviceBuffer, 1, &copyRegion);

                pool.EndAndSubmit(cb);
                lock (_stagingResourcesLock)
                {
                    _submittedStagingBuffers.Add(cb, copySrcVkBuffer);
                }
            }
        }

        private SharedCommandPool GetFreeCommandPool()
        {
            SharedCommandPool? sharedPool = null;
            lock (_graphicsCommandPoolLock)
            {
                if (_sharedGraphicsCommandPools.Count > 0)
                    sharedPool = _sharedGraphicsCommandPools.Pop();
            }

            sharedPool ??= new SharedCommandPool(this, false);
            return sharedPool;
        }

        private IntPtr MapBuffer(VkBuffer buffer, uint numBytes) => MemoryManager.Map(buffer.Memory);

        private void UnmapBuffer(VkBuffer buffer) => MemoryManager.Unmap(buffer.Memory);

        private protected override void UpdateTextureCore(
            Texture texture,
            IntPtr source,
            uint sizeInBytes,
            uint x,
            uint y,
            uint z,
            uint width,
            uint height,
            uint depth,
            uint mipLevel,
            uint arrayLayer)
        {
            VkTexture vkTex = Util.AssertSubtype<Texture, VkTexture>(texture);
            bool isStaging = (vkTex.Usage & TextureUsage.Staging) != 0;
            if (isStaging)
            {
                VkDeviceMemoryChunkRange memBlock = vkTex.Memory;
                uint subresource = texture.CalculateSubresource(mipLevel, arrayLayer);
                VkSubresourceLayout layout = vkTex.GetSubresourceLayout(subresource);
                byte* imageBasePtr = (byte*)memBlock.MappedPointerWithOffset + layout.offset;

                uint srcRowPitch = FormatHelpers.GetRowPitch(width, texture.Format);
                uint srcDepthPitch = FormatHelpers.GetDepthPitch(srcRowPitch, height, texture.Format);
                Util.CopyTextureRegion(
                    source.ToPointer(),
                    0, 0, 0,
                    srcRowPitch, srcDepthPitch,
                    imageBasePtr,
                    x, y, z,
                    (uint)layout.rowPitch, (uint)layout.depthPitch,
                    width, height, depth,
                    texture.Format);
            }
            else
            {
                VkTexture stagingTex = GetFreeStagingTexture(width, height, depth, texture.Format);
                UpdateTexture(stagingTex, source, sizeInBytes, 0, 0, 0, width, height, depth, 0, 0);
                SharedCommandPool pool = GetFreeCommandPool();
                VkCommandBuffer cb = pool.BeginNewCommandBuffer();
                VkCommandList.CopyTextureCore_VkCommandBuffer(
                    cb,
                    stagingTex, 0, 0, 0, 0, 0,
                    texture, x, y, z, mipLevel, arrayLayer,
                    width, height, depth, 1);
                lock (_stagingResourcesLock)
                {
                    _submittedStagingTextures.Add(cb, stagingTex);
                }
                pool.EndAndSubmit(cb);
            }
        }

        private VkTexture GetFreeStagingTexture(uint width, uint height, uint depth, PixelFormat format)
        {
            uint totalSize = FormatHelpers.GetRegionSize(width, height, depth, format);
            lock (_stagingResourcesLock)
            {
                for (int i = 0; i < _availableStagingTextures.Count; i++)
                {
                    VkTexture tex = _availableStagingTextures[i];
                    if (tex.Memory.Size >= totalSize)
                    {
                        _availableStagingTextures.RemoveAt(i);
                        tex.SetStagingDimensions(width, height, depth, format);
                        return tex;
                    }
                }
            }

            uint texWidth = Math.Max(256, width);
            uint texHeight = Math.Max(256, height);
            VkTexture newTex = (VkTexture)CreateTexture(TextureDescription.Texture3D(
                texWidth, texHeight, depth, 1, format, TextureUsage.Staging));
            newTex.SetStagingDimensions(width, height, depth, format);

            return newTex;
        }

        private VkBuffer GetFreeStagingBuffer(uint size)
        {
            lock (_stagingResourcesLock)
            {
                for (int i = 0; i < _availableStagingBuffers.Count; i++)
                {
                    VkBuffer buffer = _availableStagingBuffers[i];
                    if (buffer.SizeInBytes >= size)
                    {
                        _availableStagingBuffers.RemoveAt(i);
                        return buffer;
                    }
                }
            }

            uint newBufferSize = Math.Max(MinStagingBufferSize, size);
            VkBuffer newBuffer = (VkBuffer)CreateBuffer(
                new BufferDescription(newBufferSize, BufferUsage.Staging));
            return newBuffer;
        }

        public override void ResetFence(Fence fence)
        {
            XenoAtom.Interop.vulkan.VkFence vkFence = Util.AssertSubtype<Fence, VkFence>(fence).DeviceFence;
            vkResetFences(_vkDevice, 1, &vkFence);
        }

        public override bool WaitForFence(Fence fence, ulong nanosecondTimeout)
        {
            XenoAtom.Interop.vulkan.VkFence vkFence = Util.AssertSubtype<Fence, VkFence>(fence).DeviceFence;
            VkResult result = vkWaitForFences(_vkDevice, 1, &vkFence, true, nanosecondTimeout);
            return result == VK_SUCCESS;
        }

        public override bool WaitForFences(Fence[] fences, bool waitAll, ulong nanosecondTimeout)
        {
            int fenceCount = fences.Length;
            XenoAtom.Interop.vulkan.VkFence* fencesPtr = stackalloc XenoAtom.Interop.vulkan.VkFence[fenceCount];
            for (int i = 0; i < fenceCount; i++)
            {
                fencesPtr[i] = Util.AssertSubtype<Fence, VkFence>(fences[i]).DeviceFence;
            }

            VkResult result = vkWaitForFences(_vkDevice, (uint)fenceCount, fencesPtr, waitAll, nanosecondTimeout);
            return result == VK_SUCCESS;
        }

        internal void ClearColorTexture(VkTexture texture, VkClearColorValue color)
        {
            uint effectiveLayers = texture.ArrayLayers;
            if ((texture.Usage & TextureUsage.Cubemap) != 0)
            {
                effectiveLayers *= 6;
            }

            VkImageSubresourceRange range = new()
            {
                aspectMask = VK_IMAGE_ASPECT_COLOR_BIT,
                baseMipLevel = 0,
                levelCount = texture.MipLevels,
                baseArrayLayer = 0,
                layerCount = effectiveLayers
            };
            SharedCommandPool pool = GetFreeCommandPool();
            VkCommandBuffer cb = pool.BeginNewCommandBuffer();
            texture.TransitionImageLayout(cb, 0, texture.MipLevels, 0, effectiveLayers, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL);
            vkCmdClearColorImage(cb, texture.OptimalDeviceImage, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, &color, 1, &range);
            VkImageLayout colorLayout = texture.IsSwapchainTexture ? VK_IMAGE_LAYOUT_PRESENT_SRC_KHR : VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;
            texture.TransitionImageLayout(cb, 0, texture.MipLevels, 0, effectiveLayers, colorLayout);
            pool.EndAndSubmit(cb);
        }

        internal void ClearDepthTexture(VkTexture texture, VkClearDepthStencilValue clearValue)
        {
            uint effectiveLayers = texture.ArrayLayers;
            if ((texture.Usage & TextureUsage.Cubemap) != 0)
            {
                effectiveLayers *= 6;
            }
            VkImageAspectFlags aspect = FormatHelpers.IsStencilFormat(texture.Format)
                ? VK_IMAGE_ASPECT_DEPTH_BIT | VK_IMAGE_ASPECT_STENCIL_BIT
                : VK_IMAGE_ASPECT_DEPTH_BIT;

            VkImageSubresourceRange range = new()
            {
                aspectMask = aspect,
                baseMipLevel = 0,
                levelCount = texture.MipLevels,
                baseArrayLayer = 0,
                layerCount = effectiveLayers
            };
            SharedCommandPool pool = GetFreeCommandPool();
            VkCommandBuffer cb = pool.BeginNewCommandBuffer();
            texture.TransitionImageLayout(cb, 0, texture.MipLevels, 0, effectiveLayers, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL);
            vkCmdClearDepthStencilImage(
                cb,
                texture.OptimalDeviceImage,
                VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,
                &clearValue,
                1,
                &range);
            texture.TransitionImageLayout(cb, 0, texture.MipLevels, 0, effectiveLayers, VK_IMAGE_LAYOUT_DEPTH_STENCIL_ATTACHMENT_OPTIMAL);
            pool.EndAndSubmit(cb);
        }

        internal override uint GetUniformBufferMinOffsetAlignmentCore()
            => (uint)Adapter.PhysicalDeviceProperties.limits.minUniformBufferOffsetAlignment;

        internal override uint GetStructuredBufferMinOffsetAlignmentCore()
            => (uint)Adapter.PhysicalDeviceProperties.limits.minStorageBufferOffsetAlignment;

        internal void TransitionImageLayout(VkTexture texture, VkImageLayout layout)
        {
            SharedCommandPool pool = GetFreeCommandPool();
            VkCommandBuffer cb = pool.BeginNewCommandBuffer();
            texture.TransitionImageLayout(cb, 0, texture.MipLevels, 0, texture.ActualArrayLayers, layout);
            pool.EndAndSubmit(cb);
        }

        private class SharedCommandPool
        {
            private readonly VkGraphicsDevice _gd;
            private readonly VkCommandPool _pool;
            private readonly VkCommandBuffer _cb;

            public bool IsCached { get; }

            public SharedCommandPool(VkGraphicsDevice gd, bool isCached)
            {
                _gd = gd;
                IsCached = isCached;

                VkCommandPoolCreateInfo commandPoolCI = new VkCommandPoolCreateInfo();
                commandPoolCI.flags = VK_COMMAND_POOL_CREATE_TRANSIENT_BIT | VK_COMMAND_POOL_CREATE_RESET_COMMAND_BUFFER_BIT;
                commandPoolCI.queueFamilyIndex = _gd.MainQueueFamilyIndex;
                vkCreateCommandPool(_gd.VkDevice, commandPoolCI, null, out _pool)
                    .VkCheck("Unable to create shared command pool");

                VkCommandBufferAllocateInfo allocateInfo = new VkCommandBufferAllocateInfo();
                allocateInfo.commandBufferCount = 1;
                allocateInfo.level = VK_COMMAND_BUFFER_LEVEL_PRIMARY;
                allocateInfo.commandPool = _pool;

                VkCommandBuffer cb;
                vkAllocateCommandBuffers(_gd.VkDevice, allocateInfo, &cb)
                    .VkCheck("Unable to allocate shared command buffer");
                _cb = cb;
            }

            public VkCommandBuffer BeginNewCommandBuffer()
            {
                VkCommandBufferBeginInfo beginInfo = new() { flags = VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT };
                vkBeginCommandBuffer(_cb, beginInfo)
                    .VkCheck("Unable to begin shared command buffer");

                return _cb;
            }

            public void EndAndSubmit(VkCommandBuffer cb)
            {
                vkEndCommandBuffer(cb)
                    .VkCheck("Unable to end shared command buffer");

                _gd.SubmitCommandBuffer(null, cb, 0, null, 0, null, null);
                lock (_gd._stagingResourcesLock)
                {
                    _gd._submittedSharedCommandPools.Add(cb, this);
                }
            }

            internal void Destroy()
            {
                vkDestroyCommandPool(_gd.VkDevice, _pool, null);
            }
        }

        private struct FenceSubmissionInfo
        {
            public XenoAtom.Interop.vulkan.VkFence Fence;

            public VkCommandList? CommandList;

            public VkCommandBuffer CommandBuffer;
            public FenceSubmissionInfo(XenoAtom.Interop.vulkan.VkFence fence, VkCommandList? commandList, VkCommandBuffer commandBuffer)
            {
                Fence = fence;
                CommandList = commandList;
                CommandBuffer = commandBuffer;
            }
        }

        internal override void Destroy()
        {
            WaitForIdle();
            PointSampler.Dispose();
            LinearSampler.Dispose();
            AnisotropicSampler4x?.Dispose();

            Debug.Assert(_submittedFences.Count == 0);
            foreach (XenoAtom.Interop.vulkan.VkFence fence in _availableSubmissionFences)
            {
                vkDestroyFence(_vkDevice, fence, null);
            }

            _descriptorPoolManager.DestroyAll();
            vkDestroyCommandPool(_vkDevice, _graphicsCommandPool, null);

            Debug.Assert(_submittedStagingTextures.Count == 0);
            foreach (VkTexture tex in _availableStagingTextures)
            {
                tex.Dispose();
            }

            Debug.Assert(_submittedStagingBuffers.Count == 0);
            foreach (VkBuffer buffer in _availableStagingBuffers)
            {
                buffer.Dispose();
            }

            lock (_graphicsCommandPoolLock)
            {
                while (_sharedGraphicsCommandPools.Count > 0)
                {
                    SharedCommandPool sharedPool = _sharedGraphicsCommandPools.Pop();
                    sharedPool.Destroy();
                }
            }

            _memoryManager.Dispose();

            vkDeviceWaitIdle(_vkDevice)
                .VkCheck("Unable to wait for idle on device");
            vkDestroyDevice(_vkDevice, null);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator VkDevice(VkGraphicsDevice gd) => gd._vkDevice;
    }
}


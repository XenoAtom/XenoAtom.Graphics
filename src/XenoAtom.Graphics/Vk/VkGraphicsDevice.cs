using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using static XenoAtom.Interop.vulkan;
using static XenoAtom.Graphics.Vk.VulkanUtil;
using XenoAtom.Interop;


namespace XenoAtom.Graphics.Vk
{
    internal sealed unsafe class VkGraphicsDevice : GraphicsDevice
    {
        private static ReadOnlyMemoryUtf8 DefaultAppName => "XenoAtom.Graphics-VkGraphicsDevice"u8;
        private static readonly Lazy<bool> s_isSupported = new Lazy<bool>(CheckIsSupported, isThreadSafe: true);

        private readonly VkInstance _instance;
        private readonly VkPhysicalDevice _physicalDevice;
        private readonly string _deviceName;
        private readonly string _vendorName;
        private readonly GraphicsApiVersion _apiVersion;
        private readonly string _driverName;
        private readonly string _driverInfo;
        private readonly VkDeviceMemoryManager _memoryManager;
        private readonly VkPhysicalDeviceProperties _physicalDeviceProperties;
        private readonly VkPhysicalDeviceFeatures _physicalDeviceFeatures;
        private readonly VkPhysicalDeviceMemoryProperties _physicalDeviceMemProperties;
        private readonly VkDevice _device;
        private readonly uint _graphicsQueueIndex;
        private readonly uint _presentQueueIndex;
        private readonly VkCommandPool _graphicsCommandPool;
        private readonly object _graphicsCommandPoolLock = new object();
        private readonly VkQueue _graphicsQueue;
        private readonly object _graphicsQueueLock = new object();
        private readonly ConcurrentDictionary<VkFormat, VkFilter> _filters = new ConcurrentDictionary<VkFormat, VkFilter>();
        private readonly BackendInfoVulkan _vulkanInfo;
        private readonly DebugLogDelegate? _debugLog;
        private readonly GCHandle _thisGcHandle;

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

        public override string DeviceName => _deviceName;

        public override string VendorName => _vendorName;

        public override GraphicsApiVersion ApiVersion => _apiVersion;

        public override GraphicsBackend BackendType => GraphicsBackend.Vulkan;

        public override bool IsUvOriginTopLeft => true;

        public override bool IsDepthRangeZeroToOne => true;

        public override bool IsClipSpaceYInverted => !_standardClipYDirection;

        public override Swapchain? MainSwapchain => _mainSwapchain;

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

        public override bool TryGetVulkanInfo([NotNullWhen(true)] out BackendInfoVulkan? info)
        {
            info = _vulkanInfo;
            return true;
        }

        public VkInstance Instance => _instance;
        public VkDevice Device => _device;
        public VkPhysicalDevice PhysicalDevice => _physicalDevice;
        public VkPhysicalDeviceMemoryProperties PhysicalDeviceMemProperties => _physicalDeviceMemProperties;
        public VkQueue GraphicsQueue => _graphicsQueue;
        public uint GraphicsQueueIndex => _graphicsQueueIndex;
        public uint PresentQueueIndex => _presentQueueIndex;
        public string DriverName => _driverName;
        public string DriverInfo => _driverInfo;
        public VkDeviceMemoryManager MemoryManager => _memoryManager;
        public VkDescriptorPoolManager DescriptorPoolManager => _descriptorPoolManager;
        public PFN_vkCreateMetalSurfaceEXT CreateMetalSurfaceEXT => _createMetalSurfaceEXT;

        private readonly PFN_vkQueuePresentKHR _vkQueuePresentKHR;
        private readonly PFN_vkCreateMetalSurfaceEXT _createMetalSurfaceEXT;

        public readonly PFN_vkGetPhysicalDeviceSurfaceSupportKHR vkGetPhysicalDeviceSurfaceSupportKHR;
        public readonly PFN_vkAcquireNextImageKHR vkAcquireNextImageKHR;
        public readonly PFN_vkGetPhysicalDeviceSurfaceCapabilitiesKHR vkGetPhysicalDeviceSurfaceCapabilitiesKHR;
        public readonly PFN_vkGetPhysicalDeviceSurfaceFormatsKHR vkGetPhysicalDeviceSurfaceFormatsKHR;
        public readonly PFN_vkGetPhysicalDeviceSurfacePresentModesKHR vkGetPhysicalDeviceSurfacePresentModesKHR;
        public readonly PFN_vkCreateSwapchainKHR vkCreateSwapchainKHR;
        public readonly PFN_vkDestroySwapchainKHR vkDestroySwapchainKHR;
        public readonly PFN_vkDestroySurfaceKHR vkDestroySurfaceKHR;
        public readonly PFN_vkGetSwapchainImagesKHR vkGetSwapchainImagesKHR;

        // Function pointers for VK_EXT_debug_utils
        private readonly bool _debugUtilsExtensionAvailable;
        private readonly PFN_vkCreateDebugUtilsMessengerEXT _vkCreateDebugUtilsMessengerExt;
        private readonly PFN_vkDestroyDebugUtilsMessengerEXT _vkDestroyDebugUtilsMessengerExt;
        private readonly PFN_vkSetDebugUtilsObjectNameEXT _vkSetDebugUtilsObjectNameEXT;

        public readonly PFN_vkCmdBeginDebugUtilsLabelEXT vkCmdBeginDebugUtilsLabelExt;
        public readonly PFN_vkCmdEndDebugUtilsLabelEXT vkCmdEndDebugUtilsLabelExt;
        public readonly PFN_vkCmdInsertDebugUtilsLabelEXT vkCmdInsertDebugUtilsLabelExt;

        public readonly PFN_vkQueueBeginDebugUtilsLabelEXT vkQueueBeginDebugUtilsLabelExt;
        public readonly PFN_vkQueueEndDebugUtilsLabelEXT vkQueueEndDebugUtilsLabelExt;
        public readonly PFN_vkQueueInsertDebugUtilsLabelEXT vkQueueInsertDebugUtilsLabelExt;
        private readonly VkDebugUtilsMessengerEXT _debugUtilsMessenger;

        private readonly object _submittedFencesLock = new object();
        private readonly ConcurrentQueue<XenoAtom.Interop.vulkan.VkFence> _availableSubmissionFences = new ConcurrentQueue<XenoAtom.Interop.vulkan.VkFence>();
        private readonly List<FenceSubmissionInfo> _submittedFences = new List<FenceSubmissionInfo>();
        private readonly VkSwapchain? _mainSwapchain;

        private readonly List<ReadOnlyMemoryUtf8> _surfaceExtensions = new List<ReadOnlyMemoryUtf8>();

        public VkGraphicsDevice(GraphicsDeviceOptions options, SwapchainDescription? scDesc)
            : this(options, scDesc, new VulkanDeviceOptions()) { }

        public VkGraphicsDevice(GraphicsDeviceOptions options, SwapchainDescription? scDesc, VulkanDeviceOptions vkOptions)
        {
            _thisGcHandle = GCHandle.Alloc(this);

            // ---------------------------------------------------------------
            // Create vkInstance
            // ---------------------------------------------------------------
            HashSet<ReadOnlyMemoryUtf8> availableInstanceLayers = new(EnumerateInstanceLayers());
            HashSet<ReadOnlyMemoryUtf8> availableInstanceExtensions = new(EnumerateInstanceExtensions());
            StackList<nint, FixedArray64<byte>> instanceExtensions = new();
            StackList<nint, FixedArray64<byte>> instanceLayers = new();
            _debugLog = options.Debug ? options.DebugLog : null;

            // Check that vkEnumerateInstanceVersion is available
            // This function is only available in Vulkan 1.1 and later
            PFN_vkEnumerateInstanceVersion vkn = vkGetGlobalProcAddr<PFN_vkEnumerateInstanceVersion>();
            if (vkn.IsNull)
            {
                throw new GraphicsException("Vulkan 1.1 is not supported on this system.");
            }

            var instanceCI = new VkInstanceCreateInfo();
            var applicationInfo = new VkApplicationInfo
            {
                apiVersion = VK_API_VERSION_1_1, // Requesting a minimum of Vulkan 1.1
                applicationVersion = new VkVersion(1, 0, 0),
                engineVersion = new VkVersion(1, 0, 0),
                pApplicationName = (byte*)(vkOptions.ApplicationName.IsNull ? DefaultAppName : vkOptions.ApplicationName),
                pEngineName = (byte*)(vkOptions.EngineName.IsNull ? DefaultAppName : vkOptions.EngineName)
            };

            instanceCI.pApplicationInfo = &applicationInfo;
            
            if (availableInstanceExtensions.Contains(VK_KHR_PORTABILITY_SUBSET_EXTENSION_NAME))
            {
                _surfaceExtensions.Add(VK_KHR_PORTABILITY_SUBSET_EXTENSION_NAME);
            }

            if (availableInstanceExtensions.Contains(VK_KHR_PORTABILITY_ENUMERATION_EXTENSION_NAME))
            {
                instanceExtensions.Add((nint)(byte*)VK_KHR_PORTABILITY_ENUMERATION_EXTENSION_NAME);
                instanceCI.flags |= (VkInstanceCreateFlagBits)VK_INSTANCE_CREATE_ENUMERATE_PORTABILITY_BIT_KHR;
            }

            if (availableInstanceExtensions.Contains(VK_KHR_SURFACE_EXTENSION_NAME))
            {
                _surfaceExtensions.Add(VK_KHR_SURFACE_EXTENSION_NAME);
            }
            else
            {
                throw new GraphicsException($"Required `{VK_KHR_SURFACE_EXTENSION_NAME}` extension not found.");
            }

            if (OperatingSystem.IsWindows())
            {
                if (availableInstanceExtensions.Contains(VK_KHR_WIN32_SURFACE_EXTENSION_NAME))
                {
                    _surfaceExtensions.Add(VK_KHR_WIN32_SURFACE_EXTENSION_NAME);
                }
            }
            else if (OperatingSystem.IsLinux() || OperatingSystem.IsAndroid())
            {
                if (availableInstanceExtensions.Contains(VK_KHR_ANDROID_SURFACE_EXTENSION_NAME))
                {
                    _surfaceExtensions.Add(VK_KHR_ANDROID_SURFACE_EXTENSION_NAME);
                }
                if (availableInstanceExtensions.Contains(VK_KHR_XLIB_SURFACE_EXTENSION_NAME))
                {
                    _surfaceExtensions.Add(VK_KHR_XLIB_SURFACE_EXTENSION_NAME);
                }
                if (availableInstanceExtensions.Contains(VK_KHR_WAYLAND_SURFACE_EXTENSION_NAME))
                {
                    _surfaceExtensions.Add(VK_KHR_WAYLAND_SURFACE_EXTENSION_NAME);
                }
            }
            else if (OperatingSystem.IsMacOS() || OperatingSystem.IsIOS())
            {
                if (availableInstanceExtensions.Contains(VK_EXT_METAL_SURFACE_EXTENSION_NAME))
                {
                    _surfaceExtensions.Add(VK_EXT_METAL_SURFACE_EXTENSION_NAME);
                }
            }

            foreach (var ext in _surfaceExtensions)
            {
                instanceExtensions.Add((nint)(byte*)ext);
            }

            ReadOnlyMemoryUtf8[] requestedInstanceExtensions = vkOptions.InstanceExtensions;
            foreach (ReadOnlyMemoryUtf8 requiredExt in requestedInstanceExtensions)
            {
                if (!availableInstanceExtensions.Contains(requiredExt))
                {
                    throw new GraphicsException($"The required instance extension was not available: {requiredExt}");
                }

                instanceExtensions.Add((nint)(byte*)requiredExt);
            }

            _debugUtilsExtensionAvailable = false;
            bool khronosValidationSupported = false;
            if (options.Debug)
            {
                if (availableInstanceExtensions.Contains(VK_EXT_DEBUG_UTILS_EXTENSION_NAME))
                {
                    _debugUtilsExtensionAvailable = true;
                    instanceExtensions.Add((nint)(byte*)VK_EXT_DEBUG_UTILS_EXTENSION_NAME);

                    // Check for validation layers
                    khronosValidationSupported = availableInstanceLayers.Contains(VK_LAYER_KHRONOS_VALIDATION_EXTENSION_NAME);
                    if (khronosValidationSupported)
                    {
                        instanceLayers.Add((nint)(byte*)VK_LAYER_KHRONOS_VALIDATION_EXTENSION_NAME);
                    }
                }
            }

            instanceCI.enabledExtensionCount = instanceExtensions.Count;
            instanceCI.ppEnabledExtensionNames = (byte**)instanceExtensions.Data;
            instanceCI.enabledLayerCount = instanceLayers.Count;
            if (instanceLayers.Count > 0)
            {
                instanceCI.ppEnabledLayerNames = (byte**)instanceLayers.Data;
            }

            VkDebugUtilsMessengerCreateInfoEXT debugUtilsCI = new VkDebugUtilsMessengerCreateInfoEXT
            {
                pfnUserCallback = (delegate* unmanaged[Stdcall]<VkDebugUtilsMessageSeverityFlagBitsEXT, VkDebugUtilsMessageTypeFlagsEXT, VkDebugUtilsMessengerCallbackDataEXT*, void*, VkBool32>)&DebugCallback,
                pUserData = (void*)GCHandle.ToIntPtr(_thisGcHandle)
            };

            if (_debugUtilsExtensionAvailable)
            {
                if ((options.DebugLogLevel & DebugLogLevel.Verbose) != 0)
                {
                    debugUtilsCI.messageSeverity |= VK_DEBUG_UTILS_MESSAGE_SEVERITY_VERBOSE_BIT_EXT;
                }

                if ((options.DebugLogLevel & DebugLogLevel.Info) != 0)
                {
                    debugUtilsCI.messageSeverity |= VK_DEBUG_UTILS_MESSAGE_SEVERITY_INFO_BIT_EXT;
                }

                if ((options.DebugLogLevel & DebugLogLevel.Warning) != 0)
                {
                    debugUtilsCI.messageSeverity |= VK_DEBUG_UTILS_MESSAGE_SEVERITY_WARNING_BIT_EXT;
                }

                if ((options.DebugLogLevel & DebugLogLevel.Error) != 0)
                {
                    debugUtilsCI.messageSeverity |= VK_DEBUG_UTILS_MESSAGE_SEVERITY_ERROR_BIT_EXT;
                }

                if ((options.DebugLogKind & DebugLogKind.General) != 0)
                {
                    debugUtilsCI.messageType |= VK_DEBUG_UTILS_MESSAGE_TYPE_GENERAL_BIT_EXT;
                }

                if ((options.DebugLogKind & DebugLogKind.Validation) != 0)
                {
                    debugUtilsCI.messageType |= VK_DEBUG_UTILS_MESSAGE_TYPE_VALIDATION_BIT_EXT;
                }

                if ((options.DebugLogKind & DebugLogKind.Performance) != 0)
                {
                    debugUtilsCI.messageType |= VK_DEBUG_UTILS_MESSAGE_TYPE_PERFORMANCE_BIT_EXT;
                }

                instanceCI.pNext = &debugUtilsCI;
            }

            // Create VkInstance
            vkCreateInstance(instanceCI, null, out _instance)
                .VkCheck("vkCreateInstance failed. It could be that Vulkan is not supported, installed for the minimum required version 1.1 is not supported");

            if (_debugUtilsExtensionAvailable)
            {
                _vkCreateDebugUtilsMessengerExt = vkGetInstanceProcAddr<PFN_vkCreateDebugUtilsMessengerEXT>(_instance);
                if (_vkCreateDebugUtilsMessengerExt.IsNull)
                {
                    throw new GraphicsException("Unable to load vkCreateDebugUtilsMessengerEXT");
                }
                _vkDestroyDebugUtilsMessengerExt = vkGetInstanceProcAddr<PFN_vkDestroyDebugUtilsMessengerEXT>(_instance);
                _vkSetDebugUtilsObjectNameEXT = vkGetInstanceProcAddr<PFN_vkSetDebugUtilsObjectNameEXT>(_instance);
                vkCmdBeginDebugUtilsLabelExt = vkGetInstanceProcAddr<PFN_vkCmdBeginDebugUtilsLabelEXT>(_instance);
                vkCmdEndDebugUtilsLabelExt = vkGetInstanceProcAddr<PFN_vkCmdEndDebugUtilsLabelEXT>(_instance);
                vkCmdInsertDebugUtilsLabelExt = vkGetInstanceProcAddr<PFN_vkCmdInsertDebugUtilsLabelEXT>(_instance);
                vkQueueBeginDebugUtilsLabelExt = vkGetInstanceProcAddr<PFN_vkQueueBeginDebugUtilsLabelEXT>(_instance);
                vkQueueEndDebugUtilsLabelExt = vkGetInstanceProcAddr<PFN_vkQueueEndDebugUtilsLabelEXT>(_instance);
                vkQueueInsertDebugUtilsLabelExt = vkGetInstanceProcAddr<PFN_vkQueueInsertDebugUtilsLabelEXT>(_instance);

                VkDebugUtilsMessengerEXT debugMessenger;
                _vkCreateDebugUtilsMessengerExt.Invoke(_instance, &debugUtilsCI, null, &debugMessenger)
                    .VkCheck("Unable to create debug messenger");
                _debugUtilsMessenger = debugMessenger;
            }


            if (HasSurfaceExtension(VK_EXT_METAL_SURFACE_EXTENSION_NAME))
            {
                _createMetalSurfaceEXT = vkGetInstanceProcAddr<PFN_vkCreateMetalSurfaceEXT>(_instance);
            }

            // ---------------------------------------------------------------
            // Create PhysicalDevice
            // ---------------------------------------------------------------

            // Get the function pointers for the extensions we need to use during device creation.
            vkGetPhysicalDeviceSurfaceSupportKHR = vkGetInstanceProcAddr<PFN_vkGetPhysicalDeviceSurfaceSupportKHR>(_instance);
            if (vkGetPhysicalDeviceSurfaceSupportKHR.IsNull)
            {
                throw new GraphicsException("Vulkan surface support function not found.");
            }

            uint deviceCount = 0;
            vkEnumeratePhysicalDevices(_instance, out deviceCount);
            if (deviceCount == 0)
            {
                throw new InvalidOperationException("No physical devices exist.");
            }

            VkPhysicalDevice[] physicalDevices = new VkPhysicalDevice[deviceCount];
            vkEnumeratePhysicalDevices(_instance, physicalDevices);
            // Just use the first one.
            _physicalDevice = physicalDevices[0];

            vkGetPhysicalDeviceProperties(_physicalDevice, out _physicalDeviceProperties);
            fixed (byte* utf8NamePtr = _physicalDeviceProperties.deviceName)
            {
                _deviceName = Encoding.UTF8.GetString(utf8NamePtr, (int)VK_MAX_PHYSICAL_DEVICE_NAME_SIZE).TrimEnd('\0');
            }

            _vendorName = "id:" + _physicalDeviceProperties.vendorID.ToString("x8");
            _apiVersion = GraphicsApiVersion.Unknown;
            _driverInfo = "version:" + _physicalDeviceProperties.driverVersion.ToString("x8");

            vkGetPhysicalDeviceFeatures(_physicalDevice, out _physicalDeviceFeatures);

            vkGetPhysicalDeviceMemoryProperties(_physicalDevice, out _physicalDeviceMemProperties);

            // ---------------------------------------------------------------
            // Get QueueFamilyIndices
            // ---------------------------------------------------------------
            uint queueFamilyCount = 0;
            vkGetPhysicalDeviceQueueFamilyProperties(_physicalDevice, out queueFamilyCount);
            VkQueueFamilyProperties[] qfp = new VkQueueFamilyProperties[queueFamilyCount];
            vkGetPhysicalDeviceQueueFamilyProperties(_physicalDevice, qfp);

            bool foundGraphics = false;


            VkSurfaceKHR surface = default;
            if (scDesc != null)
            {
                surface = VkSurfaceUtil.CreateSurface(this, _instance, scDesc.Value.Source);
            }
            bool foundPresent = surface == default;

            for (uint i = 0; i < qfp.Length; i++)
            {
                if ((qfp[i].queueFlags & VK_QUEUE_GRAPHICS_BIT) != 0)
                {
                    _graphicsQueueIndex = i;
                    foundGraphics = true;
                }

                if (!foundPresent)
                {
                    vkGetPhysicalDeviceSurfaceSupportKHR.Invoke(_physicalDevice, i, surface, out VkBool32 presentSupported);
                    if (presentSupported)
                    {
                        _presentQueueIndex = i;
                        foundPresent = true;
                    }
                }

                if (foundGraphics && foundPresent)
                {
                    break;
                }
            }

            // ---------------------------------------------------------------
            // Create LogicalDevice
            // ---------------------------------------------------------------

            var familyIndices = new HashSet<uint> { _graphicsQueueIndex, _presentQueueIndex };
            VkDeviceQueueCreateInfo* queueCreateInfos = stackalloc VkDeviceQueueCreateInfo[familyIndices.Count];
            uint queueCreateInfosCount = (uint)familyIndices.Count;

            int queueIndex = 0;
            foreach (uint index in familyIndices)
            {
                VkDeviceQueueCreateInfo queueCreateInfo = new VkDeviceQueueCreateInfo();
                queueCreateInfo.queueFamilyIndex = _graphicsQueueIndex;
                queueCreateInfo.queueCount = 1;
                float priority = 1f;
                queueCreateInfo.pQueuePriorities = &priority;
                queueCreateInfos[queueIndex] = queueCreateInfo;
                queueIndex += 1;
            }

            VkPhysicalDeviceFeatures deviceFeatures = _physicalDeviceFeatures;

            VkExtensionProperties[] props = GetDeviceExtensionProperties();

            var requiredDeviceExtensions = new HashSet<ReadOnlyMemoryUtf8>(vkOptions.DeviceExtensions);

            // Enforce the presence of these extensions
            requiredDeviceExtensions.Add(VK_KHR_DRIVER_PROPERTIES_EXTENSION_NAME);
            requiredDeviceExtensions.Add(VK_KHR_SWAPCHAIN_EXTENSION_NAME);

            var activeExtensions = new nint[props.Length];
            uint activeExtensionCount = 0;

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
                }
            }

            if (requiredDeviceExtensions.Count != 0)
            {
                string missingList = string.Join(", ", requiredDeviceExtensions);
                throw new GraphicsException($"The following Vulkan device extensions were not available: {missingList}");
            }


            StackList<nint> layerNames = new StackList<IntPtr>();
            if (khronosValidationSupported)
            {
                layerNames.Add((nint)(byte*)VK_LAYER_KHRONOS_VALIDATION_EXTENSION_NAME);
            }

            var deviceCreateInfo = new VkDeviceCreateInfo
            {
                queueCreateInfoCount = queueCreateInfosCount,
                pQueueCreateInfos = queueCreateInfos,
                pEnabledFeatures = &deviceFeatures,
                enabledLayerCount = layerNames.Count
            };

            if (layerNames.Count > 0)
            {
                deviceCreateInfo.ppEnabledLayerNames = (byte**)layerNames.Data;
            }

            // Create the logical device
            fixed (IntPtr* activeExtensionsPtr = activeExtensions)
            {
                deviceCreateInfo.enabledExtensionCount = activeExtensionCount;
                deviceCreateInfo.ppEnabledExtensionNames = (byte**)activeExtensionsPtr;

                vkCreateDevice(_physicalDevice, deviceCreateInfo, null, out _device)
                    .VkCheck("Unable to create device.");
            }

            vkGetDeviceQueue(_device, _graphicsQueueIndex, 0, out _graphicsQueue);

            // Get driver name and version
            VkPhysicalDeviceProperties2 deviceProps = new VkPhysicalDeviceProperties2();
            VkPhysicalDeviceDriverProperties driverProps = new VkPhysicalDeviceDriverProperties();

            deviceProps.pNext = &driverProps;
            vkGetPhysicalDeviceProperties2(_physicalDevice, &deviceProps);

            VkConformanceVersion conforming = driverProps.conformanceVersion;
            _apiVersion = new GraphicsApiVersion(conforming.major, conforming.minor, conforming.subminor, conforming.patch);
            _driverName = Encoding.UTF8.GetString(driverProps.driverName, (int)VK_MAX_DRIVER_NAME_SIZE).TrimEnd('\0');
            _driverInfo = Encoding.UTF8.GetString(driverProps.driverInfo, (int)VK_MAX_DRIVER_INFO_SIZE).TrimEnd('\0');

            // VK_KHR_surface
            vkGetPhysicalDeviceSurfaceCapabilitiesKHR = vkGetInstanceProcAddr<PFN_vkGetPhysicalDeviceSurfaceCapabilitiesKHR>(Instance);
            vkGetPhysicalDeviceSurfaceFormatsKHR = vkGetInstanceProcAddr<PFN_vkGetPhysicalDeviceSurfaceFormatsKHR>(Instance);
            vkGetPhysicalDeviceSurfacePresentModesKHR = vkGetInstanceProcAddr<PFN_vkGetPhysicalDeviceSurfacePresentModesKHR>(Instance);
            vkDestroySurfaceKHR = vkGetInstanceProcAddr<PFN_vkDestroySurfaceKHR>(Instance);

            // VK_KHR_swapchain
            vkAcquireNextImageKHR = vkGetDeviceProcAddr<PFN_vkAcquireNextImageKHR>(_device);
            vkCreateSwapchainKHR = vkGetDeviceProcAddr<PFN_vkCreateSwapchainKHR>(Device);
            vkDestroySwapchainKHR = vkGetDeviceProcAddr<PFN_vkDestroySwapchainKHR>(Device);
            _vkQueuePresentKHR = vkGetDeviceProcAddr<PFN_vkQueuePresentKHR>(Device);
            vkGetSwapchainImagesKHR = vkGetDeviceProcAddr<PFN_vkGetSwapchainImagesKHR>(Device);

            _memoryManager = new VkDeviceMemoryManager(
                _device,
                _physicalDevice,
                _physicalDeviceProperties.limits.bufferImageGranularity);

            Features = new GraphicsDeviceFeatures(
                computeShader: true,
                geometryShader: _physicalDeviceFeatures.geometryShader,
                tessellationShaders: _physicalDeviceFeatures.tessellationShader,
                multipleViewports: _physicalDeviceFeatures.multiViewport,
                samplerLodBias: true,
                drawBaseVertex: true,
                drawBaseInstance: true,
                drawIndirect: true,
                drawIndirectBaseInstance: _physicalDeviceFeatures.drawIndirectFirstInstance,
                fillModeWireframe: _physicalDeviceFeatures.fillModeNonSolid,
                samplerAnisotropy: _physicalDeviceFeatures.samplerAnisotropy,
                depthClipDisable: _physicalDeviceFeatures.depthClamp,
                texture1D: true,
                independentBlend: _physicalDeviceFeatures.independentBlend,
                structuredBuffer: true,
                subsetTextureView: true,
                commandListDebugMarkers: _debugUtilsExtensionAvailable,
                bufferRangeBinding: true,
                shaderFloat64: _physicalDeviceFeatures.shaderFloat64);

            ResourceFactory = new VkResourceFactory(this);

            if (scDesc != null)
            {
                SwapchainDescription desc = scDesc.Value;
                _mainSwapchain = new VkSwapchain(this, ref desc, surface);
            }

            _descriptorPoolManager = new VkDescriptorPoolManager(this);
            CreateGraphicsCommandPool(out _graphicsCommandPool);
            for (int i = 0; i < SharedCommandPoolCount; i++)
            {
                _sharedGraphicsCommandPools.Push(new SharedCommandPool(this, true));
            }

            _vulkanInfo = new BackendInfoVulkan(this);

            PointSampler = ResourceFactory.CreateSampler(SamplerDescription.Point);
            LinearSampler = ResourceFactory.CreateSampler(SamplerDescription.Linear);
            if (Features.SamplerAnisotropy)
            {
                AnisotropicSampler4x = ResourceFactory.CreateSampler(SamplerDescription.Aniso4x);
            }
        }

        public override ResourceFactory ResourceFactory { get; }


        public void DebugLog(DebugLogLevel level, DebugLogKind kind, string message)
        {
            _debugLog?.Invoke(level, kind, message);
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
            VkPipelineStageFlags waitDstStageMask = VkPipelineStageFlagBits.VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT;
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

            lock (_graphicsQueueLock)
            {
                vkQueueSubmit(_graphicsQueue, 1, &si, vkFence)
                    .VkCheck("Error while submitting command buffer");

                if (fence != null)
                {
                    vkQueueSubmit(_graphicsQueue, 0, null, submissionFence)
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
                    if (vkGetFenceStatus(_device, fsi.Fence) == VK_SUCCESS)
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
            vkResetFences(_device, 1, &fence)
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
                vkCreateFence(_device, fenceCI, null, out XenoAtom.Interop.vulkan.VkFence newFence)
                    .VkCheck("Unable to create fence");
                return newFence;
            }
        }

        private protected override void SwapBuffersCore(Swapchain swapchain)
        {
            VkSwapchain vkSC = Util.AssertSubtype<Swapchain, VkSwapchain>(swapchain);
            VkSwapchainKHR deviceSwapchain = vkSC.DeviceSwapchain;
            var presentInfo = new VkPresentInfoKHR
            {
                swapchainCount = 1,
                pSwapchains = &deviceSwapchain
            };
            uint imageIndex = vkSC.ImageIndex;
            presentInfo.pImageIndices = &imageIndex;

            object presentLock = vkSC.PresentQueueIndex == _graphicsQueueIndex ? _graphicsQueueLock : vkSC;
            lock (presentLock)
            {
                _vkQueuePresentKHR.Invoke(vkSC.PresentQueue, presentInfo);
                if (vkSC.AcquireNextImage(_device, default, vkSC.ImageAvailableFence))
                {
                    XenoAtom.Interop.vulkan.VkFence fence = vkSC.ImageAvailableFence;
                    vkWaitForFences(_device, 1, &fence, true, ulong.MaxValue);
                    vkResetFences(_device, 1, &fence);
                }
            }
        }

        internal override void SetResourceName(GraphicsObject resource, string? name)
        {
            if (!_debugUtilsExtensionAvailable)
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
            _vkSetDebugUtilsObjectNameEXT.Invoke(_device, &nameInfo)
                .VkCheck("Unable to set debug marker object name");
        }

        public bool HasSurfaceExtension(ReadOnlyMemoryUtf8 extension)
        {
            return _surfaceExtensions.Contains(extension);
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static VkBool32 DebugCallback(
            VkDebugUtilsMessageSeverityFlagBitsEXT messageSeverity,
            VkDebugUtilsMessageTypeFlagsEXT messageTypes,
            VkDebugUtilsMessengerCallbackDataEXT* pCallbackData,
            void* pUserData)
        {
            string message = Util.GetString(pCallbackData->pMessage);
#if DEBUG
            if (Debugger.IsAttached)
            {
                Debugger.Break();
            }
#endif

            var gcHandle = GCHandle.FromIntPtr((IntPtr)pUserData);
            var device = (VkGraphicsDevice)gcHandle.Target!;
            if (device._debugLog != null)
            {
                DebugLogLevel debugLogLevel = DebugLogLevel.None;

                if ((messageSeverity & VK_DEBUG_UTILS_MESSAGE_SEVERITY_VERBOSE_BIT_EXT) != 0)
                {
                    debugLogLevel |= DebugLogLevel.Verbose;
                }

                if ((messageSeverity & VK_DEBUG_UTILS_MESSAGE_SEVERITY_INFO_BIT_EXT) != 0)
                {
                    debugLogLevel |= DebugLogLevel.Info;
                }

                if ((messageSeverity & VK_DEBUG_UTILS_MESSAGE_SEVERITY_WARNING_BIT_EXT) != 0)
                {
                    debugLogLevel |= DebugLogLevel.Warning;
                }

                if ((messageSeverity & VK_DEBUG_UTILS_MESSAGE_SEVERITY_ERROR_BIT_EXT) != 0)
                {
                    debugLogLevel |= DebugLogLevel.Error;
                }

                DebugLogKind debugLogKind = DebugLogKind.None;

                if ((messageTypes & VK_DEBUG_UTILS_MESSAGE_TYPE_GENERAL_BIT_EXT) != 0)
                {
                    debugLogKind |= DebugLogKind.General;
                }

                if ((messageTypes & VK_DEBUG_UTILS_MESSAGE_TYPE_VALIDATION_BIT_EXT) != 0)
                {
                    debugLogKind |= DebugLogKind.Validation;
                }

                if ((messageTypes & VK_DEBUG_UTILS_MESSAGE_TYPE_PERFORMANCE_BIT_EXT) != 0)
                {
                    debugLogKind |= DebugLogKind.Performance;
                }

                string fullMessage = message;
                if (pCallbackData->objectCount > 0)
                {
                    // Use only the first object
                    fullMessage = $"({pCallbackData->pObjects[0].objectType}) {message}";
                }
                
                device._debugLog(debugLogLevel, debugLogKind, fullMessage);
            }
            
            return 0;
        }
        
        public VkExtensionProperties[] GetDeviceExtensionProperties()
        {
            vkEnumerateDeviceExtensionProperties(_physicalDevice, default, out var propertyCount)
                .VkCheck("Unable to enumerate device extensions");
            VkExtensionProperties[] props = new VkExtensionProperties[(int)propertyCount];
            vkEnumerateDeviceExtensionProperties(_physicalDevice, default, props)
                .VkCheck("Unable to enumerate device extensions");
            return props;
        }

        private void CreateGraphicsCommandPool(out VkCommandPool commandPool)
        {
            VkCommandPoolCreateInfo commandPoolCI = new()
            {
                flags = VK_COMMAND_POOL_CREATE_RESET_COMMAND_BUFFER_BIT,
                queueFamilyIndex = _graphicsQueueIndex
            };
            vkCreateCommandPool(_device, commandPoolCI, null, out commandPool)
                .VkCheck("Unable to create Graphics Command Pool");
        }

        protected override MappedResource MapCore(MappableResource resource, MapMode mode, uint subresource)
        {
            VkMemoryBlock memoryBlock;
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
                VkTexture texture = Util.AssertSubtype<MappableResource, VkTexture>(resource);
                VkSubresourceLayout layout = texture.GetSubresourceLayout(subresource);
                memoryBlock = texture.Memory;
                sizeInBytes = (uint)layout.size;
                offset = (uint)layout.offset;
                rowPitch = (uint)layout.rowPitch;
                depthPitch = (uint)layout.depthPitch;
            }

            if (memoryBlock.DeviceMemory.Value.Handle != 0)
            {
                if (memoryBlock.IsPersistentMapped)
                {
                    mappedPtr = (IntPtr)memoryBlock.BlockMappedPointer;
                }
                else
                {
                    mappedPtr = _memoryManager.Map(memoryBlock);
                }
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

        protected override void UnmapCore(MappableResource resource, uint subresource)
        {
            VkMemoryBlock memoryBlock;
            if (resource is VkBuffer buffer)
            {
                memoryBlock = buffer.Memory;
            }
            else
            {
                VkTexture tex = Util.AssertSubtype<MappableResource, VkTexture>(resource);
                memoryBlock = tex.Memory;
            }

            if (memoryBlock.DeviceMemory.Value.Handle != 0 && !memoryBlock.IsPersistentMapped)
            {
                vkUnmapMemory(_device, memoryBlock.DeviceMemory);
            }
        }

        protected override void PlatformDispose()
        {
            Debug.Assert(_submittedFences.Count == 0);
            foreach (XenoAtom.Interop.vulkan.VkFence fence in _availableSubmissionFences)
            {
                vkDestroyFence(_device, fence, null);
            }

            _mainSwapchain?.Dispose();

            _descriptorPoolManager.DestroyAll();
            vkDestroyCommandPool(_device, _graphicsCommandPool, null);

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

            vkDeviceWaitIdle(_device)
                .VkCheck("Unable to wait for idle on device");
            vkDestroyDevice(_device, null);

            if (_debugUtilsMessenger != default && !_vkDestroyDebugUtilsMessengerExt.IsNull)
            {
                _vkDestroyDebugUtilsMessengerExt.Invoke(_instance, _debugUtilsMessenger, null);
            }

            vkDestroyInstance(_instance, null);

            var thisGcHandle = _thisGcHandle;
            thisGcHandle.Free();
        }

        private protected override void WaitForIdleCore()
        {
            lock (_graphicsQueueLock)
            {
                vkQueueWaitIdle(_graphicsQueue);
            }

            CheckSubmittedFences();
        }

        public override TextureSampleCount GetSampleCountLimit(PixelFormat format, bool depthFormat)
        {
            VkImageUsageFlagBits usageFlags = VK_IMAGE_USAGE_SAMPLED_BIT;
            usageFlags |= depthFormat ? VK_IMAGE_USAGE_DEPTH_STENCIL_ATTACHMENT_BIT : VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT;

            vkGetPhysicalDeviceImageFormatProperties(
                _physicalDevice,
                VkFormats.VdToVkPixelFormat(format),
                VK_IMAGE_TYPE_2D,
                VK_IMAGE_TILING_OPTIMAL,
                usageFlags,
                (VkImageCreateFlagBits)0,
                out VkImageFormatProperties formatProperties);

            VkSampleCountFlagBits vkSampleCounts = formatProperties.sampleCounts;
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
            TextureType type,
            TextureUsage usage,
            out PixelFormatProperties properties)
        {
            VkFormat vkFormat = VkFormats.VdToVkPixelFormat(format, (usage & TextureUsage.DepthStencil) != 0);
            VkImageType vkType = VkFormats.VdToVkTextureType(type);
            VkImageTiling tiling = usage == TextureUsage.Staging ? VK_IMAGE_TILING_LINEAR : VK_IMAGE_TILING_OPTIMAL;
            VkImageUsageFlags vkUsage = VkFormats.VdToVkTextureUsage(usage);

            VkResult result = vkGetPhysicalDeviceImageFormatProperties(
                _physicalDevice,
                vkFormat,
                vkType,
                tiling,
                vkUsage,
                (VkImageCreateFlagBits)0,
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
               vkProps.sampleCounts.Value);
            return true;
        }

        internal VkFilter GetFormatFilter(VkFormat format)
        {
            if (!_filters.TryGetValue(format, out VkFilter filter))
            {
                vkGetPhysicalDeviceFormatProperties(_physicalDevice, format, out VkFormatProperties vkFormatProps);
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
                mappedPtr = (IntPtr)vkBuffer.Memory.BlockMappedPointer;
                destPtr = (byte*)mappedPtr + bufferOffsetInBytes;
            }
            else
            {
                copySrcVkBuffer = GetFreeStagingBuffer(sizeInBytes);
                mappedPtr = (IntPtr)copySrcVkBuffer.Memory.BlockMappedPointer;
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

        private IntPtr MapBuffer(VkBuffer buffer, uint numBytes)
        {
            if (buffer.Memory.IsPersistentMapped)
            {
                return (IntPtr)buffer.Memory.BlockMappedPointer;
            }
            else
            {
                void* mappedPtr;
                vkMapMemory(Device, buffer.Memory.DeviceMemory, buffer.Memory.Offset, numBytes, default, &mappedPtr)
                    .VkCheck("Unable to map buffer memory");
                return (IntPtr)mappedPtr;
            }
        }

        private void UnmapBuffer(VkBuffer buffer)
        {
            if (!buffer.Memory.IsPersistentMapped)
            {
                vkUnmapMemory(Device, buffer.Memory.DeviceMemory);
            }
        }

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
                VkMemoryBlock memBlock = vkTex.Memory;
                uint subresource = texture.CalculateSubresource(mipLevel, arrayLayer);
                VkSubresourceLayout layout = vkTex.GetSubresourceLayout(subresource);
                byte* imageBasePtr = (byte*)memBlock.BlockMappedPointer + layout.offset;

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
            VkTexture newTex = (VkTexture)ResourceFactory.CreateTexture(TextureDescription.Texture3D(
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
            VkBuffer newBuffer = (VkBuffer)ResourceFactory.CreateBuffer(
                new BufferDescription(newBufferSize, BufferUsage.Staging));
            return newBuffer;
        }

        public override void ResetFence(Fence fence)
        {
            XenoAtom.Interop.vulkan.VkFence vkFence = Util.AssertSubtype<Fence, VkFence>(fence).DeviceFence;
            vkResetFences(_device, 1, &vkFence);
        }

        public override bool WaitForFence(Fence fence, ulong nanosecondTimeout)
        {
            XenoAtom.Interop.vulkan.VkFence vkFence = Util.AssertSubtype<Fence, VkFence>(fence).DeviceFence;
            VkResult result = vkWaitForFences(_device, 1, &vkFence, true, nanosecondTimeout);
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

            VkResult result = vkWaitForFences(_device, (uint)fenceCount, fencesPtr, waitAll, nanosecondTimeout);
            return result == VK_SUCCESS;
        }

        internal static bool IsSupported()
        {
            return s_isSupported.Value;
        }

        private static bool CheckIsSupported()
        {
            if (!IsVulkanLoaded())
            {
                return false;
            }

            VkInstanceCreateInfo instanceCI = new VkInstanceCreateInfo();
            VkApplicationInfo applicationInfo = new VkApplicationInfo();
            applicationInfo.apiVersion = new VkVersion(1, 1, 0);
            applicationInfo.applicationVersion = new VkVersion(1, 0, 0);
            applicationInfo.engineVersion = new VkVersion(1, 0, 0);
            applicationInfo.pApplicationName = (byte*)DefaultAppName;
            applicationInfo.pEngineName = (byte*)DefaultAppName;

            instanceCI.pApplicationInfo = &applicationInfo;

            VkResult result = vkCreateInstance(instanceCI, null, out VkInstance testInstance);
            if (result != VK_SUCCESS)
            {
                return false;
            }

            uint physicalDeviceCount;
            result = vkEnumeratePhysicalDevices(testInstance, out physicalDeviceCount);
            if (result != VK_SUCCESS || physicalDeviceCount == 0)
            {
                vkDestroyInstance(testInstance, null);
                return false;
            }

            vkDestroyInstance(testInstance, null);

            HashSet<ReadOnlyMemoryUtf8> instanceExtensions = new(GetInstanceExtensions());
            if (!instanceExtensions.Contains(VK_KHR_SURFACE_EXTENSION_NAME))
            {
                return false;
            }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return instanceExtensions.Contains(VK_KHR_WIN32_SURFACE_EXTENSION_NAME);
            }
#if NET5_0_OR_GREATER
            else if (OperatingSystem.IsAndroid())
            {
                return instanceExtensions.Contains(VK_KHR_ANDROID_SURFACE_EXTENSION_NAME);
            }
#endif
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                if (RuntimeInformation.OSDescription.Contains("Unix")) // Android
                {
                    return instanceExtensions.Contains(VK_KHR_ANDROID_SURFACE_EXTENSION_NAME);
                }
                else
                {
                    return instanceExtensions.Contains(VK_KHR_XLIB_SURFACE_EXTENSION_NAME);
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return instanceExtensions.Contains(VK_EXT_METAL_SURFACE_EXTENSION_NAME);
            }

            return false;
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
            => (uint)_physicalDeviceProperties.limits.minUniformBufferOffsetAlignment;

        internal override uint GetStructuredBufferMinOffsetAlignmentCore()
            => (uint)_physicalDeviceProperties.limits.minStorageBufferOffsetAlignment;

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
                commandPoolCI.queueFamilyIndex = _gd.GraphicsQueueIndex;
                vkCreateCommandPool(_gd.Device, commandPoolCI, null, out _pool)
                    .VkCheck("Unable to create shared command pool");

                VkCommandBufferAllocateInfo allocateInfo = new VkCommandBufferAllocateInfo();
                allocateInfo.commandBufferCount = 1;
                allocateInfo.level = VK_COMMAND_BUFFER_LEVEL_PRIMARY;
                allocateInfo.commandPool = _pool;

                VkCommandBuffer cb;
                vkAllocateCommandBuffers(_gd.Device, allocateInfo, &cb)
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
                vkDestroyCommandPool(_gd.Device, _pool, null);
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
    }
}

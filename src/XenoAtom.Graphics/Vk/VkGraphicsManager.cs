// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using XenoAtom.Interop;
using static XenoAtom.Interop.vulkan;

namespace XenoAtom.Graphics.Vk;

internal sealed unsafe class VkGraphicsManager : GraphicsManager
{
    private static ReadOnlyMemoryUtf8 DefaultAppName => "XenoAtom.Graphics-VkGraphicsDevice"u8;
    //private static readonly Lazy<bool> s_isSupported = new Lazy<bool>(CheckIsSupported, isThreadSafe: true);

    private readonly GCHandle _thisGcHandle;
    private readonly DebugLogDelegate? _debugLog;
    private readonly VkInstance _instance;

    public VkInstance Instance => _instance;

    public readonly PFN_vkCreateMetalSurfaceEXT vkCreateMetalSurfaceEXT;

    public readonly PFN_vkGetPhysicalDeviceSurfaceSupportKHR vkGetPhysicalDeviceSurfaceSupportKHR;

    // Function pointers for VK_EXT_debug_utils
    public readonly bool IsDebugActivated;

    private readonly PFN_vkCreateDebugUtilsMessengerEXT _vkCreateDebugUtilsMessengerExt;
    private readonly PFN_vkDestroyDebugUtilsMessengerEXT _vkDestroyDebugUtilsMessengerExt;

    public readonly PFN_vkSetDebugUtilsObjectNameEXT _vkSetDebugUtilsObjectNameEXT;

    public readonly PFN_vkCmdBeginDebugUtilsLabelEXT vkCmdBeginDebugUtilsLabelExt;
    public readonly PFN_vkCmdEndDebugUtilsLabelEXT vkCmdEndDebugUtilsLabelExt;
    public readonly PFN_vkCmdInsertDebugUtilsLabelEXT vkCmdInsertDebugUtilsLabelExt;

    public readonly PFN_vkQueueBeginDebugUtilsLabelEXT vkQueueBeginDebugUtilsLabelExt;
    public readonly PFN_vkQueueEndDebugUtilsLabelEXT vkQueueEndDebugUtilsLabelExt;
    public readonly PFN_vkQueueInsertDebugUtilsLabelEXT vkQueueInsertDebugUtilsLabelExt;
    private readonly VkDebugUtilsMessengerEXT _debugUtilsMessenger;

    private readonly List<ReadOnlyMemoryUtf8> _surfaceExtensions = new List<ReadOnlyMemoryUtf8>();

    private VkGraphicsAdapter[] _vkGraphicsAdapters;

    public readonly bool ValidationSupported;

    public override ReadOnlySpan<GraphicsAdapter> Adapters => _vkGraphicsAdapters;

    public VkGraphicsManager(in GraphicsManagerOptions options)
    {
        _thisGcHandle = GCHandle.Alloc(this);

        // ---------------------------------------------------------------
        // Create vkInstance
        // ---------------------------------------------------------------
        HashSet<ReadOnlyMemoryUtf8> availableInstanceLayers = new(VulkanUtil.EnumerateInstanceLayers());
        HashSet<ReadOnlyMemoryUtf8> availableInstanceExtensions = new(VulkanUtil.EnumerateInstanceExtensions());
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

        ref readonly var vkOptions = ref options.VulkanOptions;

        var instanceCI = new VkInstanceCreateInfo();
        var applicationInfo = new VkApplicationInfo
        {
            apiVersion = VK_API_VERSION_1_2, // Requesting a minimum of Vulkan 1.2
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
            instanceCI.flags |= (VkInstanceCreateFlags)VK_INSTANCE_CREATE_ENUMERATE_PORTABILITY_BIT_KHR;
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

        IsDebugActivated = false;

        ValidationSupported = false;
        if (options.Debug)
        {
            if (availableInstanceExtensions.Contains(VK_EXT_DEBUG_UTILS_EXTENSION_NAME))
            {
                IsDebugActivated = true;
                instanceExtensions.Add((nint)(byte*)VK_EXT_DEBUG_UTILS_EXTENSION_NAME);

                // Check for validation layers
                ValidationSupported = availableInstanceLayers.Contains(VK_LAYER_KHRONOS_VALIDATION_EXTENSION_NAME);
                if (ValidationSupported)
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
            pfnUserCallback = (delegate* unmanaged[Stdcall]<VkDebugUtilsMessageSeverityFlagsEXT, VkDebugUtilsMessageTypeFlagsEXT, VkDebugUtilsMessengerCallbackDataEXT*, void*, VkBool32>)&DebugCallback,
            pUserData = (void*)GCHandle.ToIntPtr(_thisGcHandle)
        };

        if (IsDebugActivated)
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
            .VkCheck("vkCreateInstance failed. It could be that Vulkan is not supported, installed for the minimum required version 1.2 is not supported");

        if (IsDebugActivated)
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
            vkCreateMetalSurfaceEXT = vkGetInstanceProcAddr<PFN_vkCreateMetalSurfaceEXT>(_instance);
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

        vkEnumeratePhysicalDevices(_instance, out var deviceCount);

        VkPhysicalDevice[] physicalDevices = new VkPhysicalDevice[deviceCount];
        vkEnumeratePhysicalDevices(_instance, physicalDevices);

        _vkGraphicsAdapters = new VkGraphicsAdapter[deviceCount];

        for (int i = 0; i < deviceCount; i++)
        {
            _vkGraphicsAdapters[i] = new VkGraphicsAdapter(this, physicalDevices[i]);
        }
    }

    public void DebugLog(DebugLogLevel level, DebugLogKind kind, string message)
    {
        _debugLog?.Invoke(level, kind, message);
    }

    public bool HasSurfaceExtension(ReadOnlyMemoryUtf8 extension)
    {
        return _surfaceExtensions.Contains(extension);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static VkBool32 DebugCallback(
        VkDebugUtilsMessageSeverityFlagsEXT messageSeverity,
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
        var device = (VkGraphicsManager)gcHandle.Target!;
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
    
    internal override void Destroy()
    {
        foreach (var adapter in _vkGraphicsAdapters)
        {
            adapter.Dispose();
        }
        _vkGraphicsAdapters = [];

        if (_debugUtilsMessenger != default && !_vkDestroyDebugUtilsMessengerExt.IsNull)
        {
            _vkDestroyDebugUtilsMessengerExt.Invoke(_instance, _debugUtilsMessenger, null);
        }

        vkDestroyInstance(_instance, null);

        var thisGcHandle = _thisGcHandle;
        thisGcHandle.Free();
    }
}
// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using static XenoAtom.Interop.vulkan;

namespace XenoAtom.Graphics.Vk;

internal sealed unsafe class VkGraphicsAdapter : GraphicsAdapter
{
    public readonly VkPhysicalDevice PhysicalDevice;
    public readonly VkPhysicalDeviceProperties PhysicalDeviceProperties;
    public readonly VkPhysicalDeviceVulkan11Properties PhysicalDeviceVulkan11Properties;
    public readonly VkPhysicalDeviceVulkan12Properties PhysicalDeviceVulkan12Properties;
    public readonly VkPhysicalDeviceVulkan13Properties PhysicalDeviceVulkan13Properties;

    public readonly VkPhysicalDeviceFeatures PhysicalDeviceFeatures;
    public readonly VkPhysicalDeviceDriverProperties PhysicalDeviceDriverPropertie;
    public readonly VkPhysicalDeviceMemoryProperties PhysicalDeviceMemProperties;
    private readonly GraphicsVersion _apiVersion;
    private readonly GraphicsVersion _driverVersion;

    private readonly Guid _deviceUUID;
    private readonly Guid _driverUUID;

    public VkGraphicsAdapter(VkGraphicsManager manager, VkPhysicalDevice physicalDevice) : base(manager)
    {
        PhysicalDevice = physicalDevice;

        // Get the properties of the physical device
        VkPhysicalDeviceProperties2 deviceProps = new VkPhysicalDeviceProperties2();
        VkPhysicalDeviceVulkan11Properties vulkan11Props = new VkPhysicalDeviceVulkan11Properties();
        VkPhysicalDeviceVulkan12Properties vulkan12Props = new VkPhysicalDeviceVulkan12Properties();
        VkPhysicalDeviceVulkan13Properties vulkan13Props = new VkPhysicalDeviceVulkan13Properties();
        VkPhysicalDeviceDriverProperties driverProps = new VkPhysicalDeviceDriverProperties();
        VkPhysicalDeviceIDProperties idProps = new VkPhysicalDeviceIDProperties();

        deviceProps.pNext = &idProps;
        idProps.pNext = &driverProps;
        driverProps.pNext = &vulkan13Props;
        vulkan13Props.pNext = &vulkan12Props;
        vulkan12Props.pNext = &vulkan11Props;
        
        vkGetPhysicalDeviceProperties2(physicalDevice, &deviceProps);
        PhysicalDeviceProperties = deviceProps.properties;
        PhysicalDeviceDriverPropertie = driverProps;
        PhysicalDeviceVulkan11Properties = vulkan11Props;
        PhysicalDeviceVulkan12Properties = vulkan12Props;
        PhysicalDeviceVulkan13Properties = vulkan13Props;
        
        DeviceName = Encoding.UTF8.GetString(deviceProps.properties.deviceName, (int)VK_MAX_PHYSICAL_DEVICE_NAME_SIZE).TrimEnd('\0');
        DriverName = Encoding.UTF8.GetString(driverProps.driverName, (int)VK_MAX_DRIVER_NAME_SIZE).TrimEnd('\0');
        DriverInfo = Encoding.UTF8.GetString(driverProps.driverInfo, (int)VK_MAX_DRIVER_INFO_SIZE).TrimEnd('\0');
        VendorName = "id:" + deviceProps.properties.vendorID.ToString("x8");
        VendorId = deviceProps.properties.vendorID;
        DeviceId = deviceProps.properties.deviceID;

        VkConformanceVersion conforming = driverProps.conformanceVersion;
        _apiVersion = new(conforming.major, conforming.minor, conforming.subminor, conforming.patch);

        var vkDriverVersion = new VkVersion(deviceProps.properties.driverVersion);
        _driverVersion = new(vkDriverVersion.Major, vkDriverVersion.Minor, vkDriverVersion.Patch, 0);

        vkGetPhysicalDeviceFeatures(PhysicalDevice, out PhysicalDeviceFeatures);
        vkGetPhysicalDeviceMemoryProperties(PhysicalDevice, out PhysicalDeviceMemProperties);

        Kind = deviceProps.properties.deviceType switch
        {
            VkPhysicalDeviceType.VK_PHYSICAL_DEVICE_TYPE_DISCRETE_GPU => GraphicsAdapterKind.DiscreteGpu,
            VkPhysicalDeviceType.VK_PHYSICAL_DEVICE_TYPE_INTEGRATED_GPU => GraphicsAdapterKind.IntegratedGpu,
            VkPhysicalDeviceType.VK_PHYSICAL_DEVICE_TYPE_VIRTUAL_GPU => GraphicsAdapterKind.VirtualGpu,
            VkPhysicalDeviceType.VK_PHYSICAL_DEVICE_TYPE_CPU => GraphicsAdapterKind.Cpu,
            _ => GraphicsAdapterKind.Other
        };

        _deviceUUID = new Guid(new ReadOnlySpan<byte>(idProps.deviceUUID, 16), true); // TODO: is it correct to use bigEndian = true?
        _driverUUID = new Guid(new ReadOnlySpan<byte>(idProps.driverUUID, 16), true);
        DeviceLUID = *(ulong*)idProps.deviceLUID;
    }

    public new VkGraphicsManager Manager => Unsafe.As<GraphicsManager, VkGraphicsManager>(ref Unsafe.AsRef(in base.Manager));

    public override string DeviceName { get; }

    public override string DriverName { get; }

    public override uint VendorId { get; }

    public override uint DeviceId { get; }

    public override string DriverInfo { get; }

    public override string VendorName { get; }

    public override ref readonly GraphicsVersion ApiVersion => ref _apiVersion;

    public override ref readonly GraphicsVersion DriverVersion => ref _driverVersion;

    public override ref readonly Guid DeviceUUID => ref _deviceUUID;

    public override ref readonly Guid DriverUUID => ref _driverUUID;

    public override ulong DeviceLUID { get; }

    public override GraphicsAdapterKind Kind { get; }

    public override GraphicsDevice CreateDevice(in GraphicsDeviceOptions options) => new VkGraphicsDevice(this, options);
    
    internal override void Destroy()
    {
    }
}
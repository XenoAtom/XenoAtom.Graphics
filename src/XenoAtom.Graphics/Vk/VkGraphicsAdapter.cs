// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using static XenoAtom.Interop.vulkan;

namespace XenoAtom.Graphics.Vk;

internal sealed unsafe class VkGraphicsAdapter : GraphicsAdapter
{
    public readonly VkPhysicalDevice PhysicalDevice;
    public readonly VkPhysicalDeviceProperties PhysicalDeviceProperties;
    public readonly VkPhysicalDeviceFeatures PhysicalDeviceFeatures;
    public readonly VkPhysicalDeviceMemoryProperties PhysicalDeviceMemProperties;

    public VkGraphicsAdapter(VkGraphicsManager manager, VkPhysicalDevice physicalDevice) : base(manager)
    {
        PhysicalDevice = physicalDevice;

        vkGetPhysicalDeviceProperties(PhysicalDevice, out PhysicalDeviceProperties);
        fixed (byte* utf8NamePtr = PhysicalDeviceProperties.deviceName)
        {
            DeviceName = Encoding.UTF8.GetString(utf8NamePtr, (int)VK_MAX_PHYSICAL_DEVICE_NAME_SIZE).TrimEnd('\0');
        }

        // Get driver name and version
        VkPhysicalDeviceProperties2 deviceProps = new VkPhysicalDeviceProperties2();
        VkPhysicalDeviceDriverProperties driverProps = new VkPhysicalDeviceDriverProperties();
        deviceProps.pNext = &driverProps;
        vkGetPhysicalDeviceProperties2(physicalDevice, &deviceProps);

        VkConformanceVersion conforming = driverProps.conformanceVersion;
        ApiVersion = new(conforming.major, conforming.minor, conforming.subminor, conforming.patch);
        DriverName = Encoding.UTF8.GetString(driverProps.driverName, (int)VK_MAX_DRIVER_NAME_SIZE).TrimEnd('\0');
        DriverInfo = Encoding.UTF8.GetString(driverProps.driverInfo, (int)VK_MAX_DRIVER_INFO_SIZE).TrimEnd('\0');
        VendorName = "id:" + PhysicalDeviceProperties.vendorID.ToString("x8");

        var vkDriverVersion = new VkVersion(PhysicalDeviceProperties.driverVersion);
        DriverVersion = new(vkDriverVersion.Major, vkDriverVersion.Minor, vkDriverVersion.Patch, 0);

        vkGetPhysicalDeviceFeatures(PhysicalDevice, out PhysicalDeviceFeatures);
        vkGetPhysicalDeviceMemoryProperties(PhysicalDevice, out PhysicalDeviceMemProperties);
    }

    public new VkGraphicsManager Manager => Unsafe.As<GraphicsManager, VkGraphicsManager>(ref Unsafe.AsRef(in base.Manager));

    public override string DeviceName { get; }

    public override string DriverName { get; }

    public override string DriverInfo { get; }

    public override string VendorName { get; }

    public override GraphicsVersion ApiVersion { get; }

    public override GraphicsVersion DriverVersion { get; }

    public override GraphicsDevice CreateDevice(in GraphicsDeviceOptions options) => new VkGraphicsDevice(this, options);
    
    internal override void Destroy()
    {
    }
}
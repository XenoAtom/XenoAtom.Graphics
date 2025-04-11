#if !EXCLUDE_VULKAN_BACKEND
using System;
using System.Collections.ObjectModel;
using XenoAtom.Graphics.Vk;
using XenoAtom.Interop;
using static XenoAtom.Interop.vulkan;

namespace XenoAtom.Graphics
{
    /// <summary>
    /// Exposes device specific backend info.
    /// </summary>
    public abstract class GraphicsDeviceBackendInfo
    {
        internal GraphicsDeviceBackendInfo()
        {
        }
    }

    /// <summary>
    /// Exposes Vulkan-specific functionality,
    /// useful for interoperating with native components which interface directly with Vulkan.
    /// Can only be used on <see cref="GraphicsBackend.Vulkan"/>.
    /// </summary>
    public class GraphicsDeviceBackendInfoVulkan : GraphicsDeviceBackendInfo
    {
        private readonly VkGraphicsDevice _gd;
        private readonly Lazy<ReadOnlyCollection<ReadOnlyMemoryUtf8>> _instanceLayers;
        private readonly ReadOnlyCollection<ReadOnlyMemoryUtf8> _instanceExtensions;
        private readonly Lazy<ReadOnlyCollection<ExtensionProperties>> _deviceExtensions;

        internal GraphicsDeviceBackendInfoVulkan(VkGraphicsDevice gd)
        {
            _gd = gd;
            _instanceLayers = new Lazy<ReadOnlyCollection<ReadOnlyMemoryUtf8>>(() => new ReadOnlyCollection<ReadOnlyMemoryUtf8>(VulkanUtil.EnumerateInstanceLayers()));
            _instanceExtensions = new ReadOnlyCollection<ReadOnlyMemoryUtf8>(VulkanUtil.EnumerateInstanceExtensions());
            _deviceExtensions = new Lazy<ReadOnlyCollection<ExtensionProperties>>(EnumerateDeviceExtensions);
            GetInstanceProcAddr = (nint)gd._vkGetInstanceProcAddr.Pointer;
            GetDeviceProcAddr = (nint)gd._vkGetDeviceProcAddr.Pointer;
            ApiVersion = VkGraphicsManager.ApiVersion;
        }

        /// <summary>
        /// Gets the underlying VkInstance used by the GraphicsDevice.
        /// </summary>
        public IntPtr Instance => _gd.VkInstance.Value.Handle;

        /// <summary>
        /// Gets the underlying VkDevice used by the GraphicsDevice.
        /// </summary>
        public IntPtr Device => _gd.VkDevice.Value.Handle;

        /// <summary>
        /// Gets the underlying VkPhysicalDevice used by the GraphicsDevice.
        /// </summary>
        public IntPtr PhysicalDevice => _gd.VkPhysicalDevice.Value.Handle;

        /// <summary>
        /// Gets the VkQueue which is used by the GraphicsDevice to submit graphics work.
        /// </summary>
        public IntPtr GraphicsQueue => _gd.VkGraphicsQueue.Value.Handle;

        /// <summary>
        /// Gets the queue family index of the graphics VkQueue.
        /// </summary>
        public uint MainQueueFamilyIndex => _gd.MainQueueFamilyIndex;

        /// <summary>
        /// Gets the VkInstanceProcAddr function pointer.
        /// </summary>
        public nint GetInstanceProcAddr { get; }

        /// <summary>
        /// Gets the VkDeviceProcAddr function pointer.
        /// </summary>
        public nint GetDeviceProcAddr { get; }
        
        /// <summary>
        /// Gets the Vulkan API version used by the device.
        /// </summary>
        public uint ApiVersion { get; }

        ///// <summary>
        ///// Gets the driver name of the device. May be null.
        ///// </summary>
        //public string DriverName => _gd.DriverName;

        ///// <summary>
        ///// Gets the driver information of the device. May be null.
        ///// </summary>
        //public string DriverInfo => _gd.DriverInfo;

        public ReadOnlyCollection<ReadOnlyMemoryUtf8> AvailableInstanceLayers => _instanceLayers.Value;

        public ReadOnlyCollection<ReadOnlyMemoryUtf8> AvailableInstanceExtensions => _instanceExtensions;

        public ReadOnlyCollection<ExtensionProperties> AvailableDeviceExtensions => _deviceExtensions.Value;

        /// <summary>
        /// Overrides the current VkImageLayout tracked by the given Texture. This should be used when a VkImage is created by
        /// an external library to inform XenoAtom.Graphics about its initial layout.
        /// </summary>
        /// <param name="texture">The Texture whose currently-tracked VkImageLayout will be overridden.</param>
        /// <param name="layout">The new VkImageLayout value.</param>
        public void OverrideImageLayout(Texture texture, uint layout)
        {
            VkTexture vkTex = Util.AssertSubtype<Texture, VkTexture>(texture);
            for (uint layer = 0; layer < vkTex.ArrayLayers; layer++)
            {
                for (uint level = 0; level < vkTex.MipLevels; level++)
                {
                    vkTex.SetImageLayout(level, layer, (VkImageLayout)layout);
                }
            }
        }

        /// <summary>
        /// Gets the underlying VkImageLayout by the given XenoAtom.Graphics Texture.
        /// </summary>
        /// <param name="texture">The Texture whose underlying VkImageLayout will be returned.</param>
        /// <param name="mipLevel">The miplevel to query.</param>
        /// <param name="arrayLayer">The array layer to query.</param>
        /// <returns>The underlying VkImage for the given Texture.</returns>
        /// <exception cref="ArgumentException">If the texture is staging.</exception>
        public uint GetVkImageLayout(Texture texture, uint mipLevel = 0U, uint arrayLayer = 0U)
        {
            VkTexture vkTexture = Util.AssertSubtype<Texture, VkTexture>(texture);
            if ((vkTexture.Usage & TextureUsage.Staging) != 0) throw new ArgumentException("Invalid staging texture. The texture cannot be a staging texture", nameof(texture));
            return (uint)vkTexture.GetImageLayout(mipLevel, arrayLayer);
        }

        /// <summary>
        /// Gets the characteristics of the given Texture's underlying VkImage.
        /// </summary>
        /// <param name="texture">The texture to query.</param>
        /// <param name="vkFormat">The VkFormat of the texture.</param>
        /// <param name="vkImageUsageFlags">The VkImageUsageFlags of the texture.</param>
        /// <param name="vkImageTiling">The VkImageTiling of the texture</param>
        /// <exception cref="ArgumentException">If the texture is staging.</exception>
        public void GetVkImageCharacteristics(Texture texture, out uint vkFormat, out uint vkImageUsageFlags, out uint vkImageTiling)
        {
            VkTexture vkTexture = Util.AssertSubtype<Texture, VkTexture>(texture);
            if ((vkTexture.Usage & TextureUsage.Staging) != 0) throw new ArgumentException("Invalid staging texture. The texture cannot be a staging texture", nameof(texture));
            vkFormat = (uint)vkTexture.VkFormat;
            vkImageUsageFlags = (uint)vkTexture.VkImageUsageFlags;
            vkImageTiling = (uint)vkTexture.VkImageTiling;
        }

        /// <summary>
        /// Transitions the given Texture's underlying VkImage into a new layout.
        /// </summary>
        /// <param name="texture">The Texture whose underlying VkImage will be transitioned.</param>
        /// <param name="layout">The new VkImageLayout value.</param>
        public void TransitionImageLayout(Texture texture, uint layout)
        {
            _gd.TransitionImageLayout(Util.AssertSubtype<Texture, VkTexture>(texture), (VkImageLayout)layout);
        }

        private unsafe ReadOnlyCollection<ExtensionProperties> EnumerateDeviceExtensions()
        {
            VkExtensionProperties[] vkProps = _gd.GetDeviceExtensionProperties();
            ExtensionProperties[] veldridProps = new ExtensionProperties[vkProps.Length];

            for (int i = 0; i < vkProps.Length; i++)
            {
                VkExtensionProperties prop = vkProps[i];
                veldridProps[i] = new ExtensionProperties(Util.GetString(prop.extensionName), prop.specVersion);
            }

            return new ReadOnlyCollection<ExtensionProperties>(veldridProps);
        }

        public readonly struct ExtensionProperties
        {
            public readonly string Name;
            public readonly uint SpecVersion;

            public ExtensionProperties(string name, uint specVersion)
            {
                Name = name ?? throw new ArgumentNullException(nameof(name));
                SpecVersion = specVersion;
            }

            public override string ToString()
            {
                return Name;
            }
        }
    }
}
#endif

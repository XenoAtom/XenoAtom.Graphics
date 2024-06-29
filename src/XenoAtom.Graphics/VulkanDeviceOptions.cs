using XenoAtom.Interop;

namespace XenoAtom.Graphics
{
    /// <summary>
    /// A structure describing Vulkan-specific device creation options.
    /// </summary>
    public struct VulkanDeviceOptions
    {
        /// <summary>
        /// An array of required Vulkan device extensions. Entries in this array will be enabled in the GraphicsDevice's
        /// created VkDevice.
        /// </summary>
        public ReadOnlyMemoryUtf8[] DeviceExtensions { get; set; }

        /// <summary>
        /// Constructs a new VulkanDeviceOptions.
        /// </summary>
        public VulkanDeviceOptions()
        {
            DeviceExtensions = [];
        }
        
        /// <summary>
        /// Constructs a new VulkanDeviceOptions.
        /// </summary>
        /// <param name="deviceExtensions">An array of required Vulkan device extensions. Entries in this array will be enabled
        /// in the GraphicsDevice's created VkDevice.</param>
        public VulkanDeviceOptions(ReadOnlyMemoryUtf8[] deviceExtensions)
        {
            DeviceExtensions = deviceExtensions;
        }
    }
}

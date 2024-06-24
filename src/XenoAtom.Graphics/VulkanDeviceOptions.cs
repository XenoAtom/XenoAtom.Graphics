using XenoAtom.Interop;

namespace XenoAtom.Graphics
{
    /// <summary>
    /// A structure describing Vulkan-specific device creation options.
    /// </summary>
    public struct VulkanDeviceOptions
    {
        /// <summary>
        /// An array of required Vulkan instance extensions. Entries in this array will be enabled in the GraphicsDevice's
        /// created VkInstance.
        /// </summary>
        public ReadOnlyMemoryUtf8[] InstanceExtensions { get; set; }
        /// <summary>
        /// An array of required Vulkan device extensions. Entries in this array will be enabled in the GraphicsDevice's
        /// created VkDevice.
        /// </summary>
        public ReadOnlyMemoryUtf8[] DeviceExtensions { get; set; }

        /// <summary>
        /// Gets or sets the name of the application.
        /// </summary>
        public ReadOnlyMemoryUtf8 ApplicationName { get; set; }

        /// <summary>
        /// Gets or sets the name of the engine.
        /// </summary>
        public ReadOnlyMemoryUtf8 EngineName { get; set; }

        /// <summary>
        /// Constructs a new VulkanDeviceOptions.
        /// </summary>
        /// <param name="instanceExtensions">An array of required Vulkan instance extensions. Entries in this array will be
        /// enabled in the GraphicsDevice's created VkInstance.</param>
        /// <param name="deviceExtensions">An array of required Vulkan device extensions. Entries in this array will be enabled
        /// in the GraphicsDevice's created VkDevice.</param>
        public VulkanDeviceOptions(ReadOnlyMemoryUtf8[] instanceExtensions, ReadOnlyMemoryUtf8[] deviceExtensions)
        {
            InstanceExtensions = instanceExtensions;
            DeviceExtensions = deviceExtensions;
        }
    }
}

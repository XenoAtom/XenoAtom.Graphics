// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using XenoAtom.Interop;

namespace XenoAtom.Graphics
{
    /// <summary>
    /// A structure describing Vulkan-specific device creation options.
    /// </summary>
    public struct VulkanManagerOptions
    {
        /// <summary>
        /// An array of required Vulkan instance extensions. Entries in this array will be enabled in the GraphicsDevice's
        /// created VkInstance.
        /// </summary>
        public ReadOnlyMemoryUtf8[] InstanceExtensions { get; set; }

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
        public VulkanManagerOptions()
        {
            InstanceExtensions = [];
        }
        
        /// <summary>
        /// Constructs a new VulkanDeviceOptions.
        /// </summary>
        /// <param name="instanceExtensions">An array of required Vulkan instance extensions. Entries in this array will be
        /// enabled in the GraphicsDevice's created VkInstance.</param>
        public VulkanManagerOptions(ReadOnlyMemoryUtf8[] instanceExtensions)
        {
            InstanceExtensions = instanceExtensions;
        }
    }
}
using System;
using System.IO;

namespace XenoAtom.Graphics
{
    /// <summary>
    /// A structure describing several common properties of a GraphicsDevice.
    /// </summary>
    public struct GraphicsDeviceOptions
    {
        /// <summary>
        /// Indicates whether the Graphicsdevice will include a "main" Swapchain. If this value is true, then the GraphicsDevice
        /// must be created with one of the overloads that provides Swapchain source information.
        /// </summary>
        public bool HasMainSwapchain;
        /// <summary>
        /// An optional <see cref="PixelFormat"/> to be used for the depth buffer of the swapchain. If this value is null, then
        /// no depth buffer will be present on the swapchain.
        /// </summary>
        public PixelFormat? SwapchainDepthFormat;
        /// <summary>
        /// Indicates whether the main Swapchain will be synchronized to the window system's vertical refresh rate.
        /// </summary>
        public bool SyncToVerticalBlank;
        /// <summary>
        /// Specifies which model the rendering backend should use for binding resources. This can be overridden per-pipeline
        /// by specifying a value in <see cref="GraphicsPipelineDescription.ResourceBindingModel"/>.
        /// </summary>
        public ResourceBindingModel ResourceBindingModel;
        /// <summary>
        /// Indicates whether a 0-to-1 depth range mapping is preferred. For OpenGL, this is not the default, and is not available
        /// on all systems.
        /// </summary>
        public bool PreferDepthRangeZeroToOne;
        /// <summary>
        /// Indicates whether a bottom-to-top-increasing clip space Y direction is preferred. For Vulkan, this is not the
        /// default, and may not be available on all systems.
        /// </summary>
        public bool PreferStandardClipSpaceYDirection;
        /// <summary>
        /// Indicates whether the main Swapchain should use an sRGB format. This value is only used in cases where the properties
        /// of the main SwapChain are not explicitly specified with a <see cref="SwapchainDescription"/>. If they are, then the
        /// value of <see cref="SwapchainDescription.ColorSrgb"/> will supercede the value specified here.
        /// </summary>
        public bool SwapchainSrgbFormat;

        /// <summary>
        /// Gets or sets Vulkan device specific options.
        /// </summary>
        public VulkanDeviceOptions VulkanDeviceOptions;

        /// <summary>
        /// Default GraphicsDeviceOptions.
        /// </summary>
        public GraphicsDeviceOptions()
        {
            VulkanDeviceOptions = new();
        }
        
        /// <summary>
        /// Constructs a new GraphicsDeviceOptions for a device with a main Swapchain.
        /// </summary>
        /// <param name="swapchainDepthFormat">An optional <see cref="PixelFormat"/> to be used for the depth buffer of the
        /// swapchain. If this value is null, then no depth buffer will be present on the swapchain.</param>
        /// <param name="syncToVerticalBlank">Indicates whether the main Swapchain will be synchronized to the window system's
        /// vertical refresh rate.</param>
        public GraphicsDeviceOptions(PixelFormat? swapchainDepthFormat, bool syncToVerticalBlank) : this()
        {
            HasMainSwapchain = true;
            SwapchainDepthFormat = swapchainDepthFormat;
            SyncToVerticalBlank = syncToVerticalBlank;
        }

        /// <summary>
        /// Constructs a new GraphicsDeviceOptions for a device with a main Swapchain.
        /// </summary>
        /// <param name="swapchainDepthFormat">An optional <see cref="PixelFormat"/> to be used for the depth buffer of the
        /// swapchain. If this value is null, then no depth buffer will be present on the swapchain.</param>
        /// <param name="syncToVerticalBlank">Indicates whether the main Swapchain will be synchronized to the window system's
        /// vertical refresh rate.</param>
        /// <param name="resourceBindingModel">Specifies which model the rendering backend should use for binding resources.</param>
        public GraphicsDeviceOptions(
            PixelFormat? swapchainDepthFormat,
            bool syncToVerticalBlank,
            ResourceBindingModel resourceBindingModel) : this()
        {
            HasMainSwapchain = true;
            SwapchainDepthFormat = swapchainDepthFormat;
            SyncToVerticalBlank = syncToVerticalBlank;
            ResourceBindingModel = resourceBindingModel;
        }

        /// <summary>
        /// Constructs a new GraphicsDeviceOptions for a device with a main Swapchain.
        /// </summary>
        /// <param name="swapchainDepthFormat">An optional <see cref="PixelFormat"/> to be used for the depth buffer of the
        /// swapchain. If this value is null, then no depth buffer will be present on the swapchain.</param>
        /// <param name="syncToVerticalBlank">Indicates whether the main Swapchain will be synchronized to the window system's
        /// vertical refresh rate.</param>
        /// <param name="resourceBindingModel">Specifies which model the rendering backend should use for binding resources.</param>
        /// <param name="preferDepthRangeZeroToOne">Indicates whether a 0-to-1 depth range mapping is preferred. For OpenGL,
        /// this is not the default, and is not available on all systems.</param>
        public GraphicsDeviceOptions(
            PixelFormat? swapchainDepthFormat,
            bool syncToVerticalBlank,
            ResourceBindingModel resourceBindingModel,
            bool preferDepthRangeZeroToOne) : this()
        {
            HasMainSwapchain = true;
            SwapchainDepthFormat = swapchainDepthFormat;
            SyncToVerticalBlank = syncToVerticalBlank;
            ResourceBindingModel = resourceBindingModel;
            PreferDepthRangeZeroToOne = preferDepthRangeZeroToOne;
        }

        /// <summary>
        /// Constructs a new GraphicsDeviceOptions for a device with a main Swapchain.
        /// </summary>
        /// <param name="debug">Indicates whether the GraphicsDevice will enable debug features, provided they are supported by
        /// the host system.</param>
        /// <param name="swapchainDepthFormat">An optional <see cref="PixelFormat"/> to be used for the depth buffer of the
        /// swapchain. If this value is null, then no depth buffer will be present on the swapchain.</param>
        /// <param name="syncToVerticalBlank">Indicates whether the main Swapchain will be synchronized to the window system's
        /// vertical refresh rate.</param>
        /// <param name="resourceBindingModel">Specifies which model the rendering backend should use for binding resources.</param>
        /// <param name="preferDepthRangeZeroToOne">Indicates whether a 0-to-1 depth range mapping is preferred. For OpenGL,
        /// this is not the default, and is not available on all systems.</param>
        /// <param name="preferStandardClipSpaceYDirection">Indicates whether a bottom-to-top-increasing clip space Y direction
        /// is preferred. For Vulkan, this is not the default, and is not available on all systems.</param>
        public GraphicsDeviceOptions(
            PixelFormat? swapchainDepthFormat,
            bool syncToVerticalBlank,
            ResourceBindingModel resourceBindingModel,
            bool preferDepthRangeZeroToOne,
            bool preferStandardClipSpaceYDirection) : this()
        {
            HasMainSwapchain = true;
            SwapchainDepthFormat = swapchainDepthFormat;
            SyncToVerticalBlank = syncToVerticalBlank;
            ResourceBindingModel = resourceBindingModel;
            PreferDepthRangeZeroToOne = preferDepthRangeZeroToOne;
            PreferStandardClipSpaceYDirection = preferStandardClipSpaceYDirection;
        }

        /// <summary>
        /// Constructs a new GraphicsDeviceOptions for a device with a main Swapchain.
        /// </summary>
        /// <param name="debug">Indicates whether the GraphicsDevice will enable debug features, provided they are supported by
        /// the host system.</param>
        /// <param name="swapchainDepthFormat">An optional <see cref="PixelFormat"/> to be used for the depth buffer of the
        /// swapchain. If this value is null, then no depth buffer will be present on the swapchain.</param>
        /// <param name="syncToVerticalBlank">Indicates whether the main Swapchain will be synchronized to the window system's
        /// vertical refresh rate.</param>
        /// <param name="resourceBindingModel">Specifies which model the rendering backend should use for binding resources.</param>
        /// <param name="preferDepthRangeZeroToOne">Indicates whether a 0-to-1 depth range mapping is preferred. For OpenGL,
        /// this is not the default, and is not available on all systems.</param>
        /// <param name="preferStandardClipSpaceYDirection">Indicates whether a bottom-to-top-increasing clip space Y direction
        /// is preferred. For Vulkan, this is not the default, and is not available on all systems.</param>
        /// <param name="swapchainSrgbFormat">Indicates whether the main Swapchain should use an sRGB format. This value is only
        /// used in cases where the properties of the main SwapChain are not explicitly specified with a
        /// <see cref="SwapchainDescription"/>. If they are, then the value of <see cref="SwapchainDescription.ColorSrgb"/> will
        /// supercede the value specified here.</param>
        public GraphicsDeviceOptions(
            PixelFormat? swapchainDepthFormat,
            bool syncToVerticalBlank,
            ResourceBindingModel resourceBindingModel,
            bool preferDepthRangeZeroToOne,
            bool preferStandardClipSpaceYDirection,
            bool swapchainSrgbFormat) : this()
        {
            HasMainSwapchain = true;
            SwapchainDepthFormat = swapchainDepthFormat;
            SyncToVerticalBlank = syncToVerticalBlank;
            ResourceBindingModel = resourceBindingModel;
            PreferDepthRangeZeroToOne = preferDepthRangeZeroToOne;
            PreferStandardClipSpaceYDirection = preferStandardClipSpaceYDirection;
            SwapchainSrgbFormat = swapchainSrgbFormat;
        }
    }
}

using System;

namespace XenoAtom.Graphics
{
    /// <summary>
    /// Describes a <see cref="Swapchain"/>, for creation via a <see cref="GraphicsDevice"/>.
    /// </summary>
    public readonly record struct SwapchainDescription
    {
        /// <summary>
        /// The <see cref="SwapchainSource"/> which will be used as the target of rendering operations.
        /// This is a window-system-specific object which differs by platform.
        /// </summary>
        public SwapchainSource Source { get; init; }

        /// <summary>
        /// The initial width of the Swapchain surface.
        /// </summary>
        public uint Width { get; init; }

        /// <summary>
        /// The initial height of the Swapchain surface.
        /// </summary>
        public uint Height { get; init; }

        /// <summary>
        /// The optional format of the depth target of the Swapchain's Framebuffer.
        /// If non-null, this must be a valid depth Texture format.
        /// If null, then no depth target will be created.
        /// </summary>
        public PixelFormat? DepthFormat { get; init; }

        /// <summary>
        /// Indicates whether presentation of the Swapchain will be synchronized to the window system's vertical refresh rate.
        /// </summary>
        public bool SyncToVerticalBlank { get; init; }

        /// <summary>
        /// Indicates whether the color target of the Swapchain will use an sRGB PixelFormat.
        /// </summary>
        public bool ColorSrgb { get; init; }

        /// <summary>
        /// Indicates whether the color target of the Swapchain will use an BGRA PixelFormat or a RGBA PixelFormat. By default, this is false (preferring RGBA).
        /// </summary>
        public bool PreferBgraOrder { get; init; }

        /// <summary>
        /// Constructs a new SwapchainDescription. Color space will be set to sRGB.
        /// </summary>
        /// <param name="source">The <see cref="SwapchainSource"/> which will be used as the target of rendering operations.
        /// This is a window-system-specific object which differs by platform.</param>
        /// <param name="width">The initial width of the Swapchain surface.</param>
        /// <param name="height">The initial height of the Swapchain surface.</param>
        /// <param name="depthFormat">The optional format of the depth target of the Swapchain's Framebuffer.
        /// If non-null, this must be a valid depth Texture format.
        /// If null, then no depth target will be created.</param>
        /// <param name="syncToVerticalBlank">Indicates whether presentation of the Swapchain will be synchronized to the window
        /// system's vertical refresh rate.</param>
        public SwapchainDescription(
            SwapchainSource source,
            uint width,
            uint height,
            PixelFormat? depthFormat,
            bool syncToVerticalBlank)
        {
            Source = source;
            Width = width;
            Height = height;
            DepthFormat = depthFormat;
            SyncToVerticalBlank = syncToVerticalBlank;
            ColorSrgb = true;
        }

        /// <summary>
        /// Constructs a new SwapchainDescription.
        /// </summary>
        /// <param name="source">The <see cref="SwapchainSource"/> which will be used as the target of rendering operations.
        /// This is a window-system-specific object which differs by platform.</param>
        /// <param name="width">The initial width of the Swapchain surface.</param>
        /// <param name="height">The initial height of the Swapchain surface.</param>
        /// <param name="depthFormat">The optional format of the depth target of the Swapchain's Framebuffer.
        /// If non-null, this must be a valid depth Texture format.
        /// If null, then no depth target will be created.</param>
        /// <param name="syncToVerticalBlank">Indicates whether presentation of the Swapchain will be synchronized to the window
        /// system's vertical refresh rate.</param>
        /// <param name="colorSrgb">Indicates whether the color target of the Swapchain will use an sRGB PixelFormat.</param>
        public SwapchainDescription(
            SwapchainSource source,
            uint width,
            uint height,
            PixelFormat? depthFormat,
            bool syncToVerticalBlank,
            bool colorSrgb)
        {
            Source = source;
            Width = width;
            Height = height;
            DepthFormat = depthFormat;
            SyncToVerticalBlank = syncToVerticalBlank;
            ColorSrgb = colorSrgb;
        }
    }
}

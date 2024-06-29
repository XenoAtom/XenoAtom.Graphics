using System;

namespace XenoAtom.Graphics
{
    /// <summary>
    /// A device resource providing the ability to present rendered images to a visible surface.
    /// See <see cref="SwapchainDescription"/>.
    /// </summary>
    public abstract class Swapchain : GraphicsDeviceObject
    {
        internal Swapchain(GraphicsDevice device) : base(device)
        {
        }

        /// <summary>
        /// Gets a <see cref="Framebuffer"/> representing the render targets of this instance.
        /// </summary>
        public abstract Framebuffer Framebuffer { get; }
        /// <summary>
        /// Resizes the renderable Textures managed by this instance to the given dimensions.
        /// </summary>
        /// <param name="width">The new width of the Swapchain.</param>
        /// <param name="height">The new height of the Swapchain.</param>
        public abstract void Resize(uint width, uint height);
        /// <summary>
        /// Gets or sets whether presentation of this Swapchain will be synchronized to the window system's vertical refresh
        /// rate.
        /// </summary>
        public abstract bool SyncToVerticalBlank { get; set; }

        /// <summary>
        /// Swaps the buffers of the swapchain and presents the rendered image to the screen.
        /// </summary>
        public abstract void SwapBuffers();
    }
}

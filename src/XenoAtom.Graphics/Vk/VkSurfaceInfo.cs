using System;
using static XenoAtom.Interop.vulkan;
using static XenoAtom.Graphics.Vk.VulkanUtil;


namespace XenoAtom.Graphics.Vk
{
    /// <summary>
    /// An object which can be used to create a VkSurfaceKHR.
    /// </summary>
    public abstract class VkSurfaceSource
    {
        internal VkSurfaceSource() { }

        /// <summary>
        /// Creates a new VkSurfaceSource from the given Xlib information.
        /// </summary>
        /// <param name="display">A pointer to the Xlib Display.</param>
        /// <param name="window">An Xlib window.</param>
        /// <returns>A new VkSurfaceSource.</returns>
        //public unsafe static VkSurfaceSource CreateXlib(Display* display, Window window) => new XlibVkSurfaceInfo(display, window);

        internal abstract SwapchainSource GetSurfaceSource();
    }

    internal class Win32VkSurfaceInfo : VkSurfaceSource
    {
        private readonly IntPtr _hinstance;
        private readonly IntPtr _hwnd;

        public Win32VkSurfaceInfo(IntPtr hinstance, IntPtr hwnd)
        {
            _hinstance = hinstance;
            _hwnd = hwnd;
        }

        internal override SwapchainSource GetSurfaceSource()
        {
            return new Win32SwapchainSource(_hwnd, _hinstance);
        }
    }

    //internal class XlibVkSurfaceInfo : VkSurfaceSource
    //{
    //    private readonly unsafe Display* _display;
    //    private readonly Window _window;

    //    public unsafe XlibVkSurfaceInfo(Display* display, Window window)
    //    {
    //        _display = display;
    //        _window = window;
    //    }

    //    public unsafe override VkSurfaceKHR CreateSurface(VkInstance instance)
    //    {
    //        return VkSurfaceUtil.CreateSurface(null, instance, GetSurfaceSource());
    //    }

    //    internal unsafe override SwapchainSource GetSurfaceSource()
    //    {
    //        return new XlibSwapchainSource((IntPtr)_display, _window.Value);
    //    }
    //}
}

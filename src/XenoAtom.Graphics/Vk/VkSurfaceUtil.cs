using static XenoAtom.Interop.vulkan;
using static XenoAtom.Graphics.Vk.VulkanUtil;

namespace XenoAtom.Graphics.Vk
{
    internal static unsafe class VkSurfaceUtil
    {
        internal static VkSurfaceKHR CreateSurface(VkGraphicsDevice gd, VkInstance instance, SwapchainSource swapchainSource)
        {
            if (!gd.HasSurfaceExtension(VK_KHR_SURFACE_EXTENSION_NAME))
                throw new GraphicsException($"The required instance extension was not available: {VK_KHR_SURFACE_EXTENSION_NAME}");

            switch (swapchainSource)
            {
                case XlibSwapchainSource xlibSource:
                    if (!gd.HasSurfaceExtension(VK_KHR_XLIB_SURFACE_EXTENSION_NAME))
                    {
                        throw new GraphicsException($"The required instance extension was not available: {VK_KHR_XLIB_SURFACE_EXTENSION_NAME}");
                    }
                    return CreateXlib(instance, xlibSource);
                case WaylandSwapchainSource waylandSource:
                    if (!gd.HasSurfaceExtension(VK_KHR_WAYLAND_SURFACE_EXTENSION_NAME))
                    {
                        throw new GraphicsException($"The required instance extension was not available: {VK_KHR_WAYLAND_SURFACE_EXTENSION_NAME}");
                    }
                    return CreateWayland(instance, waylandSource);
                case Win32SwapchainSource win32Source:
                    if (!gd.HasSurfaceExtension(VK_KHR_WIN32_SURFACE_EXTENSION_NAME))
                    {
                        throw new GraphicsException($"The required instance extension was not available: {VK_KHR_WIN32_SURFACE_EXTENSION_NAME}");
                    }
                    return CreateWin32(instance, win32Source);
                default:
                    throw new GraphicsException($"The provided SwapchainSource cannot be used to create a Vulkan surface.");
            }
        }

        private static VkSurfaceKHR CreateWin32(VkInstance instance, Win32SwapchainSource win32Source)
        {
            VkWin32SurfaceCreateInfoKHR surfaceCI = new VkWin32SurfaceCreateInfoKHR();
            surfaceCI.hwnd = win32Source.Hwnd;
            surfaceCI.hinstance = win32Source.Hinstance;
            var vkCreateWin32SurfaceKHR = vkGetInstanceProcAddr<PFN_vkCreateWin32SurfaceKHR>(instance);
            VkResult result = vkCreateWin32SurfaceKHR.Invoke(instance, surfaceCI, null, out VkSurfaceKHR surface);
            CheckResult(result);
            return surface;
        }

        private static VkSurfaceKHR CreateXlib(VkInstance instance, XlibSwapchainSource xlibSource)
        {
            VkXlibSurfaceCreateInfoKHR xsci = new VkXlibSurfaceCreateInfoKHR();
            xsci.dpy = (void*)xlibSource.Display;
            xsci.window = (nuint)xlibSource.Window;
            var vkCreateXlibSurfaceKHR = vkGetInstanceProcAddr<PFN_vkCreateXlibSurfaceKHR>(instance);
            VkResult result = vkCreateXlibSurfaceKHR.Invoke(instance, xsci, null, out VkSurfaceKHR surface);
            CheckResult(result);
            return surface;
        }

        private static VkSurfaceKHR CreateWayland(VkInstance instance, WaylandSwapchainSource waylandSource)
        {
            VkWaylandSurfaceCreateInfoKHR wsci = new ()
            {
                display = new(waylandSource.Display),
                surface = new(waylandSource.Surface)
            };
            var vkCreateWaylandSurfaceKHR = vkGetInstanceProcAddr<PFN_vkCreateWaylandSurfaceKHR>(instance);
            VkResult result = vkCreateWaylandSurfaceKHR.Invoke(instance, wsci, null, out VkSurfaceKHR surface);
            CheckResult(result);
            return surface;
        }
    }
}

using System.Linq;
using static XenoAtom.Interop.vulkan;

using static XenoAtom.Graphics.Vk.VulkanUtil;
using System;
using System.Runtime.InteropServices;
using XenoAtom.Interop;
using System.Runtime.CompilerServices;

namespace XenoAtom.Graphics.Vk
{
    internal unsafe class VkSwapchain : Swapchain
    {
        private VkGraphicsDevice _gd => Unsafe.As<GraphicsDevice, VkGraphicsDevice>(ref Unsafe.AsRef(in Device));
        private readonly VkSurfaceKHR _surface;
        private VkSwapchainKHR _deviceSwapchain;
        private readonly VkSwapchainFramebuffer _framebuffer;
        private XenoAtom.Interop.vulkan.VkFence _imageAvailableFence;
        private readonly uint _presentQueueIndex;
        private readonly VkQueue _presentQueue;
        private bool _syncToVBlank;
        private readonly SwapchainSource _swapchainSource;
        private readonly bool _colorSrgb;
        private bool? _newSyncToVBlank;
        private uint _currentImageIndex;


        public override Framebuffer Framebuffer => _framebuffer;
        public override bool SyncToVerticalBlank
        {
            get => _newSyncToVBlank ?? _syncToVBlank;
            set
            {
                if (_syncToVBlank != value)
                {
                    _newSyncToVBlank = value;
                }
            }
        }


        public VkSwapchainKHR DeviceSwapchain => _deviceSwapchain;
        public uint ImageIndex => _currentImageIndex;
        public XenoAtom.Interop.vulkan.VkFence ImageAvailableFence => _imageAvailableFence;
        public VkSurfaceKHR Surface => _surface;
        public VkQueue PresentQueue => _presentQueue;
        public uint PresentQueueIndex => _presentQueueIndex;

        public VkSwapchain(VkGraphicsDevice gd, ref SwapchainDescription description) : this(gd, ref description, default) { }

        public VkSwapchain(VkGraphicsDevice gd, ref SwapchainDescription description, VkSurfaceKHR existingSurface) : base(gd)
        {
            _syncToVBlank = description.SyncToVerticalBlank;
            _swapchainSource = description.Source;
            _colorSrgb = description.ColorSrgb;

            if (existingSurface == default)
            {
                _surface = VkSurfaceUtil.CreateSurface(gd, gd.Instance, _swapchainSource);
            }
            else
            {
                _surface = existingSurface;
            }

            if (!GetPresentQueueIndex(out _presentQueueIndex))
            {
                throw new GraphicsException($"The system does not support presenting the given Vulkan surface.");
            }
            vkGetDeviceQueue(_gd.Device, _presentQueueIndex, 0, out _presentQueue);

            _framebuffer = new VkSwapchainFramebuffer(gd, this, _surface, description.Width, description.Height, description.DepthFormat);

            CreateSwapchain(description.Width, description.Height);

            VkFenceCreateInfo fenceCI = new VkFenceCreateInfo();
            fenceCI.flags = (VkFenceCreateFlagBits)0;
            vkCreateFence(_gd.Device, fenceCI, null, out var imageAvailableFence);

            AcquireNextImage(_gd.Device, default, imageAvailableFence);
            vkWaitForFences(_gd.Device, 1, &imageAvailableFence, true, ulong.MaxValue);
            vkResetFences(_gd.Device, 1, &imageAvailableFence);
            _imageAvailableFence = imageAvailableFence;
        }

        public override void Resize(uint width, uint height)
        {
            RecreateAndReacquire(width, height);
        }

        public bool AcquireNextImage(VkDevice device, VkSemaphore semaphore, XenoAtom.Interop.vulkan.VkFence fence)
        {
            if (_newSyncToVBlank != null)
            {
                _syncToVBlank = _newSyncToVBlank.Value;
                _newSyncToVBlank = null;
                RecreateAndReacquire(_framebuffer.Width, _framebuffer.Height);
                return false;
            }

            var currentImageIndex = _currentImageIndex;
            VkResult result = _gd.vkAcquireNextImageKHR.Invoke(
                device,
                _deviceSwapchain,
                ulong.MaxValue,
                semaphore,
                fence,
                &currentImageIndex);
            _currentImageIndex = currentImageIndex;
            _framebuffer.SetImageIndex(currentImageIndex);
            if (result == VK_ERROR_OUT_OF_DATE_KHR || result == VK_SUBOPTIMAL_KHR)
            {
                CreateSwapchain(_framebuffer.Width, _framebuffer.Height);
                return false;
            }
            else if (result != VK_SUCCESS)
            {
                throw new GraphicsException("Could not acquire next image from the Vulkan swapchain.");
            }

            return true;
        }

        private void RecreateAndReacquire(uint width, uint height)
        {
            if (CreateSwapchain(width, height))
            {
                var imageAvailableFence = _imageAvailableFence;
                if (AcquireNextImage(_gd.Device, default, imageAvailableFence))
                {
                    vkWaitForFences(_gd.Device, 1, &imageAvailableFence, true, ulong.MaxValue);
                    vkResetFences(_gd.Device, 1, &imageAvailableFence);
                }
                _imageAvailableFence = imageAvailableFence;
            }
        }

        private bool CreateSwapchain(uint width, uint height)
        {
            // Obtain the surface capabilities first -- this will indicate whether the surface has been lost.
            VkResult result = _gd.vkGetPhysicalDeviceSurfaceCapabilitiesKHR.Invoke(_gd.PhysicalDevice, _surface, out VkSurfaceCapabilitiesKHR surfaceCapabilities);
            if (result == VK_ERROR_SURFACE_LOST_KHR)
            {
                throw new GraphicsException($"The Swapchain's underlying surface has been lost.");
            }

            if (surfaceCapabilities.minImageExtent.width == 0 && surfaceCapabilities.minImageExtent.height == 0
                && surfaceCapabilities.maxImageExtent.width == 0 && surfaceCapabilities.maxImageExtent.height == 0)
            {
                return false;
            }

            if (_deviceSwapchain != default)
            {
                _gd.WaitForIdle();
            }

            _currentImageIndex = 0;
            uint surfaceFormatCount = 0;
            result = _gd.vkGetPhysicalDeviceSurfaceFormatsKHR.Invoke(_gd.PhysicalDevice, _surface, out surfaceFormatCount);
            CheckResult(result);
            VkSurfaceFormatKHR[] formats = new VkSurfaceFormatKHR[surfaceFormatCount];
            result = _gd.vkGetPhysicalDeviceSurfaceFormatsKHR.Invoke(_gd.PhysicalDevice, _surface, formats);
            CheckResult(result);

            VkFormat desiredFormat = _colorSrgb
                ? VK_FORMAT_B8G8R8A8_SRGB
                : VK_FORMAT_B8G8R8A8_UNORM;

            VkSurfaceFormatKHR surfaceFormat = new VkSurfaceFormatKHR();
            if (formats.Length == 1 && formats[0].format == VK_FORMAT_UNDEFINED)
            {
                surfaceFormat = new VkSurfaceFormatKHR { colorSpace = VK_COLOR_SPACE_SRGB_NONLINEAR_KHR, format = desiredFormat };
            }
            else
            {
                foreach (VkSurfaceFormatKHR format in formats)
                {
                    if (format.colorSpace == VK_COLOR_SPACE_SRGB_NONLINEAR_KHR && format.format == desiredFormat)
                    {
                        surfaceFormat = format;
                        break;
                    }
                }
                if (surfaceFormat.format == VK_FORMAT_UNDEFINED)
                {
                    if (_colorSrgb && surfaceFormat.format != VK_FORMAT_R8G8B8A8_SRGB)
                    {
                        throw new GraphicsException($"Unable to create an sRGB Swapchain for this surface.");
                    }

                    surfaceFormat = formats[0];
                }
            }

            uint presentModeCount = 0;
            result = _gd.vkGetPhysicalDeviceSurfacePresentModesKHR.Invoke(_gd.PhysicalDevice, _surface, out presentModeCount);
            CheckResult(result);
            VkPresentModeKHR[] presentModes = new VkPresentModeKHR[presentModeCount];
            result = _gd.vkGetPhysicalDeviceSurfacePresentModesKHR.Invoke(_gd.PhysicalDevice, _surface, presentModes);
            CheckResult(result);

            VkPresentModeKHR presentMode = VK_PRESENT_MODE_FIFO_KHR;

            if (_syncToVBlank)
            {
                if (presentModes.Contains(VK_PRESENT_MODE_FIFO_RELAXED_KHR))
                {
                    presentMode = VK_PRESENT_MODE_FIFO_RELAXED_KHR;
                }
            }
            else
            {
                if (presentModes.Contains(VK_PRESENT_MODE_MAILBOX_KHR))
                {
                    presentMode = VK_PRESENT_MODE_MAILBOX_KHR;
                }
                else if (presentModes.Contains(VK_PRESENT_MODE_IMMEDIATE_KHR))
                {
                    presentMode = VK_PRESENT_MODE_IMMEDIATE_KHR;
                }
            }

            uint maxImageCount = surfaceCapabilities.maxImageCount == 0 ? uint.MaxValue : surfaceCapabilities.maxImageCount;
            uint imageCount = Math.Min(maxImageCount, surfaceCapabilities.minImageCount + 1);

            VkSwapchainCreateInfoKHR swapchainCI = new VkSwapchainCreateInfoKHR();
            swapchainCI.surface = _surface;
            swapchainCI.presentMode = presentMode;
            swapchainCI.imageFormat = surfaceFormat.format;
            swapchainCI.imageColorSpace = surfaceFormat.colorSpace;
            uint clampedWidth = Util.Clamp(width, surfaceCapabilities.minImageExtent.width, surfaceCapabilities.maxImageExtent.width);
            uint clampedHeight = Util.Clamp(height, surfaceCapabilities.minImageExtent.height, surfaceCapabilities.maxImageExtent.height);
            swapchainCI.imageExtent = new VkExtent2D { width = clampedWidth, height = clampedHeight };
            swapchainCI.minImageCount = imageCount;
            swapchainCI.imageArrayLayers = 1;
            swapchainCI.imageUsage = VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT | VK_IMAGE_USAGE_TRANSFER_DST_BIT;

            FixedArray2<uint> queueFamilyIndices = new();
            queueFamilyIndices[0] = _gd.GraphicsQueueIndex;
            queueFamilyIndices[1] = _gd.PresentQueueIndex;

            if (_gd.GraphicsQueueIndex != _gd.PresentQueueIndex)
            {
                swapchainCI.imageSharingMode = VK_SHARING_MODE_CONCURRENT;
                swapchainCI.queueFamilyIndexCount = 2;
                swapchainCI.pQueueFamilyIndices = (uint*)&queueFamilyIndices;
            }
            else
            {
                swapchainCI.imageSharingMode = VK_SHARING_MODE_EXCLUSIVE;
                swapchainCI.queueFamilyIndexCount = 0;
            }

            swapchainCI.preTransform = VK_SURFACE_TRANSFORM_IDENTITY_BIT_KHR;
            swapchainCI.compositeAlpha = VK_COMPOSITE_ALPHA_OPAQUE_BIT_KHR;
            swapchainCI.clipped = true;

            VkSwapchainKHR oldSwapchain = _deviceSwapchain;
            swapchainCI.oldSwapchain = oldSwapchain;

            VkSwapchainKHR deviceSwapchain;
            result = _gd.vkCreateSwapchainKHR.Invoke(_gd.Device, &swapchainCI, null, &deviceSwapchain);
            _deviceSwapchain = deviceSwapchain;
            CheckResult(result);
            if (oldSwapchain != default)
            {
                _gd.vkDestroySwapchainKHR.Invoke(_gd.Device, oldSwapchain, null);
            }

            _framebuffer.SetNewSwapchain(_deviceSwapchain, width, height, surfaceFormat, swapchainCI.imageExtent);
            return true;
        }

        private bool GetPresentQueueIndex(out uint queueFamilyIndex)
        {
            uint graphicsQueueIndex = _gd.GraphicsQueueIndex;
            uint presentQueueIndex = _gd.PresentQueueIndex;

            if (QueueSupportsPresent(graphicsQueueIndex, _surface))
            {
                queueFamilyIndex = graphicsQueueIndex;
                return true;
            }
            else if (graphicsQueueIndex != presentQueueIndex && QueueSupportsPresent(presentQueueIndex, _surface))
            {
                queueFamilyIndex = presentQueueIndex;
                return true;
            }

            queueFamilyIndex = 0;
            return false;
        }

        private bool QueueSupportsPresent(uint queueFamilyIndex, VkSurfaceKHR surface)
        {
            VkResult result = _gd.vkGetPhysicalDeviceSurfaceSupportKHR.Invoke(
                _gd.PhysicalDevice,
                queueFamilyIndex,
                surface,
                out VkBool32 supported);
            CheckResult(result);
            return supported;
        }

        internal override void DisposeCore()
        {
            vkDestroyFence(_gd.Device, _imageAvailableFence, null);
            _framebuffer.Dispose();
            _gd.vkDestroySwapchainKHR.Invoke(_gd.Device, _deviceSwapchain, null);
            _gd.vkDestroySurfaceKHR.Invoke(_gd.Instance, _surface, null);
        }
    }
}

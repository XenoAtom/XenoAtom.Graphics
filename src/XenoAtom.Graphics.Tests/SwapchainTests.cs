using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace XenoAtom.Graphics.Tests
{
    public abstract class SwapchainTests : GraphicsDeviceTestBase
    {
        /*
        [TestMethod]
        [DataRow(PixelFormat.R16_UNorm, false)]
        [DataRow(PixelFormat.R16_UNorm, true)]
        [DataRow(PixelFormat.R32_Float, false)]
        [DataRow(PixelFormat.R32_Float, true)]
        [DataRow(null, false)]
        [DataRow(null, true)]
        public void Ctor_SetsProperties(PixelFormat? depthFormat, bool syncToVerticalBlank)
        {
            //Sdl2Window window = new Sdl2Window("SwapchainTestWindow", 0, 0, 100, 100, SDL_WindowFlags.Hidden, false);
            //SwapchainSource source = VeldridStartup.GetSwapchainSource(window);
            //SwapchainDescription swapchainDesc = new SwapchainDescription(source, 100, 100, depthFormat, syncToVerticalBlank);
            //Swapchain swapchain = GD.CreateSwapchain(ref swapchainDesc);

            //if (depthFormat == null)
            //{
            //    Assert.Null(swapchain.Framebuffer.DepthTarget);
            //}
            //else
            //{
            //    Assert.NotNull(swapchain.Framebuffer.DepthTarget);
            //    Assert.AreEqual(depthFormat, swapchain.Framebuffer.DepthTarget.Value.Target.Format);
            //}

            //Assert.AreEqual(syncToVerticalBlank, swapchain.SyncToVerticalBlank);

            //window.Close();
        }
        */
    }

    public abstract class MainSwapchainTests : GraphicsDeviceTestBase
    {
        //[TestMethod]
        //public void Textures_Properties_Correct()
        //{
        //    Texture colorTarget = GD.MainSwapchain.Framebuffer.ColorTargets[0].Target;
        //    Assert.AreEqual(TextureType.Texture2D, colorTarget.Type);
        //    Assert.InRange(colorTarget.Width, 1u, uint.MaxValue);
        //    Assert.InRange(colorTarget.Height, 1u, uint.MaxValue);
        //    Assert.AreEqual(1u, colorTarget.Depth);
        //    Assert.AreEqual(1u, colorTarget.ArrayLayers);
        //    Assert.AreEqual(1u, colorTarget.MipLevels);
        //    Assert.AreEqual(TextureUsage.RenderTarget, colorTarget.Usage);
        //    Assert.AreEqual(TextureSampleCount.Count1, colorTarget.SampleCount);

        //    Texture depthTarget = GD.MainSwapchain.Framebuffer.DepthTarget.Value.Target;
        //    Assert.AreEqual(TextureType.Texture2D, depthTarget.Type);
        //    Assert.AreEqual(colorTarget.Width, depthTarget.Width);
        //    Assert.AreEqual(colorTarget.Height, depthTarget.Height);
        //    Assert.AreEqual(1u, depthTarget.Depth);
        //    Assert.AreEqual(1u, depthTarget.ArrayLayers);
        //    Assert.AreEqual(1u, depthTarget.MipLevels);
        //    Assert.AreEqual(TextureUsage.DepthStencil, depthTarget.Usage);
        //    Assert.AreEqual(TextureSampleCount.Count1, depthTarget.SampleCount);
        //}

    }
}

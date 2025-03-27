using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace XenoAtom.Graphics.Tests
{
    [TestClass]
    public class FramebufferTests : GraphicsDeviceTestBase
    {
        [TestMethod]
        public void NoDepthTarget_ClearAllColors_Succeeds()
        {
            Texture colorTarget = GD.CreateTexture(
                TextureDescription.Texture2D(1024, 1024, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.RenderTarget));
            Framebuffer fb = GD.CreateFramebuffer(new FramebufferDescription(null, colorTarget));

            Texture staging = GD.CreateTexture(
                TextureDescription.Texture2D(1024, 1024, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Staging));

            using (var cbp = GD.CreateCommandBufferPool(new(CommandBufferPoolFlags.CanResetCommandBuffer)))
            using (var cb = cbp.CreateCommandBuffer())
            {
                cb.Begin();


                cb.SetFramebuffer(fb);
                cb.ClearColorTarget(0, RgbaFloat.Red);
                cb.End();
                GD.SubmitCommands(cb);
                GD.WaitForIdle();
                
                cb.Begin();
                cb.CopyTexture(
                    colorTarget, 0, 0, 0, 0, 0,
                    staging, 0, 0, 0, 0, 0,
                    1024, 1024, 1, 1);
                cb.End();
                GD.SubmitCommands(cb);
                GD.WaitForIdle();
            }

            MappedResourceView<RgbaFloat> view = GD.Map<RgbaFloat>(staging, MapMode.Read);
            for (int i = 0; i < view.Count; i++)
            {
                Assert.AreEqual(RgbaFloat.Red, view[i]);
            }
            GD.Unmap(staging);
        }

        [TestMethod]
        public void NoDepthTarget_ClearDepth_Fails()
        {
            Texture colorTarget = GD.CreateTexture(
                TextureDescription.Texture2D(1024, 1024, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.RenderTarget));
            Framebuffer fb = GD.CreateFramebuffer(new FramebufferDescription(null, colorTarget));

            using (var cbp = GD.CreateCommandBufferPool())
            using (var cb = cbp.CreateCommandBuffer())
            {
                cb.Begin();
                cb.SetFramebuffer(fb);
                Assert.Throws<GraphicsException>(() => cb.ClearDepthStencil(1f));
            }
        }

        [TestMethod]
        public void NoColorTarget_ClearColor_Fails()
        {
            Texture depthTarget = GD.CreateTexture(
                TextureDescription.Texture2D(1024, 1024, 1, 1, PixelFormat.R16_UNorm, TextureUsage.DepthStencil));
            Framebuffer fb = GD.CreateFramebuffer(new FramebufferDescription(depthTarget));

            using (var cbp = GD.CreateCommandBufferPool())
            using (var cb = cbp.CreateCommandBuffer())
            {
                cb.Begin();
                cb.SetFramebuffer(fb);
                Assert.Throws<GraphicsException>(() => cb.ClearColorTarget(0, RgbaFloat.Red));
            }
        }

        [TestMethod]
        public void ClearColorTarget_OutOfRange_Fails()
        {
            TextureDescription desc = TextureDescription.Texture2D(
                1024, 1024, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.RenderTarget);
            Texture colorTarget0 = GD.CreateTexture(desc);
            Texture colorTarget1 = GD.CreateTexture(desc);
            Framebuffer fb = GD.CreateFramebuffer(new FramebufferDescription(null, colorTarget0, colorTarget1));

            using (var cbp = GD.CreateCommandBufferPool())
            using (var cb = cbp.CreateCommandBuffer())
            {
                cb.Begin();
                cb.SetFramebuffer(fb);
                cb.ClearColorTarget(0, RgbaFloat.Red);
                cb.ClearColorTarget(1, RgbaFloat.Red);
                Assert.Throws<GraphicsException>(() => cb.ClearColorTarget(2, RgbaFloat.Red));
                Assert.Throws<GraphicsException>(() => cb.ClearColorTarget(3, RgbaFloat.Red));
            }
        }

        [TestMethod]
        public void NonZeroMipLevel_ClearColor_Succeeds()
        {
            Texture testTex = GD.CreateTexture(
                TextureDescription.Texture2D(1024, 1024, 11, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.RenderTarget));

            Framebuffer[] framebuffers = new Framebuffer[11];
            for (uint level = 0; level < 11; level++)
            {
                framebuffers[level] = GD.CreateFramebuffer(
                    new FramebufferDescription(null, new[] { new FramebufferAttachmentDescription(testTex, 0, level) }));
            }

            Texture readback = GD.CreateTexture(
                TextureDescription.Texture2D(1024, 1024, 11, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Staging));

            using (var cbp = GD.CreateCommandBufferPool(new(CommandBufferPoolFlags.CanResetCommandBuffer)))
            using (var cb = cbp.CreateCommandBuffer())
            {
                cb.Begin();
                for (uint level = 0; level < 11; level++)
                {
                    cb.SetFramebuffer(framebuffers[level]);
                    cb.ClearColorTarget(0, new RgbaFloat(level, level, level, 1));
                }

                cb.End();
                GD.SubmitCommands(cb);
                GD.WaitForIdle();

                cb.Begin();
                cb.CopyTexture(testTex, readback);
                cb.End();
                GD.SubmitCommands(cb);
                GD.WaitForIdle();
            }

            uint mipWidth = 1024;
            uint mipHeight = 1024;
            for (uint level = 0; level < 11; level++)
            {
                MappedResourceView<RgbaFloat> readView = GD.Map<RgbaFloat>(readback, MapMode.Read, level);
                for (uint y = 0; y < mipHeight; y++)
                    for (uint x = 0; x < mipWidth; x++)
                    {
                        Assert.AreEqual(new RgbaFloat(level, level, level, 1), readView[x, y]);
                    }

                GD.Unmap(readback, level);
                mipWidth = Math.Max(1, mipWidth / 2);
                mipHeight = Math.Max(1, mipHeight / 2);
            }
        }
    }

    public abstract class SwapchainFramebufferTests : GraphicsDeviceTestBase
    {
        //[TestMethod]
        //public void ClearSwapchainFramebuffer_Succeeds()
        //{
        //    CommandList cl = GD.CreateCommandList();
        //    cl.Begin();
        //    cl.SetFramebuffer(GD.SwapchainFramebuffer);
        //    cl.ClearColorTarget(0, RgbaFloat.Red);
        //    cl.ClearDepthStencil(1f);
        //    cl.End();
        //}
    }
}

using System;
using Xunit;
using Xunit.Abstractions;

namespace XenoAtom.Graphics.Tests
{
    public abstract class FramebufferTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
    {
        protected FramebufferTests(ITestOutputHelper textOutputHelper) : base(textOutputHelper)
        {
        }

        [Fact]
        public void NoDepthTarget_ClearAllColors_Succeeds()
        {
            Texture colorTarget = RF.CreateTexture(
                TextureDescription.Texture2D(1024, 1024, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.RenderTarget));
            Framebuffer fb = RF.CreateFramebuffer(new FramebufferDescription(null, colorTarget));

            CommandList cl = RF.CreateCommandList();
            cl.Begin();
            cl.SetFramebuffer(fb);
            cl.ClearColorTarget(0, RgbaFloat.Red);
            cl.End();
            GD.SubmitCommands(cl);
            GD.WaitForIdle();

            Texture staging = RF.CreateTexture(
                TextureDescription.Texture2D(1024, 1024, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Staging));

            cl.Begin();
            cl.CopyTexture(
                colorTarget, 0, 0, 0, 0, 0,
                staging, 0, 0, 0, 0, 0,
                1024, 1024, 1, 1);
            cl.End();
            GD.SubmitCommands(cl);
            GD.WaitForIdle();

            MappedResourceView<RgbaFloat> view = GD.Map<RgbaFloat>(staging, MapMode.Read);
            for (int i = 0; i < view.Count; i++)
            {
                Assert.Equal(RgbaFloat.Red, view[i]);
            }
            GD.Unmap(staging);
        }

        [Fact]
        public void NoDepthTarget_ClearDepth_Fails()
        {
            Texture colorTarget = RF.CreateTexture(
                TextureDescription.Texture2D(1024, 1024, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.RenderTarget));
            Framebuffer fb = RF.CreateFramebuffer(new FramebufferDescription(null, colorTarget));

            CommandList cl = RF.CreateCommandList();
            cl.Begin();
            cl.SetFramebuffer(fb);
            Assert.Throws<GraphicsException>(() => cl.ClearDepthStencil(1f));
        }

        [Fact]
        public void NoColorTarget_ClearColor_Fails()
        {
            Texture depthTarget = RF.CreateTexture(
                TextureDescription.Texture2D(1024, 1024, 1, 1, PixelFormat.R16_UNorm, TextureUsage.DepthStencil));
            Framebuffer fb = RF.CreateFramebuffer(new FramebufferDescription(depthTarget));

            CommandList cl = RF.CreateCommandList();
            cl.Begin();
            cl.SetFramebuffer(fb);
            Assert.Throws<GraphicsException>(() => cl.ClearColorTarget(0, RgbaFloat.Red));
        }

        [Fact]
        public void ClearColorTarget_OutOfRange_Fails()
        {
            TextureDescription desc = TextureDescription.Texture2D(
                1024, 1024, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.RenderTarget);
            Texture colorTarget0 = RF.CreateTexture(desc);
            Texture colorTarget1 = RF.CreateTexture(desc);
            Framebuffer fb = RF.CreateFramebuffer(new FramebufferDescription(null, colorTarget0, colorTarget1));

            CommandList cl = RF.CreateCommandList();
            cl.Begin();
            cl.SetFramebuffer(fb);
            cl.ClearColorTarget(0, RgbaFloat.Red);
            cl.ClearColorTarget(1, RgbaFloat.Red);
            Assert.Throws<GraphicsException>(() => cl.ClearColorTarget(2, RgbaFloat.Red));
            Assert.Throws<GraphicsException>(() => cl.ClearColorTarget(3, RgbaFloat.Red));
        }

        [Fact]
        public void NonZeroMipLevel_ClearColor_Succeeds()
        {
            Texture testTex = RF.CreateTexture(
                TextureDescription.Texture2D(1024, 1024, 11, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.RenderTarget));

            Framebuffer[] framebuffers = new Framebuffer[11];
            for (uint level = 0; level < 11; level++)
            {
                framebuffers[level] = RF.CreateFramebuffer(
                    new FramebufferDescription(null, new[] { new FramebufferAttachmentDescription(testTex, 0, level) }));
            }

            CommandList cl = RF.CreateCommandList();
            cl.Begin();
            for (uint level = 0; level < 11; level++)
            {
                cl.SetFramebuffer(framebuffers[level]);
                cl.ClearColorTarget(0, new RgbaFloat(level, level, level, 1));
            }
            cl.End();
            GD.SubmitCommands(cl);
            GD.WaitForIdle();

            Texture readback = RF.CreateTexture(
                TextureDescription.Texture2D(1024, 1024, 11, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Staging));
            cl.Begin();
            cl.CopyTexture(testTex, readback);
            cl.End();
            GD.SubmitCommands(cl);
            GD.WaitForIdle();

            uint mipWidth = 1024;
            uint mipHeight = 1024;
            for (uint level = 0; level < 11; level++)
            {
                MappedResourceView<RgbaFloat> readView = GD.Map<RgbaFloat>(readback, MapMode.Read, level);
                for (uint y = 0; y < mipHeight; y++)
                    for (uint x = 0; x < mipWidth; x++)
                    {
                        Assert.Equal(new RgbaFloat(level, level, level, 1), readView[x, y]);
                    }

                GD.Unmap(readback, level);
                mipWidth = Math.Max(1, mipWidth / 2);
                mipHeight = Math.Max(1, mipHeight / 2);
            }
        }
    }

    public abstract class SwapchainFramebufferTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
    {
        protected SwapchainFramebufferTests(ITestOutputHelper textOutputHelper) : base(textOutputHelper)
        {
        }

        //[Fact]
        //public void ClearSwapchainFramebuffer_Succeeds()
        //{
        //    CommandList cl = RF.CreateCommandList();
        //    cl.Begin();
        //    cl.SetFramebuffer(GD.SwapchainFramebuffer);
        //    cl.ClearColorTarget(0, RgbaFloat.Red);
        //    cl.ClearDepthStencil(1f);
        //    cl.End();
        //}
    }

    [Trait("Backend", "Vulkan")]
    public class VulkanFramebufferTests : FramebufferTests<VulkanDeviceCreator>
    {
        public VulkanFramebufferTests(ITestOutputHelper textOutputHelper) : base(textOutputHelper)
        {
        }
    }
}

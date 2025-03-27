using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace XenoAtom.Graphics.Tests
{
    [TestClass]
    public class DisposalTestBase : GraphicsDeviceTestBase
    {
        [TestMethod]
        public void Dispose_Buffer()
        {
            DeviceBuffer b = GD.CreateBuffer(new BufferDescription(256, BufferUsage.VertexBuffer));
            b.Dispose();
            Assert.IsTrue(b.IsDisposed);
        }

        [TestMethod]
        public void Dispose_Texture()
        {
            Texture t = GD.CreateTexture(TextureDescription.Texture2D(1, 1, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Sampled));
            TextureView tv = GD.CreateTextureView(t);
            GD.WaitForIdle(); // Required currently by Vulkan backend.
            tv.Dispose();
            Assert.IsTrue(tv.IsDisposed);
            Assert.IsFalse(t.IsDisposed);
            t.Dispose();
            Assert.IsTrue(t.IsDisposed);
        }

        [TestMethod]
        public void Dispose_Framebuffer()
        {
            Texture t = GD.CreateTexture(TextureDescription.Texture2D(1, 1, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.RenderTarget));
            Framebuffer fb = GD.CreateFramebuffer(new FramebufferDescription(null, t));
            GD.WaitForIdle(); // Required currently by Vulkan backend.
            fb.Dispose();
            Assert.IsTrue(fb.IsDisposed);
            Assert.IsFalse(t.IsDisposed);
            t.Dispose();
            Assert.IsTrue(t.IsDisposed);
        }

        [TestMethod]
        public void Dispose_CommandBuffer_And_Pool()
        {
            var cbp = GD.CreateCommandBufferPool();
            var cb = cbp.CreateCommandBuffer();
            cb.Dispose();
            Assert.IsFalse(cb.IsDisposed);
            cbp.Dispose();
            Assert.IsTrue(cbp.IsDisposed);
            Assert.IsTrue(cb.IsDisposed);
        }

        [TestMethod]
        public void Dispose_CommandBufferPoolManager_Rent()
        {
            CommandBufferPool pool;
            {
                pool = GD.PoolManager.Rent();
                Assert.AreEqual(CommandBufferPoolState.Ready, pool.State);
                using var cb = pool.CreateCommandBuffer();
                Assert.AreEqual(CommandBufferPoolState.Ready, pool.State);
                Assert.AreEqual(CommandBufferState.Ready, cb.State);
                cb.Begin();
                Assert.AreEqual(CommandBufferState.Recording, cb.State);
                Assert.AreEqual(CommandBufferPoolState.InUse, pool.State);
                cb.End();
                Assert.AreEqual(CommandBufferState.Recorded, cb.State);
                Assert.AreEqual(CommandBufferPoolState.Ready, pool.State);
                GD.SubmitCommands(cb);
                Assert.AreEqual(CommandBufferState.Submitted, cb.State);
                Assert.AreEqual(CommandBufferPoolState.InUse, pool.State);
                GD.WaitForIdle();
                GD.Refresh();
                Assert.AreEqual(CommandBufferState.Unallocated, cb.State);
                Assert.AreEqual(CommandBufferPoolState.InPool, pool.State);
                pool.Dispose();
            }
            Assert.AreEqual(CommandBufferPoolState.InPool, pool.State);

            CommandBufferPool newPool;
            {
                newPool = GD.PoolManager.Rent();
                Assert.IsTrue(object.ReferenceEquals(pool, newPool), "Invalid state, we should have reused the previous pool");
                Assert.AreEqual(CommandBufferPoolState.Ready, newPool.State);
                GD.PoolManager.Return(newPool);
                Assert.AreEqual(CommandBufferPoolState.InPool, newPool.State);
            }
        }

        [TestMethod]
        public void Dispose_Sampler()
        {
            Sampler s = GD.CreateSampler(SamplerDescription.Point);
            s.Dispose();
            Assert.IsTrue(s.IsDisposed);
        }

        [TestMethod]
        public void Dispose_Pipeline()
        {
            Shader[] shaders = TestShaders.LoadVertexFragment(GD, "UIntVertexAttribs");
            ShaderSetDescription shaderSet = new ShaderSetDescription(
                new VertexLayoutDescription[]
                {
                    new VertexLayoutDescription(
                        new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
                        new VertexElementDescription("Color_UInt", VertexElementSemantic.TextureCoordinate, VertexElementFormat.UInt4))
                },
                shaders);

            ResourceLayout layout = GD.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("InfoBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex),
                new ResourceLayoutElementDescription("Ortho", ResourceKind.UniformBuffer, ShaderStages.Vertex)));

            GraphicsPipelineDescription gpd = new GraphicsPipelineDescription(
                BlendStateDescription.SingleOverrideBlend,
                DepthStencilStateDescription.Disabled,
                RasterizerStateDescription.Default,
                PrimitiveTopology.PointList,
                shaderSet,
                layout,
                new OutputDescription(null, new OutputAttachmentDescription(PixelFormat.R32_G32_B32_A32_Float)));
            Pipeline pipeline = GD.CreateGraphicsPipeline(gpd);
            pipeline.Dispose();
            Assert.IsTrue(pipeline.IsDisposed);
            Assert.IsFalse(shaders[0].IsDisposed);
            Assert.IsFalse(shaders[1].IsDisposed);
            Assert.IsFalse(layout.IsDisposed);
            layout.Dispose();
            Assert.IsTrue(layout.IsDisposed);
            Assert.IsFalse(shaders[0].IsDisposed);
            Assert.IsFalse(shaders[1].IsDisposed);
            shaders[0].Dispose();
            Assert.IsTrue(shaders[0].IsDisposed);
            shaders[1].Dispose();
            Assert.IsTrue(shaders[1].IsDisposed);
        }

        [TestMethod]
        public void Dispose_ResourceSet()
        {
            ResourceLayout layout = GD.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("InfoBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex),
                new ResourceLayoutElementDescription("Ortho", ResourceKind.UniformBuffer, ShaderStages.Vertex)));

            DeviceBuffer ub0 = GD.CreateBuffer(new BufferDescription(256, BufferUsage.UniformBuffer));
            DeviceBuffer ub1 = GD.CreateBuffer(new BufferDescription(256, BufferUsage.UniformBuffer));

            ResourceSet rs = GD.CreateResourceSet(new ResourceSetDescription(layout, ub0, ub1));
            rs.Dispose();
            Assert.IsTrue(rs.IsDisposed);
            Assert.IsFalse(ub0.IsDisposed);
            Assert.IsFalse(ub1.IsDisposed);
            Assert.IsFalse(layout.IsDisposed);
            layout.Dispose();
            Assert.IsTrue(layout.IsDisposed);
            Assert.IsFalse(ub0.IsDisposed);
            Assert.IsFalse(ub1.IsDisposed);
            ub0.Dispose();
            Assert.IsTrue(ub0.IsDisposed);
            ub1.Dispose();
            Assert.IsTrue(ub1.IsDisposed);
        }
    }
}

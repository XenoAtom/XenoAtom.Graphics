using Xunit;
using Xunit.Abstractions;

namespace XenoAtom.Graphics.Tests
{
    public abstract class DisposalTestBase : GraphicsDeviceTestBase
    {
        protected DisposalTestBase(ITestOutputHelper textOutputHelper) : base(textOutputHelper)
        {
        }

        [Fact]
        public void Dispose_Buffer()
        {
            DeviceBuffer b = GD.CreateBuffer(new BufferDescription(256, BufferUsage.VertexBuffer));
            b.Dispose();
            Assert.True(b.IsDisposed);
        }

        [Fact]
        public void Dispose_Texture()
        {
            Texture t = GD.CreateTexture(TextureDescription.Texture2D(1, 1, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Sampled));
            TextureView tv = GD.CreateTextureView(t);
            GD.WaitForIdle(); // Required currently by Vulkan backend.
            tv.Dispose();
            Assert.True(tv.IsDisposed);
            Assert.False(t.IsDisposed);
            t.Dispose();
            Assert.True(t.IsDisposed);
        }

        [Fact]
        public void Dispose_Framebuffer()
        {
            Texture t = GD.CreateTexture(TextureDescription.Texture2D(1, 1, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.RenderTarget));
            Framebuffer fb = GD.CreateFramebuffer(new FramebufferDescription(null, t));
            GD.WaitForIdle(); // Required currently by Vulkan backend.
            fb.Dispose();
            Assert.True(fb.IsDisposed);
            Assert.False(t.IsDisposed);
            t.Dispose();
            Assert.True(t.IsDisposed);
        }

        [Fact]
        public void Dispose_CommandList()
        {
            CommandList cl = GD.CreateCommandList();
            cl.Dispose();
            Assert.True(cl.IsDisposed);
        }

        [Fact]
        public void Dispose_Sampler()
        {
            Sampler s = GD.CreateSampler(SamplerDescription.Point);
            s.Dispose();
            Assert.True(s.IsDisposed);
        }

        [Fact]
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
            Assert.True(pipeline.IsDisposed);
            Assert.False(shaders[0].IsDisposed);
            Assert.False(shaders[1].IsDisposed);
            Assert.False(layout.IsDisposed);
            layout.Dispose();
            Assert.True(layout.IsDisposed);
            Assert.False(shaders[0].IsDisposed);
            Assert.False(shaders[1].IsDisposed);
            shaders[0].Dispose();
            Assert.True(shaders[0].IsDisposed);
            shaders[1].Dispose();
            Assert.True(shaders[1].IsDisposed);
        }

        [Fact]
        public void Dispose_ResourceSet()
        {
            ResourceLayout layout = GD.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("InfoBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex),
                new ResourceLayoutElementDescription("Ortho", ResourceKind.UniformBuffer, ShaderStages.Vertex)));

            DeviceBuffer ub0 = GD.CreateBuffer(new BufferDescription(256, BufferUsage.UniformBuffer));
            DeviceBuffer ub1 = GD.CreateBuffer(new BufferDescription(256, BufferUsage.UniformBuffer));

            ResourceSet rs = GD.CreateResourceSet(new ResourceSetDescription(layout, ub0, ub1));
            rs.Dispose();
            Assert.True(rs.IsDisposed);
            Assert.False(ub0.IsDisposed);
            Assert.False(ub1.IsDisposed);
            Assert.False(layout.IsDisposed);
            layout.Dispose();
            Assert.True(layout.IsDisposed);
            Assert.False(ub0.IsDisposed);
            Assert.False(ub1.IsDisposed);
            ub0.Dispose();
            Assert.True(ub0.IsDisposed);
            ub1.Dispose();
            Assert.True(ub1.IsDisposed);
        }
    }

    [Trait("Backend", "Vulkan")]
    public class VulkanDisposalTests : DisposalTestBase
    {
        public VulkanDisposalTests(ITestOutputHelper textOutputHelper) : base(textOutputHelper)
        {
        }
    }
}

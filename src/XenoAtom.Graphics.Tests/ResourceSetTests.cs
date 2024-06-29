using System.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace XenoAtom.Graphics.Tests
{
    public abstract class ResourceSetTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
    {
        [Fact]
        public void ResourceSet_BufferInsteadOfTextureView_Fails()
        {
            ResourceLayout layout = RF.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("TV0", ResourceKind.TextureReadOnly, ShaderStages.Vertex)));

            DeviceBuffer ub = RF.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer));

            Assert.Throws<GraphicsException>(() =>
            {
                ResourceSet set = RF.CreateResourceSet(new ResourceSetDescription(layout,
                    ub));
            });
        }

        [Fact]
        public void ResourceSet_IncorrectTextureUsage_Fails()
        {
            ResourceLayout layout = RF.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("TV0", ResourceKind.TextureReadWrite, ShaderStages.Vertex)));

            Texture t = RF.CreateTexture(TextureDescription.Texture2D(64, 64, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));
            TextureView tv = RF.CreateTextureView(t);

            Assert.Throws<GraphicsException>(() =>
            {
                ResourceSet set = RF.CreateResourceSet(new ResourceSetDescription(layout, tv));
            });
        }

        [Fact]
        public void ResourceSet_IncorrectBufferUsage_Fails()
        {
            ResourceLayout layout = RF.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("RWB0", ResourceKind.StructuredBufferReadWrite, ShaderStages.Vertex)));

            DeviceBuffer readOnlyBuffer = RF.CreateBuffer(new BufferDescription(1024, BufferUsage.UniformBuffer));

            Assert.Throws<GraphicsException>(() =>
            {
                ResourceSet set = RF.CreateResourceSet(new ResourceSetDescription(layout, readOnlyBuffer));
            });
        }

        [Fact]
        public void ResourceSet_TooFewOrTooManyElements_Fails()
        {
            ResourceLayout layout = RF.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("UB0", ResourceKind.UniformBuffer, ShaderStages.Vertex),
                new ResourceLayoutElementDescription("UB1", ResourceKind.UniformBuffer, ShaderStages.Vertex),
                new ResourceLayoutElementDescription("UB2", ResourceKind.UniformBuffer, ShaderStages.Vertex)));

            DeviceBuffer ub = RF.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer));

            Assert.Throws<GraphicsException>(() =>
            {
                RF.CreateResourceSet(new ResourceSetDescription(layout, ub));
            });

            Assert.Throws<GraphicsException>(() =>
            {
                RF.CreateResourceSet(new ResourceSetDescription(layout, ub, ub));
            });

            Assert.Throws<GraphicsException>(() =>
            {
                RF.CreateResourceSet(new ResourceSetDescription(layout, ub, ub, ub, ub));
            });

            Assert.Throws<GraphicsException>(() =>
            {
                RF.CreateResourceSet(new ResourceSetDescription(layout, ub, ub, ub, ub, ub));
            });
        }

        [Fact]
        public void ResourceSet_InvalidUniformOffset_Fails()
        {
            ResourceLayout layout = RF.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("UB0", ResourceKind.UniformBuffer, ShaderStages.Vertex)));

            DeviceBuffer buffer = RF.CreateBuffer(new BufferDescription(1024, BufferUsage.UniformBuffer));

            Assert.Throws<GraphicsException>(() =>
            {
                RF.CreateResourceSet(new ResourceSetDescription(layout,
                    new DeviceBufferRange(buffer, GD.UniformBufferMinOffsetAlignment - 1, 256)));
            });

            Assert.Throws<GraphicsException>(() =>
            {
                RF.CreateResourceSet(new ResourceSetDescription(layout,
                    new DeviceBufferRange(buffer, GD.UniformBufferMinOffsetAlignment + 1, 256)));
            });
        }

        [Fact]
        public void ResourceSet_NoPipelineBound_Fails()
        {
            ResourceLayout layout = RF.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("UB0", ResourceKind.UniformBuffer, ShaderStages.Vertex)));
            DeviceBuffer ub = RF.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer));


            ResourceSet rs = RF.CreateResourceSet(new ResourceSetDescription(layout, ub));

            CommandList cl = RF.CreateCommandList();
            cl.Begin();
            Assert.Throws<GraphicsException>(() => cl.SetGraphicsResourceSet(0, rs));
            cl.End();
        }

        [Fact]
        public void ResourceSet_InvalidSlot_Fails()
        {
            DeviceBuffer infoBuffer = RF.CreateBuffer(new BufferDescription(16, BufferUsage.UniformBuffer));
            DeviceBuffer orthoBuffer = RF.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer));

            ShaderSetDescription shaderSet = new ShaderSetDescription(
                new VertexLayoutDescription[]
                {
                    new VertexLayoutDescription(
                        new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
                        new VertexElementDescription("Color_UInt", VertexElementSemantic.TextureCoordinate, VertexElementFormat.UInt4))
                },
                TestShaders.LoadVertexFragment(RF, "UIntVertexAttribs"));

            ResourceLayout layout = RF.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("InfoBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex),
                new ResourceLayoutElementDescription("Ortho", ResourceKind.UniformBuffer, ShaderStages.Vertex)));

            ResourceSet set = RF.CreateResourceSet(new ResourceSetDescription(layout, infoBuffer, orthoBuffer));

            GraphicsPipelineDescription gpd = new GraphicsPipelineDescription(
                BlendStateDescription.SingleOverrideBlend,
                DepthStencilStateDescription.Disabled,
                RasterizerStateDescription.Default,
                PrimitiveTopology.PointList,
                shaderSet,
                layout,
                new OutputDescription(null, new OutputAttachmentDescription(PixelFormat.B8_G8_R8_A8_UNorm)));

            Pipeline pipeline = RF.CreateGraphicsPipeline(gpd);

            CommandList cl = RF.CreateCommandList();
            cl.Begin();
            cl.SetPipeline(pipeline);
            Assert.Throws<GraphicsException>(() => cl.SetGraphicsResourceSet(1, set));
            Assert.Throws<GraphicsException>(() => cl.SetGraphicsResourceSet(2, set));
            Assert.Throws<GraphicsException>(() => cl.SetGraphicsResourceSet(3, set));
            cl.End();
        }

        [Fact]
        public void ResourceSet_IncompatibleSet_Fails()
        {
            DeviceBuffer infoBuffer = RF.CreateBuffer(new BufferDescription(16, BufferUsage.UniformBuffer));
            DeviceBuffer orthoBuffer = RF.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer));

            ShaderSetDescription shaderSet = new ShaderSetDescription(
                new VertexLayoutDescription[]
                {
                    new VertexLayoutDescription(
                        new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
                        new VertexElementDescription("Color_UInt", VertexElementSemantic.TextureCoordinate, VertexElementFormat.UInt4))
                },
                TestShaders.LoadVertexFragment(RF, "UIntVertexAttribs"));

            ResourceLayout layout = RF.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("InfoBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex),
                new ResourceLayoutElementDescription("Ortho", ResourceKind.UniformBuffer, ShaderStages.Vertex)));

            ResourceLayout layout2 = RF.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("InfoBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex),
                new ResourceLayoutElementDescription("Tex", ResourceKind.TextureReadOnly, ShaderStages.Fragment)));

            ResourceLayout layout3 = RF.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("InfoBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex)));

            Texture tex = RF.CreateTexture(TextureDescription.Texture2D(16, 16, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Sampled));
            TextureView texView = RF.CreateTextureView(tex);

            ResourceSet set = RF.CreateResourceSet(new ResourceSetDescription(layout, infoBuffer, orthoBuffer));
            ResourceSet set2 = RF.CreateResourceSet(new ResourceSetDescription(layout2, infoBuffer, texView));
            ResourceSet set3 = RF.CreateResourceSet(new ResourceSetDescription(layout3, infoBuffer));

            GraphicsPipelineDescription gpd = new GraphicsPipelineDescription(
                BlendStateDescription.SingleOverrideBlend,
                DepthStencilStateDescription.Disabled,
                RasterizerStateDescription.Default,
                PrimitiveTopology.PointList,
                shaderSet,
                layout,
                new OutputDescription(null, new OutputAttachmentDescription(PixelFormat.B8_G8_R8_A8_UNorm)));

            Pipeline pipeline = RF.CreateGraphicsPipeline(gpd);

            CommandList cl = RF.CreateCommandList();
            cl.Begin();
            cl.SetPipeline(pipeline);
            cl.SetGraphicsResourceSet(0, set);
            Assert.Throws<GraphicsException>(() => cl.SetGraphicsResourceSet(0, set2)); // Wrong type
            Assert.Throws<GraphicsException>(() => cl.SetGraphicsResourceSet(0, set3)); // Wrong count
            cl.End();
        }

        protected ResourceSetTests(ITestOutputHelper textOutputHelper) : base(textOutputHelper)
        {
        }
    }

    [Trait("Backend", "Vulkan")]
    public class VulkanResourceSetTests : ResourceSetTests<VulkanDeviceCreator>
    {
        public VulkanResourceSetTests(ITestOutputHelper textOutputHelper) : base(textOutputHelper)
        {
        }
    }
}

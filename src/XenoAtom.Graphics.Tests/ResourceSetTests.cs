using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace XenoAtom.Graphics.Tests
{
    [TestClass]
    public class ResourceSetTests : GraphicsDeviceTestBase
    {
        [TestMethod]
        public void ResourceSet_BufferInsteadOfTextureView_Fails()
        {
            ResourceLayout layout = GD.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("TV0", ResourceKind.TextureReadOnly, ShaderStages.Vertex)));

            DeviceBuffer ub = GD.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer));

            Assert.Throws<GraphicsException>(() =>
            {
                ResourceSet set = GD.CreateResourceSet(new ResourceSetDescription(layout,
                    ub));
            });
        }

        [TestMethod]
        public void ResourceSet_IncorrectTextureUsage_Fails()
        {
            ResourceLayout layout = GD.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("TV0", ResourceKind.TextureReadWrite, ShaderStages.Vertex)));

            Texture t = GD.CreateTexture(TextureDescription.Texture2D(64, 64, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));
            TextureView tv = GD.CreateTextureView(t);

            Assert.Throws<GraphicsException>(() =>
            {
                ResourceSet set = GD.CreateResourceSet(new ResourceSetDescription(layout, tv));
            });
        }

        [TestMethod]
        public void ResourceSet_IncorrectBufferUsage_Fails()
        {
            ResourceLayout layout = GD.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("RWB0", ResourceKind.StructuredBufferReadWrite, ShaderStages.Vertex)));

            DeviceBuffer readOnlyBuffer = GD.CreateBuffer(new BufferDescription(1024, BufferUsage.UniformBuffer));

            Assert.Throws<GraphicsException>(() =>
            {
                ResourceSet set = GD.CreateResourceSet(new ResourceSetDescription(layout, readOnlyBuffer));
            });
        }

        [TestMethod]
        public void ResourceSet_TooFewOrTooManyElements_Fails()
        {
            ResourceLayout layout = GD.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("UB0", ResourceKind.UniformBuffer, ShaderStages.Vertex),
                new ResourceLayoutElementDescription("UB1", ResourceKind.UniformBuffer, ShaderStages.Vertex),
                new ResourceLayoutElementDescription("UB2", ResourceKind.UniformBuffer, ShaderStages.Vertex)));

            DeviceBuffer ub = GD.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer));

            Assert.Throws<GraphicsException>(() =>
            {
                GD.CreateResourceSet(new ResourceSetDescription(layout, ub));
            });

            Assert.Throws<GraphicsException>(() =>
            {
                GD.CreateResourceSet(new ResourceSetDescription(layout, ub, ub));
            });

            Assert.Throws<GraphicsException>(() =>
            {
                GD.CreateResourceSet(new ResourceSetDescription(layout, ub, ub, ub, ub));
            });

            Assert.Throws<GraphicsException>(() =>
            {
                GD.CreateResourceSet(new ResourceSetDescription(layout, ub, ub, ub, ub, ub));
            });
        }

        [TestMethod]
        public void ResourceSet_InvalidUniformOffset_Fails()
        {
            ResourceLayout layout = GD.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("UB0", ResourceKind.UniformBuffer, ShaderStages.Vertex)));

            DeviceBuffer buffer = GD.CreateBuffer(new BufferDescription(1024, BufferUsage.UniformBuffer));

            Assert.Throws<GraphicsException>(() =>
            {
                GD.CreateResourceSet(new ResourceSetDescription(layout,
                    new DeviceBufferRange(buffer, GD.UniformBufferMinOffsetAlignment - 1, 256)));
            });

            Assert.Throws<GraphicsException>(() =>
            {
                GD.CreateResourceSet(new ResourceSetDescription(layout,
                    new DeviceBufferRange(buffer, GD.UniformBufferMinOffsetAlignment + 1, 256)));
            });
        }

        [TestMethod]
        public void ResourceSet_NoPipelineBound_Fails()
        {
            ResourceLayout layout = GD.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("UB0", ResourceKind.UniformBuffer, ShaderStages.Vertex)));
            DeviceBuffer ub = GD.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer));


            ResourceSet rs = GD.CreateResourceSet(new ResourceSetDescription(layout, ub));

            using (var cbp = GD.CreateCommandBufferPool())
            using (var cb = cbp.CreateCommandBuffer())
            {
                cb.Begin();
                Assert.Throws<GraphicsException>(() => cb.SetGraphicsResourceSet(0, rs));
                cb.End();
            }
        }

        [TestMethod]
        public void ResourceSet_InvalidSlot_Fails()
        {
            DeviceBuffer infoBuffer = GD.CreateBuffer(new BufferDescription(16, BufferUsage.UniformBuffer));
            DeviceBuffer orthoBuffer = GD.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer));

            ShaderSetDescription shaderSet = new ShaderSetDescription(
                new VertexLayoutDescription[]
                {
                    new VertexLayoutDescription(
                        new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
                        new VertexElementDescription("Color_UInt", VertexElementSemantic.TextureCoordinate, VertexElementFormat.UInt4))
                },
                TestShaders.LoadVertexFragment(GD, "UIntVertexAttribs"));

            ResourceLayout layout = GD.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("InfoBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex),
                new ResourceLayoutElementDescription("Ortho", ResourceKind.UniformBuffer, ShaderStages.Vertex)));

            ResourceSet set = GD.CreateResourceSet(new ResourceSetDescription(layout, infoBuffer, orthoBuffer));

            GraphicsPipelineDescription gpd = new GraphicsPipelineDescription(
                BlendStateDescription.SingleOverrideBlend,
                DepthStencilStateDescription.Disabled,
                RasterizerStateDescription.Default,
                PrimitiveTopology.PointList,
                shaderSet,
                layout,
                new OutputDescription(null, new OutputAttachmentDescription(PixelFormat.B8_G8_R8_A8_UNorm)));

            Pipeline pipeline = GD.CreateGraphicsPipeline(gpd);

            using (var cbp = GD.CreateCommandBufferPool())
            using (var cb = cbp.CreateCommandBuffer())
            {
                cb.Begin();
                cb.SetPipeline(pipeline);
                Assert.Throws<GraphicsException>(() => cb.SetGraphicsResourceSet(1, set));
                Assert.Throws<GraphicsException>(() => cb.SetGraphicsResourceSet(2, set));
                Assert.Throws<GraphicsException>(() => cb.SetGraphicsResourceSet(3, set));
                cb.End();
            }
        }

        [TestMethod]
        public void ResourceSet_IncompatibleSet_Fails()
        {
            DeviceBuffer infoBuffer = GD.CreateBuffer(new BufferDescription(16, BufferUsage.UniformBuffer));
            DeviceBuffer orthoBuffer = GD.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer));

            ShaderSetDescription shaderSet = new ShaderSetDescription(
                new VertexLayoutDescription[]
                {
                    new VertexLayoutDescription(
                        new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
                        new VertexElementDescription("Color_UInt", VertexElementSemantic.TextureCoordinate, VertexElementFormat.UInt4))
                },
                TestShaders.LoadVertexFragment(GD, "UIntVertexAttribs"));

            ResourceLayout layout = GD.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("InfoBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex),
                new ResourceLayoutElementDescription("Ortho", ResourceKind.UniformBuffer, ShaderStages.Vertex)));

            ResourceLayout layout2 = GD.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("InfoBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex),
                new ResourceLayoutElementDescription("Tex", ResourceKind.TextureReadOnly, ShaderStages.Fragment)));

            ResourceLayout layout3 = GD.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("InfoBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex)));

            Texture tex = GD.CreateTexture(TextureDescription.Texture2D(16, 16, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Sampled));
            TextureView texView = GD.CreateTextureView(tex);

            ResourceSet set = GD.CreateResourceSet(new ResourceSetDescription(layout, infoBuffer, orthoBuffer));
            ResourceSet set2 = GD.CreateResourceSet(new ResourceSetDescription(layout2, infoBuffer, texView));
            ResourceSet set3 = GD.CreateResourceSet(new ResourceSetDescription(layout3, infoBuffer));

            GraphicsPipelineDescription gpd = new GraphicsPipelineDescription(
                BlendStateDescription.SingleOverrideBlend,
                DepthStencilStateDescription.Disabled,
                RasterizerStateDescription.Default,
                PrimitiveTopology.PointList,
                shaderSet,
                layout,
                new OutputDescription(null, new OutputAttachmentDescription(PixelFormat.B8_G8_R8_A8_UNorm)));

            Pipeline pipeline = GD.CreateGraphicsPipeline(gpd);

            using (var cbp = GD.CreateCommandBufferPool())
            using (var cb = cbp.CreateCommandBuffer())
            {
                cb.Begin();
                cb.SetPipeline(pipeline);
                cb.SetGraphicsResourceSet(0, set);
                Assert.Throws<GraphicsException>(() => cb.SetGraphicsResourceSet(0, set2)); // Wrong type
                Assert.Throws<GraphicsException>(() => cb.SetGraphicsResourceSet(0, set3)); // Wrong count
                cb.End();
            }
        }
    }
}

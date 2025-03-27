using System;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace XenoAtom.Graphics.Tests
{
    [TestClass]
    public class PipelineTests : GraphicsDeviceTestBase
    {
        [TestMethod]
        public void CreatePipelines_DifferentInstanceStepRate_Succeeds()
        {
            Texture colorTex = GD.CreateTexture(TextureDescription.Texture2D(1, 1, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.RenderTarget));
            Framebuffer framebuffer = GD.CreateFramebuffer(new FramebufferDescription(null, colorTex));

            ShaderSetDescription shaderSet = new ShaderSetDescription(
                new VertexLayoutDescription[]
                {
                    new VertexLayoutDescription(
                        24,
                        0,
                        new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
                        new VertexElementDescription("Color_UInt", VertexElementSemantic.TextureCoordinate, VertexElementFormat.UInt4))
                },
                TestShaders.LoadVertexFragment(GD, "UIntVertexAttribs"));

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
                framebuffer.OutputDescription);

            Pipeline pipeline1 = GD.CreateGraphicsPipeline(gpd);
            Pipeline pipeline2 = GD.CreateGraphicsPipeline(gpd);

            gpd.ShaderSet.VertexLayouts[0].InstanceStepRate = 4;
            Pipeline pipeline3 = GD.CreateGraphicsPipeline(gpd);

            gpd.ShaderSet.VertexLayouts[0].InstanceStepRate = 5;
            Pipeline pipeline4 = GD.CreateGraphicsPipeline(gpd);
        }
    }
}

using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace XenoAtom.Graphics.Tests
{

    [TestClass]
    public class VertexLayoutTests : GraphicsDeviceTestBase
    {
        [DataTestMethod]
        [DataRow(0U, 0u, 0u, 0u, -1, true)]
        [DataRow(0U, 12u, 28u, 36u, -1, true)]
        [DataRow(0U, 16u, 32u, 48u, -1, true)]
        [DataRow(0U, 16u, 32u, 48u, 64, true)]
        [DataRow(0U, 16u, 32u, 48u, 128, true)]
        [DataRow(0U, 16u, 32u, 48u, 49, false)]
        [DataRow(0U, 12u, 12u, 12u, -1, false)]
        [DataRow(0U, 12u, 0u, 36u, -1, false)]
        [DataRow(0U, 12u, 28u, 35u, -1, false)]
        public void ExplicitOffsets(uint firstOffset, uint secondOffset, uint thirdOffset, uint fourthOffset, int stride, bool succeeds)
        {
            Texture outTex = GD.CreateTexture(
                TextureDescription.Texture2D(1, 1, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.RenderTarget));
            Framebuffer fb = GD.CreateFramebuffer(new FramebufferDescription(null, outTex));

            VertexLayoutDescription vertexLayoutDesc = new VertexLayoutDescription(
                new VertexElementDescription("A_V3", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3, firstOffset),
                new VertexElementDescription("B_V4", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4, secondOffset),
                new VertexElementDescription("C_V2", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2, thirdOffset),
                new VertexElementDescription("D_V4", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4, fourthOffset));

            if (stride > 0)
            {
                vertexLayoutDesc.Stride = (uint)stride;
            }

            ShaderSetDescription shaderSet = new ShaderSetDescription(
                new VertexLayoutDescription[]
                {
                    vertexLayoutDesc
                },
                TestShaders.LoadVertexFragment(GD, "VertexLayoutTestShader"));

            try
            {
                GD.CreateGraphicsPipeline(new GraphicsPipelineDescription(
                    BlendStateDescription.SingleOverrideBlend,
                    DepthStencilStateDescription.Disabled,
                    RasterizerStateDescription.Default,
                    PrimitiveTopology.TriangleList,
                    shaderSet,
                    Array.Empty<ResourceLayout>(),
                    fb.OutputDescription));
            }
            catch when (!succeeds) { }
        }

        protected VertexLayoutTests() : base()
        {
        }
    }

    [TestClass]
    public class VulkanVertexLayoutTests : VertexLayoutTests
    {
        public VulkanVertexLayoutTests()
        {
        }
    }
}

using System;
using Xunit;
using Xunit.Abstractions;

namespace XenoAtom.Graphics.Tests
{

    public abstract class VertexLayoutTests : GraphicsDeviceTestBase
    {
        [Theory]
        [InlineData(0, 0, 0, 0, -1, true)]
        [InlineData(0, 12, 28, 36, -1, true)]
        [InlineData(0, 16, 32, 48, -1, true)]
        [InlineData(0, 16, 32, 48, 64, true)]
        [InlineData(0, 16, 32, 48, 128, true)]
        [InlineData(0, 16, 32, 48, 49, false)]
        [InlineData(0, 12, 12, 12, -1, false)]
        [InlineData(0, 12, 0, 36, -1, false)]
        [InlineData(0, 12, 28, 35, -1, false)]
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

        protected VertexLayoutTests(ITestOutputHelper textOutputHelper) : base(textOutputHelper)
        {
        }
    }

    [Trait("Backend", "Vulkan")]
    public class VulkanVertexLayoutTests : VertexLayoutTests
    {
        public VulkanVertexLayoutTests(ITestOutputHelper textOutputHelper) : base(textOutputHelper)
        {
        }
    }
}

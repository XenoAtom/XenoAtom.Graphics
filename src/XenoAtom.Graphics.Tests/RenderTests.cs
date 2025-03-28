using System;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace XenoAtom.Graphics.Tests
{
    internal struct UIntVertexAttribsVertex
    {
        public Vector2 Position;
        public UInt4 Color_Int;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct UIntVertexAttribsInfo
    {
        public uint ColorNormalizationFactor;
        private float padding0;
        private float padding1;
        private float padding2;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ColoredVertex
    {
        public Vector4 Color;
        public Vector2 Position;
        private Vector2 _padding0;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TestVertex
    {
        public Vector3 A_V3;
        public Vector4 B_V4;
        public Vector2 C_V2;
        public Vector4 D_V4;
    }

    [TestClass]
    public class RenderTests : GraphicsDeviceTestBase
    {
        [TestMethod]
        public void Points_WithUIntColor()
        {
            Texture target = GD.CreateTexture(TextureDescription.Texture2D(
                50, 50, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.RenderTarget));
            Texture staging = GD.CreateTexture(TextureDescription.Texture2D(
                50, 50, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Staging));

            Framebuffer framebuffer = GD.CreateFramebuffer(new FramebufferDescription(null, target));

            DeviceBuffer infoBuffer = GD.CreateBuffer(new BufferDescription(16, BufferUsage.UniformBuffer));
            DeviceBuffer orthoBuffer = GD.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer));
            Matrix4x4 orthoMatrix = Matrix4x4.CreateOrthographicOffCenter(
                0,
                framebuffer.Width,
                framebuffer.Height,
                0,
                -1,
                1);
            GD.UpdateBuffer(orthoBuffer, 0, orthoMatrix);

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
                framebuffer.OutputDescription);

            Pipeline pipeline = GD.CreateGraphicsPipeline(gpd);

            uint colorNormalizationFactor = 2500;

            UIntVertexAttribsVertex[] vertices = new UIntVertexAttribsVertex[]
            {
                new UIntVertexAttribsVertex
                {
                    Position = new Vector2(0.5f, 0.5f),
                    Color_Int = new UInt4
                    {
                        X = (uint)(0.25f * colorNormalizationFactor),
                        Y = (uint)(0.5f * colorNormalizationFactor),
                        Z = (uint)(0.75f * colorNormalizationFactor),
                    }
                },
                new UIntVertexAttribsVertex
                {
                    Position = new Vector2(10.5f, 12.5f),
                    Color_Int = new UInt4
                    {
                        X = (uint)(0.25f * colorNormalizationFactor),
                        Y = (uint)(0.5f * colorNormalizationFactor),
                        Z = (uint)(0.75f * colorNormalizationFactor),
                    }
                },
                new UIntVertexAttribsVertex
                {
                    Position = new Vector2(25.5f, 35.5f),
                    Color_Int = new UInt4
                    {
                        X = (uint)(0.75f * colorNormalizationFactor),
                        Y = (uint)(0.5f * colorNormalizationFactor),
                        Z = (uint)(0.25f * colorNormalizationFactor),
                    }
                },
                new UIntVertexAttribsVertex
                {
                    Position = new Vector2(49.5f, 49.5f),
                    Color_Int = new UInt4
                    {
                        X = (uint)(0.15f * colorNormalizationFactor),
                        Y = (uint)(0.25f * colorNormalizationFactor),
                        Z = (uint)(0.35f * colorNormalizationFactor),
                    }
                },
            };

            DeviceBuffer vb = GD.CreateBuffer(
                new BufferDescription((uint)(Unsafe.SizeOf<UIntVertexAttribsVertex>() * vertices.Length), BufferUsage.VertexBuffer));
            GD.UpdateBuffer(vb, 0, vertices);
            GD.UpdateBuffer(infoBuffer, 0, new UIntVertexAttribsInfo { ColorNormalizationFactor = colorNormalizationFactor });

            using (var cbp = GD.CreateCommandBufferPool())
            using (var cb = cbp.CreateCommandBuffer())
            {
                cb.Begin();
                cb.SetFramebuffer(framebuffer);
                cb.SetFullViewports();
                cb.SetFullScissorRects();
                cb.ClearColorTarget(0, RgbaFloat.Black);
                cb.SetPipeline(pipeline);
                cb.SetVertexBuffer(0, vb);
                cb.SetGraphicsResourceSet(0, set);
                cb.Draw((uint)vertices.Length);
                cb.CopyTexture(target, staging);
                cb.End();
                GD.SubmitCommands(cb);
                GD.WaitForIdle();
            }

            MappedResourceView<RgbaFloat> readView = GD.Map<RgbaFloat>(staging, MapMode.Read);

            foreach (UIntVertexAttribsVertex vertex in vertices)
            {
                uint x = (uint)vertex.Position.X;
                uint y = (uint)vertex.Position.Y;
                if (!GD.IsUvOriginTopLeft || GD.IsClipSpaceYInverted)
                {
                    y = framebuffer.Height - y - 1;
                }

                RgbaFloat expectedColor = new RgbaFloat(
                    vertex.Color_Int.X / (float)colorNormalizationFactor,
                    vertex.Color_Int.Y / (float)colorNormalizationFactor,
                    vertex.Color_Int.Z / (float)colorNormalizationFactor,
                    1);
                Assert.AreEqual(expectedColor, readView[x, y], RgbaFloatFuzzyComparer.Instance);
            }
            GD.Unmap(staging);
        }

        [TestMethod]
        public void Points_WithUShortNormColor()
        {
            Texture target = GD.CreateTexture(TextureDescription.Texture2D(
                50, 50, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.RenderTarget));
            Texture staging = GD.CreateTexture(TextureDescription.Texture2D(
                50, 50, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Staging));

            Framebuffer framebuffer = GD.CreateFramebuffer(new FramebufferDescription(null, target));

            DeviceBuffer orthoBuffer = GD.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer));
            Matrix4x4 orthoMatrix = Matrix4x4.CreateOrthographicOffCenter(
                0,
                framebuffer.Width,
                framebuffer.Height,
                0,
                -1,
                1);
            GD.UpdateBuffer(orthoBuffer, 0, orthoMatrix);

            ShaderSetDescription shaderSet = new ShaderSetDescription(
                new VertexLayoutDescription[]
                {
                    new VertexLayoutDescription(
                        new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
                        new VertexElementDescription("Color", VertexElementSemantic.TextureCoordinate, VertexElementFormat.UShort4_Norm))
                },
                TestShaders.LoadVertexFragment(GD, "U16NormVertexAttribs"));

            ResourceLayout layout = GD.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("Ortho", ResourceKind.UniformBuffer, ShaderStages.Vertex)));

            ResourceSet set = GD.CreateResourceSet(new ResourceSetDescription(layout, orthoBuffer));

            GraphicsPipelineDescription gpd = new GraphicsPipelineDescription(
                BlendStateDescription.SingleOverrideBlend,
                DepthStencilStateDescription.Disabled,
                RasterizerStateDescription.Default,
                PrimitiveTopology.PointList,
                shaderSet,
                layout,
                framebuffer.OutputDescription);

            Pipeline pipeline = GD.CreateGraphicsPipeline(gpd);

            VertexCPU_UShortNorm[] vertices = new VertexCPU_UShortNorm[]
            {
                new VertexCPU_UShortNorm
                {
                    Position = new Vector2(0.5f, 0.5f),
                    R = UShortNorm(0.25f),
                    G = UShortNorm(0.5f),
                    B = UShortNorm(0.75f),
                },
                new VertexCPU_UShortNorm
                {
                    Position = new Vector2(10.5f, 12.5f),
                    R = UShortNorm(0.25f),
                    G = UShortNorm(0.5f),
                    B = UShortNorm(0.75f),
                },
                new VertexCPU_UShortNorm
                {
                    Position = new Vector2(25.5f, 35.5f),
                    R = UShortNorm(0.75f),
                    G = UShortNorm(0.5f),
                    B = UShortNorm(0.25f),
                },
                new VertexCPU_UShortNorm
                {
                    Position = new Vector2(49.5f, 49.5f),
                    R = UShortNorm(0.15f),
                    G = UShortNorm(0.25f),
                    B = UShortNorm(0.35f),
                },
            };

            DeviceBuffer vb = GD.CreateBuffer(
                new BufferDescription((uint)(Unsafe.SizeOf<VertexCPU_UShortNorm>() * vertices.Length), BufferUsage.VertexBuffer));
            GD.UpdateBuffer(vb, 0, vertices);

            using (var cbp = GD.CreateCommandBufferPool())
            using (var cb = cbp.CreateCommandBuffer())
            {
                cb.Begin();
                cb.SetFramebuffer(framebuffer);
                cb.SetFullViewports();
                cb.SetFullScissorRects();
                cb.ClearColorTarget(0, RgbaFloat.Black);
                cb.SetPipeline(pipeline);
                cb.SetVertexBuffer(0, vb);
                cb.SetGraphicsResourceSet(0, set);
                cb.Draw((uint)vertices.Length);
                cb.CopyTexture(target, staging);
                cb.End();
                GD.SubmitCommands(cb);
                GD.WaitForIdle();
            }

            MappedResourceView<RgbaFloat> readView = GD.Map<RgbaFloat>(staging, MapMode.Read);

            foreach (VertexCPU_UShortNorm vertex in vertices)
            {
                uint x = (uint)vertex.Position.X;
                uint y = (uint)vertex.Position.Y;
                if (!GD.IsUvOriginTopLeft || GD.IsClipSpaceYInverted)
                {
                    y = framebuffer.Height - y - 1;
                }

                RgbaFloat expectedColor = new RgbaFloat(
                    vertex.R / (float)ushort.MaxValue,
                    vertex.G / (float)ushort.MaxValue,
                    vertex.B / (float)ushort.MaxValue,
                    1);
                Assert.AreEqual(expectedColor, readView[x, y], RgbaFloatFuzzyComparer.Instance);
            }
            GD.Unmap(staging);
        }

        public struct VertexCPU_UShortNorm
        {
            public Vector2 Position;
            public ushort R;
            public ushort G;
            public ushort B;
            public ushort A;
        }

        public struct VertexCPU_UShort
        {
            public Vector2 Position;
            public ushort R;
            public ushort G;
            public ushort B;
            public ushort A;
        }

        private ushort UShortNorm(float normalizedValue)
        {
            Debug.Assert(normalizedValue >= 0 && normalizedValue <= 1);
            return (ushort)(normalizedValue * ushort.MaxValue);
        }

        [TestMethod]
        public void Points_WithUShortColor()
        {
            Texture target = GD.CreateTexture(TextureDescription.Texture2D(
                50, 50, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.RenderTarget));
            Texture staging = GD.CreateTexture(TextureDescription.Texture2D(
                50, 50, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Staging));

            Framebuffer framebuffer = GD.CreateFramebuffer(new FramebufferDescription(null, target));

            DeviceBuffer infoBuffer = GD.CreateBuffer(new BufferDescription(16, BufferUsage.UniformBuffer));
            DeviceBuffer orthoBuffer = GD.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer));
            Matrix4x4 orthoMatrix = Matrix4x4.CreateOrthographicOffCenter(
                0,
                framebuffer.Width,
                framebuffer.Height,
                0,
                -1,
                1);
            GD.UpdateBuffer(orthoBuffer, 0, orthoMatrix);

            ShaderSetDescription shaderSet = new ShaderSetDescription(
                new VertexLayoutDescription[]
                {
                    new VertexLayoutDescription(
                        new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
                        new VertexElementDescription("Color_UInt", VertexElementSemantic.TextureCoordinate, VertexElementFormat.UShort4))
                },
                TestShaders.LoadVertexFragment(GD, "U16VertexAttribs"));

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
                framebuffer.OutputDescription);

            Pipeline pipeline = GD.CreateGraphicsPipeline(gpd);

            uint colorNormalizationFactor = 2500;

            VertexCPU_UShort[] vertices = new VertexCPU_UShort[]
            {
                new VertexCPU_UShort
                {
                    Position = new Vector2(0.5f, 0.5f),
                    R = (ushort)(0.25f * colorNormalizationFactor),
                    G = (ushort)(0.5f * colorNormalizationFactor),
                    B = (ushort)(0.75f * colorNormalizationFactor),
                },
                new VertexCPU_UShort
                {
                    Position = new Vector2(10.5f, 12.5f),
                    R = (ushort)(0.25f * colorNormalizationFactor),
                    G = (ushort)(0.5f * colorNormalizationFactor),
                    B = (ushort)(0.75f * colorNormalizationFactor),
                },
                new VertexCPU_UShort
                {
                    Position = new Vector2(25.5f, 35.5f),
                    R = (ushort)(0.75f * colorNormalizationFactor),
                    G = (ushort)(0.5f * colorNormalizationFactor),
                    B = (ushort)(0.25f * colorNormalizationFactor),
                },
                new VertexCPU_UShort
                {
                    Position = new Vector2(49.5f, 49.5f),
                    R = (ushort)(0.15f * colorNormalizationFactor),
                    G = (ushort)(0.2f * colorNormalizationFactor),
                    B = (ushort)(0.35f * colorNormalizationFactor),
                },
            };

            DeviceBuffer vb = GD.CreateBuffer(
                new BufferDescription((uint)(Unsafe.SizeOf<UIntVertexAttribsVertex>() * vertices.Length), BufferUsage.VertexBuffer));
            GD.UpdateBuffer(vb, 0, vertices);
            GD.UpdateBuffer(infoBuffer, 0, new UIntVertexAttribsInfo { ColorNormalizationFactor = colorNormalizationFactor });

            using (var cbp = GD.CreateCommandBufferPool())
            using (var cb = cbp.CreateCommandBuffer())
            {
                cb.Begin();
                cb.SetFramebuffer(framebuffer);
                cb.SetFullViewports();
                cb.SetFullScissorRects();
                cb.ClearColorTarget(0, RgbaFloat.Black);
                cb.SetPipeline(pipeline);
                cb.SetVertexBuffer(0, vb);
                cb.SetGraphicsResourceSet(0, set);
                cb.Draw((uint)vertices.Length);
                cb.CopyTexture(target, staging);
                cb.End();
                GD.SubmitCommands(cb);
                GD.WaitForIdle();
            }

            MappedResourceView<RgbaFloat> readView = GD.Map<RgbaFloat>(staging, MapMode.Read);

            foreach (VertexCPU_UShort vertex in vertices)
            {
                uint x = (uint)vertex.Position.X;
                uint y = (uint)vertex.Position.Y;
                if (!GD.IsUvOriginTopLeft || GD.IsClipSpaceYInverted)
                {
                    y = framebuffer.Height - y - 1;
                }

                RgbaFloat expectedColor = new RgbaFloat(
                    vertex.R / (float)colorNormalizationFactor,
                    vertex.G / (float)colorNormalizationFactor,
                    vertex.B / (float)colorNormalizationFactor,
                    1);
                Assert.AreEqual(expectedColor, readView[x, y], RgbaFloatFuzzyComparer.Instance);
            }
            GD.Unmap(staging);
        }

        [TestMethod]
        public void Points_WithFloat16Color()
        {
            Texture target = GD.CreateTexture(TextureDescription.Texture2D(
                50, 50, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.RenderTarget));
            Texture staging = GD.CreateTexture(TextureDescription.Texture2D(
                50, 50, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Staging));

            Framebuffer framebuffer = GD.CreateFramebuffer(new FramebufferDescription(null, target));

            DeviceBuffer infoBuffer = GD.CreateBuffer(new BufferDescription(16, BufferUsage.UniformBuffer));
            DeviceBuffer orthoBuffer = GD.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer));
            Matrix4x4 orthoMatrix = Matrix4x4.CreateOrthographicOffCenter(
                0,
                framebuffer.Width,
                framebuffer.Height,
                0,
                -1,
                1);
            GD.UpdateBuffer(orthoBuffer, 0, orthoMatrix);

            ShaderSetDescription shaderSet = new ShaderSetDescription(
                new VertexLayoutDescription[]
                {
                    new VertexLayoutDescription(
                        new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
                        new VertexElementDescription("Color_Half", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Half4))
                },
                TestShaders.LoadVertexFragment(GD, "F16VertexAttribs"));

            ResourceLayout layout = GD.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("InfoBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex),
                new ResourceLayoutElementDescription("OrthoBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex)));

            ResourceSet set = GD.CreateResourceSet(new ResourceSetDescription(layout, infoBuffer, orthoBuffer));

            GraphicsPipelineDescription gpd = new GraphicsPipelineDescription(
                BlendStateDescription.SingleOverrideBlend,
                DepthStencilStateDescription.Disabled,
                RasterizerStateDescription.Default,
                PrimitiveTopology.PointList,
                shaderSet,
                layout,
                framebuffer.OutputDescription);

            Pipeline pipeline = GD.CreateGraphicsPipeline(gpd);

            uint colorNormalizationFactor = 2500;

            const ushort f16_375 = 0x5DDC; // 375.0
            const ushort f16_500 = 0x5FD0; // 500.0
            const ushort f16_625 = 0x60E2; // 625.0
            const ushort f16_875 = 0x62D6; // 875.0
            const ushort f16_1250 = 0x64E2; // 1250.0
            const ushort f16_1875 = 0x6753; // 1875.0

            VertexCPU_UShort[] vertices = new VertexCPU_UShort[]
            {
                new VertexCPU_UShort
                {
                    Position = new Vector2(0.5f, 0.5f),
                    R = f16_625,
                    G = f16_1250,
                    B = f16_1875,
                },
                new VertexCPU_UShort
                {
                    Position = new Vector2(10.5f, 12.5f),
                    R = f16_625,
                    G = f16_1250,
                    B = f16_1875,
                },
                new VertexCPU_UShort
                {
                    Position = new Vector2(25.5f, 35.5f),
                    R = f16_1875,
                    G = f16_1250,
                    B = f16_625,
                },
                new VertexCPU_UShort
                {
                    Position = new Vector2(49.5f, 49.5f),
                    R = f16_375,
                    G = f16_500,
                    B = f16_875,
                },
            };

            RgbaFloat[] expectedColors = new[]
            {
                new RgbaFloat(
                    625.0f / colorNormalizationFactor,
                    1250.0f / colorNormalizationFactor,
                    1875.0f / colorNormalizationFactor,
                    1),
                new RgbaFloat(
                    625.0f / colorNormalizationFactor,
                    1250.0f / colorNormalizationFactor,
                    1875.0f / colorNormalizationFactor,
                    1),
                new RgbaFloat(
                    1875.0f / colorNormalizationFactor,
                    1250.0f / colorNormalizationFactor,
                    625.0f / colorNormalizationFactor,
                    1),
                new RgbaFloat(
                    375.0f / colorNormalizationFactor,
                    500.0f / colorNormalizationFactor,
                    875.0f / colorNormalizationFactor,
                    1),
            };

            DeviceBuffer vb = GD.CreateBuffer(
                new BufferDescription((uint)(Unsafe.SizeOf<UIntVertexAttribsVertex>() * vertices.Length), BufferUsage.VertexBuffer));
            GD.UpdateBuffer(vb, 0, vertices);
            GD.UpdateBuffer(infoBuffer, 0, new UIntVertexAttribsInfo { ColorNormalizationFactor = colorNormalizationFactor });

            using (var cbp = GD.CreateCommandBufferPool())
            using (var cb = cbp.CreateCommandBuffer())
            {
                cb.Begin();
                cb.SetFramebuffer(framebuffer);
                cb.SetFullViewports();
                cb.SetFullScissorRects();
                cb.ClearColorTarget(0, RgbaFloat.Black);
                cb.SetPipeline(pipeline);
                cb.SetVertexBuffer(0, vb);
                cb.SetGraphicsResourceSet(0, set);
                cb.Draw((uint)vertices.Length);
                cb.CopyTexture(target, staging);
                cb.End();
                GD.SubmitCommands(cb);
                GD.WaitForIdle();
            }

            MappedResourceView<RgbaFloat> readView = GD.Map<RgbaFloat>(staging, MapMode.Read);

            for (int i = 0; i < vertices.Length; i++)
            {
                VertexCPU_UShort vertex = vertices[i];
                uint x = (uint)vertex.Position.X;
                uint y = (uint)vertex.Position.Y;
                if (!GD.IsUvOriginTopLeft || GD.IsClipSpaceYInverted)
                {
                    y = framebuffer.Height - y - 1;
                }

                RgbaFloat expectedColor = expectedColors[i];
                Assert.AreEqual(expectedColor, readView[x, y], RgbaFloatFuzzyComparer.Instance);
            }
            GD.Unmap(staging);
        }

        [DataRow(false)]
        [DataRow(true)]
        [TestMethod]
        public unsafe void Points_WithTexture_UpdateUnrelated(bool useTextureView)
        {
            // This is a regression test for the case where a user modifies an unrelated texture
            // at a time after a ResourceSet containing a texture has been bound. The OpenGL
            // backend was caching texture state improperly, resulting in wrong textures being sampled.

            Texture target = GD.CreateTexture(TextureDescription.Texture2D(
                50, 50, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.RenderTarget));
            Texture staging = GD.CreateTexture(TextureDescription.Texture2D(
                50, 50, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Staging));

            Framebuffer framebuffer = GD.CreateFramebuffer(new FramebufferDescription(null, target));

            DeviceBuffer orthoBuffer = GD.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer));
            Matrix4x4 orthoMatrix = Matrix4x4.CreateOrthographicOffCenter(
                0,
                framebuffer.Width,
                framebuffer.Height,
                0,
                -1,
                1);
            GD.UpdateBuffer(orthoBuffer, 0, orthoMatrix);

            Texture sampledTexture = GD.CreateTexture(
                TextureDescription.Texture2D(1, 1, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Sampled));

            RgbaFloat white = RgbaFloat.White;
            GD.UpdateTexture(sampledTexture, (IntPtr)(&white), (uint)Unsafe.SizeOf<RgbaFloat>(), 0, 0, 0, 1, 1, 1, 0, 0);

            Texture shouldntBeSampledTexture = GD.CreateTexture(
                TextureDescription.Texture2D(1, 1, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Sampled));

            ShaderSetDescription shaderSet = new ShaderSetDescription(
                new VertexLayoutDescription[]
                {
                    new VertexLayoutDescription(
                        new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2))
                },
                TestShaders.LoadVertexFragment(GD, "TexturedPoints"));

            ResourceLayout layout = GD.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("Ortho", ResourceKind.UniformBuffer, ShaderStages.Vertex),
                new ResourceLayoutElementDescription("Tex", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("Smp", ResourceKind.Sampler, ShaderStages.Fragment)));

            ResourceSet set;
            if (useTextureView)
            {
                TextureView view = GD.CreateTextureView(sampledTexture);
                set = GD.CreateResourceSet(new ResourceSetDescription(layout, orthoBuffer, view, GD.PointSampler));
            }
            else
            {
                set = GD.CreateResourceSet(new ResourceSetDescription(layout, orthoBuffer, sampledTexture, GD.PointSampler));
            }

            GraphicsPipelineDescription gpd = new GraphicsPipelineDescription(
                BlendStateDescription.SingleOverrideBlend,
                DepthStencilStateDescription.Disabled,
                RasterizerStateDescription.Default,
                PrimitiveTopology.PointList,
                shaderSet,
                layout,
                framebuffer.OutputDescription);

            Pipeline pipeline = GD.CreateGraphicsPipeline(gpd);

            Vector2[] vertices = new Vector2[]
            {
                new Vector2(0.5f, 0.5f),
                new Vector2(15.5f, 15.5f),
                new Vector2(25.5f, 26.5f),
                new Vector2(3.5f, 25.5f),
            };

            DeviceBuffer vb = GD.CreateBuffer(
                new BufferDescription((uint)(Unsafe.SizeOf<Vector2>() * vertices.Length), BufferUsage.VertexBuffer));
            GD.UpdateBuffer(vb, 0, vertices);

            using (var cbp = GD.CreateCommandBufferPool(new(CommandBufferPoolFlags.CanResetCommandBuffer)))
            using (var cb = cbp.CreateCommandBuffer())
            {
                for (int i = 0; i < 2; i++)
                {
                    cb.Begin();
                    cb.SetFramebuffer(framebuffer);
                    cb.ClearColorTarget(0, RgbaFloat.Black);
                    cb.SetPipeline(pipeline);
                    cb.SetVertexBuffer(0, vb);
                    cb.SetGraphicsResourceSet(0, set);

                    // Modify an unrelated texture.
                    // This must have no observable effect on the next draw call.
                    RgbaFloat pink = RgbaFloat.Pink;
                    GD.UpdateTexture(shouldntBeSampledTexture,
                        (IntPtr)(&pink), (uint)Unsafe.SizeOf<RgbaFloat>(),
                        0, 0, 0,
                        1, 1, 1,
                        0, 0);

                    cb.Draw((uint)vertices.Length);
                    cb.End();
                    GD.SubmitCommands(cb);
                    GD.WaitForIdle();
                }

                cb.Begin();
                cb.CopyTexture(target, staging);
                cb.End();
                GD.SubmitCommands(cb);
                GD.WaitForIdle();
            }

            MappedResourceView<RgbaFloat> readView = GD.Map<RgbaFloat>(staging, MapMode.Read);

            foreach (Vector2 vertex in vertices)
            {
                uint x = (uint)vertex.X;
                uint y = (uint)vertex.Y;
                if (!GD.IsUvOriginTopLeft || GD.IsClipSpaceYInverted)
                {
                    y = framebuffer.Height - y - 1;
                }

                Assert.AreEqual(white, readView[x, y], RgbaFloatFuzzyComparer.Instance);
            }
            GD.Unmap(staging);
        }

        [TestMethod]
        public void ComputeGeneratedVertices()
        {
            if (!GD.Features.ComputeShader)
            {
                return;
            }

            uint width = 512;
            uint height = 512;
            Texture output = GD.CreateTexture(
                TextureDescription.Texture2D(width, height, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.RenderTarget));
            Framebuffer framebuffer = GD.CreateFramebuffer(new FramebufferDescription(null, output));

            uint vertexSize = (uint)Unsafe.SizeOf<ColoredVertex>();
            DeviceBuffer buffer = GD.CreateBuffer(new BufferDescription(
                vertexSize * 4,
                BufferUsage.StructuredBufferReadWrite,
                vertexSize,
                true));

            ResourceLayout computeLayout = GD.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("OutputVertices", ResourceKind.StructuredBufferReadWrite, ShaderStages.Compute)));
            ResourceSet computeSet = GD.CreateResourceSet(new ResourceSetDescription(computeLayout, buffer));

            Pipeline computePipeline = GD.CreateComputePipeline(new ComputePipelineDescription(
                TestShaders.LoadCompute(GD, "ComputeColoredQuadGenerator"),
                computeLayout));

            ResourceLayout graphicsLayout = GD.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("InputVertices", ResourceKind.StructuredBufferReadOnly, ShaderStages.Vertex)));
            ResourceSet graphicsSet = GD.CreateResourceSet(new ResourceSetDescription(graphicsLayout, buffer));

            Pipeline graphicsPipeline = GD.CreateGraphicsPipeline(new GraphicsPipelineDescription(
                BlendStateDescription.SingleOverrideBlend,
                DepthStencilStateDescription.Disabled,
                RasterizerStateDescription.Default,
                PrimitiveTopology.TriangleStrip,
                new ShaderSetDescription(
                    Array.Empty<VertexLayoutDescription>(),
                    TestShaders.LoadVertexFragment(GD, "ColoredQuadRenderer")),
                graphicsLayout,
                framebuffer.OutputDescription));

            using (var cbp = GD.CreateCommandBufferPool())
            using (var cb = cbp.CreateCommandBuffer())
            {
                cb.Begin();
                cb.SetPipeline(computePipeline);
                cb.SetComputeResourceSet(0, computeSet);
                cb.Dispatch(1, 1, 1);
                cb.SetFramebuffer(framebuffer);
                cb.ClearColorTarget(0, new RgbaFloat());
                cb.SetPipeline(graphicsPipeline);
                cb.SetGraphicsResourceSet(0, graphicsSet);
                cb.Draw(4);
                cb.End();
                GD.SubmitCommands(cb);
                GD.WaitForIdle();
            }

            Texture readback = GetReadback(output);
            MappedResourceView<RgbaFloat> readView = GD.Map<RgbaFloat>(readback, MapMode.Read);
            for (uint y = 0; y < height; y++)
                for (uint x = 0; x < width; x++)
                {
                    Assert.AreEqual(RgbaFloat.Red, readView[x, y]);
                }
            GD.Unmap(readback);
        }

        [TestMethod]
        public void ComputeGeneratedTexture()
        {
            if (!GD.Features.ComputeShader)
            {
                Assert.Inconclusive("Compute shaders are not supported on this device.");
                return;
            }
            
            uint width = 4;
            uint height = 1;
            TextureDescription texDesc = TextureDescription.Texture2D(
                width, height,
                1,
                1,
                PixelFormat.R32_G32_B32_A32_Float,
                TextureUsage.Sampled | TextureUsage.Storage);
            Texture computeOutput = GD.CreateTexture(texDesc);
            texDesc.Usage = TextureUsage.RenderTarget;
            Texture finalOutput = GD.CreateTexture(texDesc);
            Framebuffer framebuffer = GD.CreateFramebuffer(new FramebufferDescription(null, finalOutput));

            ResourceLayout computeLayout = GD.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("ComputeOutput", ResourceKind.TextureReadWrite, ShaderStages.Compute)));
            ResourceSet computeSet = GD.CreateResourceSet(new ResourceSetDescription(computeLayout, computeOutput));

            Pipeline computePipeline = GD.CreateComputePipeline(new ComputePipelineDescription(
                TestShaders.LoadCompute(GD, "ComputeTextureGenerator"),
                computeLayout));

            ResourceLayout graphicsLayout = GD.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("Input", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("InputSampler", ResourceKind.Sampler, ShaderStages.Fragment)));
            ResourceSet graphicsSet = GD.CreateResourceSet(new ResourceSetDescription(graphicsLayout, computeOutput, GD.PointSampler));

            Pipeline graphicsPipeline = GD.CreateGraphicsPipeline(new GraphicsPipelineDescription(
                BlendStateDescription.SingleOverrideBlend,
                DepthStencilStateDescription.Disabled,
                RasterizerStateDescription.CullNone,
                PrimitiveTopology.TriangleStrip,
                new ShaderSetDescription(
                    Array.Empty<VertexLayoutDescription>(),
                    TestShaders.LoadVertexFragment(GD, "FullScreenBlit")),
                graphicsLayout,
                framebuffer.OutputDescription));

            using (var cbp = GD.CreateCommandBufferPool())
            using (var cb = cbp.CreateCommandBuffer())
            {
                cb.Begin();
                cb.SetPipeline(computePipeline);
                cb.SetComputeResourceSet(0, computeSet);
                cb.Dispatch(1, 1, 1);
                cb.SetFramebuffer(framebuffer);
                cb.ClearColorTarget(0, new RgbaFloat());
                cb.SetPipeline(graphicsPipeline);
                cb.SetGraphicsResourceSet(0, graphicsSet);
                cb.Draw(4);
                cb.End();
                GD.SubmitCommands(cb);
                GD.WaitForIdle();
            }

            Texture readback = GetReadback(finalOutput);
            MappedResourceView<RgbaFloat> readView = GD.Map<RgbaFloat>(readback, MapMode.Read);
            Assert.AreEqual(RgbaFloat.Red, readView[0, 0]);
            Assert.AreEqual(RgbaFloat.Green, readView[1, 0]);
            Assert.AreEqual(RgbaFloat.Blue, readView[2, 0]);
            Assert.AreEqual(RgbaFloat.White, readView[3, 0]);
            GD.Unmap(readback);
        }

        [TestMethod]
        [DataRow(2u)]
        [DataRow(6u)]
        public void ComputeBindTextureWithArrayLayersAsWriteable(uint ArrayLayers)
        {
            if (!GD.Features.ComputeShader)
            {
                Assert.Inconclusive("Compute shaders are not supported on this device.");
                return;
            }

            uint TexSize = 32;
            uint MipLevels = 1;
            TextureDescription texDesc = TextureDescription.Texture2D(
                TexSize, TexSize,
                MipLevels,
                ArrayLayers,
                PixelFormat.R8_G8_B8_A8_UNorm,
                TextureUsage.Sampled | TextureUsage.Storage);
            Texture computeOutput = GD.CreateTexture(texDesc);

            ResourceLayout computeLayout = GD.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("ComputeOutput", ResourceKind.TextureReadWrite, ShaderStages.Compute)));
            ResourceSet computeSet = GD.CreateResourceSet(new ResourceSetDescription(computeLayout, computeOutput));

            Pipeline computePipeline = GD.CreateComputePipeline(new ComputePipelineDescription(
                TestShaders.LoadCompute(GD, "ComputeImage2DArrayGenerator"),
                computeLayout));

            using (var cbp = GD.CreateCommandBufferPool())
            using (var cb = cbp.CreateCommandBuffer())
            {
                cb.Begin();
                cb.SetPipeline(computePipeline);
                cb.SetComputeResourceSet(0, computeSet);
                cb.Dispatch(TexSize / 32, TexSize / 32, ArrayLayers);
                cb.End();
                GD.SubmitCommands(cb);
                GD.WaitForIdle();
            }

            float sideColorStep = (float)Math.Floor(1.0f / ArrayLayers);
            Texture readback = GetReadback(computeOutput);

            foreach (var mip in Enumerable.Range(0, (int)MipLevels))
            {
                foreach (var layer in Enumerable.Range(0, (int)ArrayLayers))
                {
                    var subresource = readback.CalculateSubresource((uint)mip, (uint)layer);
                    var mipSize = TexSize >> mip;
                    var expectedColor = (byte)255.0f * ((layer + 1) * sideColorStep);
                    var map = GD.Map<byte>(readback, MapMode.Read, subresource);

                    foreach (var x in Enumerable.Range(0, (int)mipSize))
                    {
                        foreach (var y in Enumerable.Range(0, (int)mipSize))
                        {
                            Assert.AreEqual(map[x, y], expectedColor);
                        }
                    }

                    GD.Unmap(readback, subresource);
                }
            }
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void SampleTexture1D(bool arrayTexture)
        {
            if (!GD.Features.Texture1D) { return; }

            Texture target = GD.CreateTexture(TextureDescription.Texture2D(
                50, 50, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.RenderTarget));
            Texture staging = GD.CreateTexture(TextureDescription.Texture2D(
                50, 50, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Staging));

            Framebuffer framebuffer = GD.CreateFramebuffer(new FramebufferDescription(null, target));

            string SetName = arrayTexture ? "FullScreenTriSampleTextureArray" : "FullScreenTriSampleTexture";
            ShaderSetDescription shaderSet = new ShaderSetDescription(
                Array.Empty<VertexLayoutDescription>(),
                TestShaders.LoadVertexFragment(GD, SetName));

            uint layers = arrayTexture ? 10u : 1u;
            Texture tex1D = GD.CreateTexture(
                TextureDescription.Texture1D(128, 1, layers, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Sampled));
            RgbaFloat[] colors = new RgbaFloat[tex1D.Width];
            for (int i = 0; i < colors.Length; i++) { colors[i] = RgbaFloat.Pink; }
            GD.UpdateTexture(tex1D, colors, 0, 0, 0, tex1D.Width, 1, 1, 0, 0);

            ResourceLayout layout = GD.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("Tex", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("Smp", ResourceKind.Sampler, ShaderStages.Fragment)));

            ResourceSet set = GD.CreateResourceSet(new ResourceSetDescription(layout, tex1D, GD.PointSampler));

            GraphicsPipelineDescription gpd = new GraphicsPipelineDescription(
                BlendStateDescription.SingleOverrideBlend,
                DepthStencilStateDescription.Disabled,
                RasterizerStateDescription.CullNone,
                PrimitiveTopology.TriangleList,
                shaderSet,
                layout,
                framebuffer.OutputDescription);

            Pipeline pipeline = GD.CreateGraphicsPipeline(gpd);

            using (var cbp = GD.CreateCommandBufferPool())
            using (var cb = cbp.CreateCommandBuffer())
            {
                cb.Begin();
                cb.SetFramebuffer(framebuffer);
                cb.SetFullViewports();
                cb.SetFullScissorRects();
                cb.ClearColorTarget(0, RgbaFloat.Black);
                cb.SetPipeline(pipeline);
                cb.SetGraphicsResourceSet(0, set);
                cb.Draw(3);
                cb.CopyTexture(target, staging);
                cb.End();
                GD.SubmitCommands(cb);
                GD.WaitForIdle();
            }

            MappedResourceView<RgbaFloat> readView = GD.Map<RgbaFloat>(staging, MapMode.Read);
            for (int x = 0; x < staging.Width; x++)
            {
                Assert.AreEqual(RgbaFloat.Pink, readView[x, 0]);
            }
            GD.Unmap(staging);
        }

        [TestMethod]
        public void BindTextureAcrossMultipleDrawCalls()
        {
            using Texture target1 = GD.CreateTexture(TextureDescription.Texture2D(
                50, 50, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.RenderTarget));
            using Texture target2 = GD.CreateTexture(TextureDescription.Texture2D(
                50, 50, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.RenderTarget | TextureUsage.Sampled));
            using TextureView textureView = GD.CreateTextureView(target2);

            using Texture staging1 = GD.CreateTexture(TextureDescription.Texture2D(
                50, 50, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Staging));
            using Texture staging2 = GD.CreateTexture(TextureDescription.Texture2D(
                50, 50, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Staging));
            using Texture staging3 = GD.CreateTexture(TextureDescription.Texture2D(
                50, 50, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Staging));

            using Framebuffer framebuffer1 = GD.CreateFramebuffer(new FramebufferDescription(null, target1));
            using Framebuffer framebuffer2 = GD.CreateFramebuffer(new FramebufferDescription(null, target2));

            // This shader doesn't really matter, just as long as it is different to the first
            // and third render pass and also doesn't use any texture bindings
            ShaderSetDescription textureShaderSet = new ShaderSetDescription(
                Array.Empty<VertexLayoutDescription>(),
                TestShaders.LoadVertexFragment(GD, "FullScreenTriSampleTexture2D"));
            ShaderSetDescription quadShaderSet = new ShaderSetDescription(
                new VertexLayoutDescription[]
                {
                    new VertexLayoutDescription(
                        new VertexElementDescription("A_V3", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
                        new VertexElementDescription("B_V4", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4),
                        new VertexElementDescription("C_V2", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
                        new VertexElementDescription("D_V4", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4)
                    )
                },
                TestShaders.LoadVertexFragment(GD, "VertexLayoutTestShader"));

            using DeviceBuffer vertexBuffer = GD.CreateBuffer(new BufferDescription(
                (uint)Unsafe.SizeOf<TestVertex>() * 3,
                BufferUsage.VertexBuffer));
            GD.UpdateBuffer(vertexBuffer, 0, new[] {
                new TestVertex(),
                new TestVertex(),
                new TestVertex()
            });

            // Fill the second target with a known color
            RgbaFloat[] colors = new RgbaFloat[target2.Width * target2.Height];
            for (int i = 0; i < colors.Length; i++) { colors[i] = RgbaFloat.Pink; }
            GD.UpdateTexture(target2, colors, 0, 0, 0, target2.Width, target2.Height, 1, 0, 0);

            using ResourceLayout textureLayout = GD.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("Tex", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("Smp", ResourceKind.Sampler, ShaderStages.Fragment)));

            using ResourceSet textureSet = GD.CreateResourceSet(new ResourceSetDescription(textureLayout, textureView, GD.PointSampler));

            using Pipeline texturePipeline = GD.CreateGraphicsPipeline(new GraphicsPipelineDescription(
                BlendStateDescription.SingleOverrideBlend,
                DepthStencilStateDescription.Disabled,
                RasterizerStateDescription.CullNone,
                PrimitiveTopology.TriangleList,
                textureShaderSet,
                textureLayout,
                framebuffer1.OutputDescription));
            using Pipeline quadPipeline = GD.CreateGraphicsPipeline(new GraphicsPipelineDescription(
                BlendStateDescription.SingleOverrideBlend,
                DepthStencilStateDescription.Disabled,
                RasterizerStateDescription.CullNone,
                PrimitiveTopology.TriangleList,
                quadShaderSet,
                Array.Empty<ResourceLayout>(),
                framebuffer2.OutputDescription));

            using (var cbp = GD.CreateCommandBufferPool())
            using (var cb = cbp.CreateCommandBuffer())
            {
                cb.Begin();
                cb.SetFramebuffer(framebuffer1);
                cb.SetFullViewports();
                cb.SetFullScissorRects();

                // First pass using texture shader
                cb.SetPipeline(texturePipeline);
                cb.ClearColorTarget(0, RgbaFloat.Black);
                cb.SetGraphicsResourceSet(0, textureSet);
                cb.Draw(3);
                cb.CopyTexture(target1, staging1);

                //  Second pass using dummy shader
                cb.SetPipeline(quadPipeline);
                cb.SetFramebuffer(framebuffer2);
                cb.ClearColorTarget(0, RgbaFloat.Blue);
                cb.SetVertexBuffer(0, vertexBuffer);
                cb.Draw(3);
                cb.CopyTexture(target2, staging2);

                // Third pass using texture shader again
                cb.SetPipeline(texturePipeline);
                cb.SetFramebuffer(framebuffer1);
                cb.ClearColorTarget(0, RgbaFloat.Black);
                cb.SetGraphicsResourceSet(0, textureSet);
                cb.Draw(3);
                cb.CopyTexture(target1, staging3);

                cb.End();
                GD.SubmitCommands(cb);
                GD.WaitForIdle();
            }

            MappedResourceView<RgbaFloat> readView1 = GD.Map<RgbaFloat>(staging1, MapMode.Read);
            MappedResourceView<RgbaFloat> readView2 = GD.Map<RgbaFloat>(staging2, MapMode.Read);
            MappedResourceView<RgbaFloat> readView3 = GD.Map<RgbaFloat>(staging3, MapMode.Read);
            for (int x = 0; x < staging1.Width; x++)
            {
                Assert.AreEqual(RgbaFloat.Pink, readView1[x, 0]);
                Assert.AreEqual(RgbaFloat.Blue, readView2[x, 0]);
                Assert.AreEqual(RgbaFloat.Blue, readView3[x, 0]);
            }
            GD.Unmap(staging1);
            GD.Unmap(staging2);
            GD.Unmap(staging3);
        }

        [TestMethod]
        [DataRow(2u, 0u)]
        [DataRow(5u, 3u)]
        [DataRow(32u, 31u)]
        public void FramebufferArrayLayer(uint layerCount, uint targetLayer)
        {
            Texture target = GD.CreateTexture(TextureDescription.Texture2D(
                16, 16, 1, layerCount, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.RenderTarget));
            Framebuffer framebuffer = GD.CreateFramebuffer(
                new FramebufferDescription(
                    null,
                    new[] { new FramebufferAttachmentDescription(target, targetLayer) }));

            string setName = "FullScreenTriSampleTexture2D";
            ShaderSetDescription shaderSet = new ShaderSetDescription(
                Array.Empty<VertexLayoutDescription>(),
                TestShaders.LoadVertexFragment(GD, setName));

            Texture tex2D = GD.CreateTexture(
                TextureDescription.Texture2D(128, 128, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Sampled));
            RgbaFloat[] colors = new RgbaFloat[tex2D.Width * tex2D.Height];
            for (int i = 0; i < colors.Length; i++) { colors[i] = RgbaFloat.Pink; }
            GD.UpdateTexture(tex2D, colors, 0, 0, 0, tex2D.Width, 1, 1, 0, 0);

            ResourceLayout layout = GD.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("Tex", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("Smp", ResourceKind.Sampler, ShaderStages.Fragment)));

            ResourceSet set = GD.CreateResourceSet(new ResourceSetDescription(layout, tex2D, GD.PointSampler));

            GraphicsPipelineDescription gpd = new GraphicsPipelineDescription(
                BlendStateDescription.SingleOverrideBlend,
                DepthStencilStateDescription.Disabled,
                RasterizerStateDescription.CullNone,
                PrimitiveTopology.TriangleList,
                shaderSet,
                layout,
                framebuffer.OutputDescription);

            Pipeline pipeline = GD.CreateGraphicsPipeline(gpd);
            using (var cbp = GD.CreateCommandBufferPool())
            using (var cb = cbp.CreateCommandBuffer())
            {
                cb.Begin();
                cb.SetFramebuffer(framebuffer);
                cb.SetFullViewports();
                cb.SetFullScissorRects();
                cb.ClearColorTarget(0, RgbaFloat.Black);
                cb.SetPipeline(pipeline);
                cb.SetGraphicsResourceSet(0, set);
                cb.Draw(3);
                cb.End();
                GD.SubmitCommands(cb);
                GD.WaitForIdle();
            }

            Texture staging = GetReadback(target);
            MappedResourceView<RgbaFloat> readView = GD.Map<RgbaFloat>(staging, MapMode.Read, targetLayer);
            for (int x = 0; x < staging.Width; x++)
            {
                Assert.AreEqual(RgbaFloat.Pink, readView[x, 0]);
            }
            GD.Unmap(staging, targetLayer);
        }

        [TestMethod]
        [DataRow(1u, 0u, 0u)]
        [DataRow(1u, 0u, 3u)]
        [DataRow(1u, 0u, 5u)]
        [DataRow(4u, 2u, 0u)]
        [DataRow(4u, 2u, 3u)]
        [DataRow(4u, 2u, 5u)]
        public void RenderToCubemapFace(uint layerCount, uint targetLayer, uint targetFace)
        {
            Texture target = GD.CreateTexture(TextureDescription.Texture2D(
                16, 16,
                1, layerCount,
                PixelFormat.R32_G32_B32_A32_Float,
                TextureUsage.RenderTarget | TextureUsage.Cubemap));
            Framebuffer framebuffer = GD.CreateFramebuffer(
                new FramebufferDescription(
                    null,
                    new[] { new FramebufferAttachmentDescription(target, (targetLayer * 6) + targetFace) }));

            string setName = "FullScreenTriSampleTexture2D";
            ShaderSetDescription shaderSet = new ShaderSetDescription(
                Array.Empty<VertexLayoutDescription>(),
                TestShaders.LoadVertexFragment(GD, setName));

            Texture tex2D = GD.CreateTexture(
                TextureDescription.Texture2D(128, 128, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Sampled));
            RgbaFloat[] colors = new RgbaFloat[tex2D.Width * tex2D.Height];
            for (int i = 0; i < colors.Length; i++) { colors[i] = RgbaFloat.Pink; }
            GD.UpdateTexture(tex2D, colors, 0, 0, 0, tex2D.Width, 1, 1, 0, 0);

            ResourceLayout layout = GD.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("Tex", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("Smp", ResourceKind.Sampler, ShaderStages.Fragment)));

            ResourceSet set = GD.CreateResourceSet(new ResourceSetDescription(layout, tex2D, GD.PointSampler));

            GraphicsPipelineDescription gpd = new GraphicsPipelineDescription(
                BlendStateDescription.SingleOverrideBlend,
                DepthStencilStateDescription.Disabled,
                RasterizerStateDescription.CullNone,
                PrimitiveTopology.TriangleList,
                shaderSet,
                layout,
                framebuffer.OutputDescription);

            Pipeline pipeline = GD.CreateGraphicsPipeline(gpd);

            using (var cbp = GD.CreateCommandBufferPool())
            using (var cb = cbp.CreateCommandBuffer())
            {
                cb.Begin();
                cb.SetFramebuffer(framebuffer);
                cb.SetFullViewports();
                cb.SetFullScissorRects();
                cb.ClearColorTarget(0, RgbaFloat.Black);
                cb.SetPipeline(pipeline);
                cb.SetGraphicsResourceSet(0, set);
                cb.Draw(3);
                cb.End();
                GD.SubmitCommands(cb);
                GD.WaitForIdle();
            }

            Texture staging = GetReadback(target);
            MappedResourceView<RgbaFloat> readView = GD.Map<RgbaFloat>(staging, MapMode.Read, (targetLayer * 6) + targetFace);
            for (int x = 0; x < staging.Width; x++)
            {
                Assert.AreEqual(RgbaFloat.Pink, readView[x, 0]);
            }
            GD.Unmap(staging, (targetLayer * 6) + targetFace);
        }

        [TestMethod]
        public void WriteFragmentDepth()
        {
            Texture depthTarget = GD.CreateTexture(
                TextureDescription.Texture2D(64, 64, 1, 1, PixelFormat.R32_Float, TextureUsage.DepthStencil | TextureUsage.Sampled));
            Framebuffer framebuffer = GD.CreateFramebuffer(new FramebufferDescription(depthTarget));

            string setName = "FullScreenWriteDepth";
            ShaderSetDescription shaderSet = new ShaderSetDescription(
                Array.Empty<VertexLayoutDescription>(),
                TestShaders.LoadVertexFragment(GD, setName));

            ResourceLayout layout = GD.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("FramebufferInfo", ResourceKind.UniformBuffer, ShaderStages.Fragment)));

            DeviceBuffer ub = GD.CreateBuffer(new BufferDescription(16, BufferUsage.UniformBuffer));
            GD.UpdateBuffer(ub, 0, new Vector4(depthTarget.Width, depthTarget.Height, 0, 0));
            ResourceSet rs = GD.CreateResourceSet(new ResourceSetDescription(layout, ub));

            GraphicsPipelineDescription gpd = new GraphicsPipelineDescription(
                BlendStateDescription.SingleOverrideBlend,
                new DepthStencilStateDescription(true, true, ComparisonKind.Always),
                RasterizerStateDescription.CullNone,
                PrimitiveTopology.TriangleList,
                shaderSet,
                layout,
                framebuffer.OutputDescription);

            Pipeline pipeline = GD.CreateGraphicsPipeline(gpd);

            using (var cbp = GD.CreateCommandBufferPool())
            using (var cb = cbp.CreateCommandBuffer())
            {
                cb.Begin();
                cb.SetFramebuffer(framebuffer);
                cb.SetFullViewports();
                cb.SetFullScissorRects();
                cb.ClearDepthStencil(0f);
                cb.SetPipeline(pipeline);
                cb.SetGraphicsResourceSet(0, rs);
                cb.Draw(3);
                cb.End();
                GD.SubmitCommands(cb);
                GD.WaitForIdle();
            }

            Texture readback = GetReadback(depthTarget);

            MappedResourceView<float> readView = GD.Map<float>(readback, MapMode.Read);
            for (uint y = 0; y < readback.Height; y++)
            {
                for (uint x = 0; x < readback.Width; x++)
                {
                    float xComp = x;
                    float yComp = y * readback.Width;
                    float val = (yComp + xComp) / (readback.Width * readback.Height);

                    Assert.AreEqual(val, readView[x, y], 2.0f);
                }
            }
            GD.Unmap(readback);
        }

        [TestMethod]
        public void UseBlendFactor()
        {
            const uint width = 512;
            const uint height = 512;
            using var output = GD.CreateTexture(
                TextureDescription.Texture2D(width, height, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.RenderTarget));
            using var framebuffer = GD.CreateFramebuffer(new FramebufferDescription(null, output));

            var yMod = GD.IsClipSpaceYInverted ? -1.0f : 1.0f;
            var vertices = new[]
            {
                new ColoredVertex { Position = new Vector2(-1, 1 * yMod), Color = Vector4.One },
                new ColoredVertex { Position = new Vector2(1, 1 * yMod), Color = Vector4.One },
                new ColoredVertex { Position = new Vector2(-1, -1 * yMod), Color = Vector4.One },
                new ColoredVertex { Position = new Vector2(1, -1 * yMod), Color = Vector4.One }
            };
            uint vertexSize = (uint)Unsafe.SizeOf<ColoredVertex>();
            using var buffer = GD.CreateBuffer(new BufferDescription(
                vertexSize * (uint)vertices.Length,
                BufferUsage.StructuredBufferReadOnly,
                vertexSize,
                true));
            GD.UpdateBuffer(buffer, 0, vertices);

            using var graphicsLayout = GD.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("InputVertices", ResourceKind.StructuredBufferReadOnly, ShaderStages.Vertex)));
            using var graphicsSet = GD.CreateResourceSet(new ResourceSetDescription(graphicsLayout, buffer));

            var blendDesc = new BlendStateDescription
            {
                BlendFactor = new RgbaFloat(0.25f, 0.5f, 0.75f, 1),
                AttachmentStates = new[]
                {
                    new BlendAttachmentDescription
                    {
                        BlendEnabled = true,
                        SourceColorFactor = BlendFactor.BlendFactor,
                        DestinationColorFactor = BlendFactor.Zero,
                        ColorFunction = BlendFunction.Add,
                        SourceAlphaFactor = BlendFactor.BlendFactor,
                        DestinationAlphaFactor = BlendFactor.Zero,
                        AlphaFunction = BlendFunction.Add
                    }
                }
            };
            var pipelineDesc = new GraphicsPipelineDescription(
                blendDesc,
                DepthStencilStateDescription.Disabled,
                RasterizerStateDescription.Default,
                PrimitiveTopology.TriangleStrip,
                new ShaderSetDescription(
                    Array.Empty<VertexLayoutDescription>(),
                    TestShaders.LoadVertexFragment(GD, "ColoredQuadRenderer")),
                graphicsLayout,
                framebuffer.OutputDescription);

            using (var pipeline1 = GD.CreateGraphicsPipeline(pipelineDesc))
            using (var cbp = GD.CreateCommandBufferPool())
            using (var cb = cbp.CreateCommandBuffer())
            {
                cb.Begin();
                cb.SetFramebuffer(framebuffer);
                cb.ClearColorTarget(0, RgbaFloat.Clear);
                cb.SetPipeline(pipeline1);
                cb.SetGraphicsResourceSet(0, graphicsSet);
                cb.Draw((uint)vertices.Length);
                cb.End();
                GD.SubmitCommands(cb);
                GD.WaitForIdle();
            }

            using (var readback = GetReadback(output))
            {
                var readView = GD.Map<RgbaFloat>(readback, MapMode.Read);
                for (uint y = 0; y < height; y++)
                    for (uint x = 0; x < width; x++)
                    {
                        Assert.AreEqual(new RgbaFloat(0.25f, 0.5f, 0.75f, 1), readView[x, y]);
                    }
                GD.Unmap(readback);
            }

            blendDesc.BlendFactor = new RgbaFloat(0, 1, 0.5f, 0);
            blendDesc.AttachmentStates[0].DestinationColorFactor = BlendFactor.InverseBlendFactor;
            blendDesc.AttachmentStates[0].DestinationAlphaFactor = BlendFactor.InverseBlendFactor;
            pipelineDesc.BlendState = blendDesc;

            using (var pipeline2 = GD.CreateGraphicsPipeline(pipelineDesc))
            using (var cbp = GD.CreateCommandBufferPool())
            using (var cb = cbp.CreateCommandBuffer())
            {
                cb.Begin();
                cb.SetFramebuffer(framebuffer);
                cb.SetPipeline(pipeline2);
                cb.SetGraphicsResourceSet(0, graphicsSet);
                cb.Draw((uint)vertices.Length);
                cb.End();
                GD.SubmitCommands(cb);
                GD.WaitForIdle();
            }

            using (var readback = GetReadback(output))
            {
                var readView = GD.Map<RgbaFloat>(readback, MapMode.Read);
                for (uint y = 0; y < height; y++)
                    for (uint x = 0; x < width; x++)
                    {
                        Assert.AreEqual(new RgbaFloat(0.25f, 1, 0.875f, 1), readView[x, y]);
                    }
                GD.Unmap(readback);
            }
        }

        [TestMethod]
        public void UseColorWriteMask()
        {
            Texture output = GD.CreateTexture(
                TextureDescription.Texture2D(64, 64, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.RenderTarget));
            using var framebuffer = GD.CreateFramebuffer(new FramebufferDescription(null, output));

            var yMod = GD.IsClipSpaceYInverted ? -1.0f : 1.0f;
            var vertices = new[]
            {
                new ColoredVertex { Position = new Vector2(-1, 1 * yMod), Color = Vector4.One },
                new ColoredVertex { Position = new Vector2(1, 1 * yMod), Color = Vector4.One },
                new ColoredVertex { Position = new Vector2(-1, -1 * yMod), Color = Vector4.One },
                new ColoredVertex { Position = new Vector2(1, -1 * yMod), Color = Vector4.One }
            };
            uint vertexSize = (uint)Unsafe.SizeOf<ColoredVertex>();
            using var buffer = GD.CreateBuffer(new BufferDescription(
                vertexSize * (uint)vertices.Length,
                BufferUsage.StructuredBufferReadOnly,
                vertexSize,
                true));
            GD.UpdateBuffer(buffer, 0, vertices);

            using var graphicsLayout = GD.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("InputVertices", ResourceKind.StructuredBufferReadOnly, ShaderStages.Vertex)));
            using var graphicsSet = GD.CreateResourceSet(new ResourceSetDescription(graphicsLayout, buffer));

            var blendDesc = new BlendStateDescription
            {
                AttachmentStates = new[]
                {
                    new BlendAttachmentDescription
                    {
                        BlendEnabled = true,
                        SourceColorFactor = BlendFactor.One,
                        DestinationColorFactor = BlendFactor.Zero,
                        ColorFunction = BlendFunction.Add,
                        SourceAlphaFactor = BlendFactor.One,
                        DestinationAlphaFactor = BlendFactor.Zero,
                        AlphaFunction = BlendFunction.Add,
                    }
                },
            };

            var pipelineDesc = new GraphicsPipelineDescription(
                blendDesc,
                DepthStencilStateDescription.Disabled,
                RasterizerStateDescription.Default,
                PrimitiveTopology.TriangleStrip,
                new ShaderSetDescription(
                    Array.Empty<VertexLayoutDescription>(),
                    TestShaders.LoadVertexFragment(GD, "ColoredQuadRenderer")),
                graphicsLayout,
                framebuffer.OutputDescription);

            using (var pipeline1 = GD.CreateGraphicsPipeline(pipelineDesc))
            using (var cbp = GD.CreateCommandBufferPool())
            using (var cb = cbp.CreateCommandBuffer())
            {
                cb.Begin();
                cb.SetFramebuffer(framebuffer);
                cb.ClearColorTarget(0, RgbaFloat.Clear);
                cb.SetPipeline(pipeline1);
                cb.SetGraphicsResourceSet(0, graphicsSet);
                cb.Draw((uint)vertices.Length);
                cb.End();
                GD.SubmitCommands(cb);
                GD.WaitForIdle();
            }

            using (var readback = GetReadback(output))
            {
                var readView = GD.Map<RgbaFloat>(readback, MapMode.Read);
                for (uint y = 0; y < output.Height; y++)
                    for (uint x = 0; x < output.Width; x++)
                    {
                        Assert.AreEqual(RgbaFloat.White, readView[x, y]);
                    }

                GD.Unmap(readback);
            }

            foreach (var mask in Enum.GetValues<ColorWriteMask>())
            {
                blendDesc.AttachmentStates[0].ColorWriteMask = mask;
                pipelineDesc.BlendState = blendDesc;

                using (var maskedPipeline = GD.CreateGraphicsPipeline(pipelineDesc))
                using (var cbp = GD.CreateCommandBufferPool())
                using (var cb = cbp.CreateCommandBuffer())
                {
                    cb.Begin();
                    cb.SetFramebuffer(framebuffer);
                    cb.ClearColorTarget(0, new RgbaFloat(0.25f, 0.25f, 0.25f, 0.25f));
                    cb.SetPipeline(maskedPipeline);
                    cb.SetGraphicsResourceSet(0, graphicsSet);
                    cb.Draw((uint)vertices.Length);
                    cb.End();
                    GD.SubmitCommands(cb);
                    GD.WaitForIdle();
                }

                using (var readback = GetReadback(output))
                {
                    var readView = GD.Map<RgbaFloat>(readback, MapMode.Read);
                    for (uint y = 0; y < output.Height; y++)
                        for (uint x = 0; x < output.Width; x++)
                        {
                            Assert.AreEqual(mask.HasFlag(ColorWriteMask.Red) ? 1 : 0.25f, readView[x, y].R);
                            Assert.AreEqual(mask.HasFlag(ColorWriteMask.Green) ? 1 : 0.25f, readView[x, y].G);
                            Assert.AreEqual(mask.HasFlag(ColorWriteMask.Blue) ? 1 : 0.25f, readView[x, y].B);
                            Assert.AreEqual(mask.HasFlag(ColorWriteMask.Alpha) ? 1 : 0.25f, readView[x, y].A);
                        }
                    GD.Unmap(readback);
                }
            }
        }
    }
}

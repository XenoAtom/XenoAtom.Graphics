using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace XenoAtom.Graphics.Tests
{
    [StructLayout(LayoutKind.Sequential)]
    struct FillValueStruct
    {
        /// <summary>
        /// The value we fill the 3d texture with.
        /// </summary>
        public float FillValue;
        public float pad1, pad2, pad3;

        public FillValueStruct(float fillValue)
        {
            FillValue = fillValue;
            pad1 = pad2 = pad3 = 0;
        }
    }


    public abstract class ComputeTests : GraphicsDeviceTestBase
    {
        protected ComputeTests(ITestOutputHelper textOutputHelper) : base(textOutputHelper)
        {
        }

        [Fact]
        public void ComputeShader3dTexture()
        {
            const float FillValue = 42.42f;
            const uint OutputTextureSize = 32;

            using Shader computeShader = TestShaders.LoadCompute(GD, "ComputeShader3dTexture");
            using ResourceLayout computeLayout = GD.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("TextureToFill", ResourceKind.TextureReadWrite, ShaderStages.Compute),
                new ResourceLayoutElementDescription("FillValueBuffer", ResourceKind.UniformBuffer, ShaderStages.Compute)));

            using Pipeline computePipeline = GD.CreateComputePipeline(new ComputePipelineDescription(
                computeShader,
                computeLayout,
                16, 16, 1));

            using DeviceBuffer fillValueBuffer = GD.CreateBuffer(new BufferDescription((uint)Marshal.SizeOf<FillValueStruct>(), BufferUsage.UniformBuffer));

            // Create our output texture.
            using Texture computeTargetTexture = GD.CreateTexture(TextureDescription.Texture3D(
                OutputTextureSize,
                OutputTextureSize,
                OutputTextureSize,
                1,
                PixelFormat.R32_G32_B32_A32_Float,
                TextureUsage.Sampled | TextureUsage.Storage));

            using TextureView computeTargetTextureView = GD.CreateTextureView(computeTargetTexture);

            using ResourceSet computeResourceSet = GD.CreateResourceSet(new ResourceSetDescription(
                computeLayout,
                computeTargetTextureView,
                fillValueBuffer));

            using CommandList cl = GD.CreateCommandList();
            cl.Begin();

            cl.UpdateBuffer(fillValueBuffer, 0, new FillValueStruct(FillValue));

            // Use the compute shader to fill the texture.
            cl.SetPipeline(computePipeline);
            cl.SetComputeResourceSet(0, computeResourceSet);
            const uint GroupDivisorXY = 16;
            cl.Dispatch(OutputTextureSize / GroupDivisorXY, OutputTextureSize / GroupDivisorXY, OutputTextureSize);

            cl.End();
            GD.SubmitCommands(cl);
            GD.WaitForIdle();

            // Read back from our texture and make sure it has been properly filled.
            for (uint depth = 0; depth < computeTargetTexture.Depth; depth++)
            {
                RgbaFloat expectedFillValue = new RgbaFloat(new System.Numerics.Vector4(FillValue * (depth + 1)));
                int notFilledCount = CountTexelsNotFilledAtDepth(GD, computeTargetTexture, expectedFillValue, depth);

                Assert.Equal(0, notFilledCount);
            }
        }


        /// <summary>
        /// Returns the number of texels in the texture that DO NOT match the fill value.
        /// </summary>
        private int CountTexelsNotFilledAtDepth<TexelType>(GraphicsDevice device, Texture texture, TexelType fillValue, uint depth)
            where TexelType : unmanaged
        {
            // We need to create a staging texture and copy into it.
            using Texture staging = GD.CreateTexture(new(texture.Width, texture.Height, depth: 1,
                texture.MipLevels, texture.ArrayLayers,
                texture.Format, TextureUsage.Staging,
                texture.Kind, texture.SampleCount));

            using CommandList cl = GD.CreateCommandList();
            cl.Begin();

            cl.CopyTexture(texture,
                srcX: 0, srcY: 0, srcZ: depth,
                srcMipLevel: 0, srcBaseArrayLayer: 0,
                staging,
                dstX: 0, dstY: 0, dstZ: 0,
                dstMipLevel: 0, dstBaseArrayLayer: 0,
                staging.Width, staging.Height,
                depth: 1, layerCount: 1);

            cl.End();
            device.SubmitCommands(cl);
            device.WaitForIdle();

            try
            {
                MappedResourceView<TexelType> mapped = device.Map<TexelType>(staging, MapMode.Read);

                int notFilledCount = 0;
                for (int y = 0; y < staging.Height; y++)
                {
                    for (int x = 0; x < staging.Width; x++)
                    {
                        TexelType actual = mapped[x, y];
                        if (!fillValue.Equals(actual))
                        {
                            notFilledCount++;
                        }
                    }
                }

                return notFilledCount;
            }
            finally
            {
                device.Unmap(staging);
            }
        }


        [StructLayout(LayoutKind.Sequential)]
        public struct BasicComputeTestParams
        {
            public uint Width;
            public uint Height;
            private uint _padding1;
            private uint _padding2;
        }

        [SkippableFact]
        public void BasicCompute()
        {
            Skip.IfNot(GD.Features.ComputeShader);

            ResourceLayout layout = GD.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("Params", ResourceKind.UniformBuffer, ShaderStages.Compute),
                new ResourceLayoutElementDescription("Source", ResourceKind.StructuredBufferReadWrite, ShaderStages.Compute),
                new ResourceLayoutElementDescription("Destination", ResourceKind.StructuredBufferReadWrite, ShaderStages.Compute)));

            uint width = 1024;
            uint height = 1024;
            DeviceBuffer paramsBuffer = GD.CreateBuffer(new BufferDescription((uint)Unsafe.SizeOf<BasicComputeTestParams>(), BufferUsage.UniformBuffer));
            DeviceBuffer sourceBuffer = GD.CreateBuffer(new BufferDescription(width * height * 4, BufferUsage.StructuredBufferReadWrite, 4, true));
            DeviceBuffer destinationBuffer = GD.CreateBuffer(new BufferDescription(width * height * 4, BufferUsage.StructuredBufferReadWrite, 4, true));

            GD.UpdateBuffer(paramsBuffer, 0, new BasicComputeTestParams { Width = width, Height = height });

            float[] sourceData = new float[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int index = y * (int)width + x;
                    sourceData[index] = index;
                }
            GD.UpdateBuffer(sourceBuffer, 0, sourceData);

            ResourceSet rs = GD.CreateResourceSet(new ResourceSetDescription(layout, paramsBuffer, sourceBuffer, destinationBuffer));

            Pipeline pipeline = GD.CreateComputePipeline(new ComputePipelineDescription(
                TestShaders.LoadCompute(GD, "BasicComputeTest"),
                layout,
                16, 16, 1));

            CommandList cl = GD.CreateCommandList();
            cl.Begin();
            cl.SetPipeline(pipeline);
            cl.SetComputeResourceSet(0, rs);
            cl.Dispatch(width / 16, width / 16, 1);
            cl.End();
            GD.SubmitCommands(cl);
            GD.WaitForIdle();

            DeviceBuffer sourceReadback = GetReadback(sourceBuffer);
            DeviceBuffer destinationReadback = GetReadback(destinationBuffer);

            MappedResourceView<float> sourceReadView = GD.Map<float>(sourceReadback, MapMode.Read);
            MappedResourceView<float> destinationReadView = GD.Map<float>(destinationReadback, MapMode.Read);
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int index = y * (int)width + x;
                    Assert.Equal(2 * sourceData[index], sourceReadView[index]);
                    Assert.Equal(sourceData[index], destinationReadView[index]);
                }

            GD.Unmap(sourceReadback);
            GD.Unmap(destinationReadback);
        }

        [SkippableFact]
        public void ComputeCubemapGeneration()
        {
            Skip.IfNot(GD.Features.ComputeShader);

            const int TexSize = 32;
            const uint MipLevels = 1;

            TextureDescription texDesc = TextureDescription.Texture2D(
                TexSize, TexSize,
                MipLevels,
                1,
                PixelFormat.R8_G8_B8_A8_UNorm,
                TextureUsage.Sampled | TextureUsage.Storage | TextureUsage.Cubemap);
            Texture computeOutput = GD.CreateTexture(texDesc);

            Vector4[] faceColors = new Vector4[] {
                new Vector4(0 * 42),
                new Vector4(1 * 42),
                new Vector4(2 * 42),
                new Vector4(3 * 42),
                new Vector4(4 * 42),
                new Vector4(5 * 42)
            };

            ResourceLayout computeLayout = GD.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("ComputeOutput", ResourceKind.TextureReadWrite, ShaderStages.Compute)));
            ResourceSet computeSet = GD.CreateResourceSet(new ResourceSetDescription(computeLayout, computeOutput));

            Pipeline computePipeline = GD.CreateComputePipeline(new ComputePipelineDescription(
                TestShaders.LoadCompute(GD, "ComputeCubemapGenerator"),
                computeLayout,
                32, 32, 1));

            CommandList cl = GD.CreateCommandList();
            cl.Begin();
            cl.SetPipeline(computePipeline);
            cl.SetComputeResourceSet(0, computeSet);
            cl.Dispatch(TexSize / 32, TexSize / 32, 6);
            cl.End();
            GD.SubmitCommands(cl);
            GD.WaitForIdle();

            using (var readback = GetReadback(computeOutput))
            {
                for (uint mip = 0; mip < MipLevels; mip++)
                {
                    for (uint face = 0; face < 6; face++)
                    {
                        var subresource = readback.CalculateSubresource(mip, face);
                        var mipSize = (TexSize >> (int)mip);
                        var expectedColor = new RgbaByte((byte)faceColors[face].X, (byte)faceColors[face].Y, (byte)faceColors[face].Z, (byte)faceColors[face].Z);
                        MappedResourceView<RgbaByte> readView = GD.Map<RgbaByte>(readback, MapMode.Read, subresource);
                        for (int y = 0; y < mipSize; y++)
                            for (int x = 0; x < mipSize; x++)
                            {
                                Assert.Equal(expectedColor, readView[x, y]);
                            }
                        GD.Unmap(readback, subresource);
                    }
                }
            }
        }

        [SkippableFact]
        public void ComputeCubemapBindSingleTextureMipLevelOutput()
        {
            Skip.IfNot(GD.Features.ComputeShader);

            const int TexSize = 128;
            const uint MipLevels = 7;

            const uint BoundMipLevel = 2;

            TextureDescription texDesc = TextureDescription.Texture2D(
                TexSize, TexSize,
                MipLevels,
                1,
                PixelFormat.R8_G8_B8_A8_UNorm,
                TextureUsage.Sampled | TextureUsage.Storage | TextureUsage.Cubemap);
            Texture computeOutput = GD.CreateTexture(texDesc);

            TextureView computeOutputMipLevel = GD.CreateTextureView(new TextureViewDescription(computeOutput, BoundMipLevel, 1, 0, 1));

            Vector4[] faceColors = new Vector4[] {
                new Vector4(0 * 42),
                new Vector4(1 * 42),
                new Vector4(2 * 42),
                new Vector4(3 * 42),
                new Vector4(4 * 42),
                new Vector4(5 * 42)
            };

            ResourceLayout computeLayout = GD.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("ComputeOutput", ResourceKind.TextureReadWrite, ShaderStages.Compute)));
            ResourceSet computeSet = GD.CreateResourceSet(new ResourceSetDescription(computeLayout, computeOutputMipLevel));

            Pipeline computePipeline = GD.CreateComputePipeline(new ComputePipelineDescription(
                TestShaders.LoadCompute(GD, "ComputeCubemapGenerator"),
                computeLayout,
                32, 32, 1));

            using (var readback = GetReadback(computeOutput))
            {
                for (uint mip = 0; mip < MipLevels; mip++)
                {
                    for (uint face = 0; face < 6; face++)
                    {
                        var subresource = readback.CalculateSubresource(mip, face);
                        var mipSize = (uint)(TexSize / (1 << (int)mip));
                        var expectedColor = RgbaByte.Clear;
                        MappedResourceView<RgbaByte> readView = GD.Map<RgbaByte>(readback, MapMode.Read, subresource);
                        for (int y = 0; y < mipSize; y++)
                            for (int x = 0; x < mipSize; x++)
                            {
                                Assert.Equal(expectedColor, readView[x, y]);
                            }
                        GD.Unmap(readback, subresource);
                    }
                }
            }

            CommandList cl = GD.CreateCommandList();
            cl.Begin();
            cl.SetPipeline(computePipeline);
            cl.SetComputeResourceSet(0, computeSet);
            cl.Dispatch((TexSize >> (int)BoundMipLevel) / 32, (TexSize >> (int)BoundMipLevel) / 32, 6);
            cl.End();
            GD.SubmitCommands(cl);
            GD.WaitForIdle();

            using (var readback = GetReadback(computeOutput))
            {
                for (uint mip = 0; mip < MipLevels; mip++)
                {
                    for (uint face = 0; face < 6; face++)
                    {
                        var subresource = readback.CalculateSubresource(mip, face);
                        var mipSize = (uint)(TexSize / (1 << (int)mip));
                        var expectedColor = mip == BoundMipLevel ? new RgbaByte((byte)faceColors[face].X, (byte)faceColors[face].Y, (byte)faceColors[face].Z, (byte)faceColors[face].Z) : RgbaByte.Clear;
                        MappedResourceView<RgbaByte> readView = GD.Map<RgbaByte>(readback, MapMode.Read, subresource);
                        for (int y = 0; y < mipSize; y++)
                            for (int x = 0; x < mipSize; x++)
                            {
                                Assert.Equal(expectedColor, readView[x, y]);
                            }
                        GD.Unmap(readback, subresource);
                    }
                }
            }
        }

        [SkippableTheory]
        [MemberData(nameof(FillBuffer_WithOffsetsData))]
        public void FillBuffer_WithOffsets(uint srcSetMultiple, uint srcBindingMultiple, uint dstSetMultiple, uint dstBindingMultiple, bool combinedLayout)
        {
            Skip.IfNot(GD.Features.ComputeShader);
            Skip.If(!GD.Features.BufferRangeBinding && (srcSetMultiple != 0 || srcBindingMultiple != 0 || dstSetMultiple != 0 || dstBindingMultiple != 0));

            Debug.Assert((GD.StructuredBufferMinOffsetAlignment % sizeof(uint)) == 0);

            uint valueCount = 512;
            uint dataSize = valueCount * sizeof(uint);
            uint totalSrcAlignment = GD.StructuredBufferMinOffsetAlignment * (srcSetMultiple + srcBindingMultiple);
            uint totalDstAlignment = GD.StructuredBufferMinOffsetAlignment * (dstSetMultiple + dstBindingMultiple);

            DeviceBuffer copySrc = GD.CreateBuffer(
                new BufferDescription(totalSrcAlignment + dataSize, BufferUsage.StructuredBufferReadOnly, sizeof(uint), true));
            DeviceBuffer copyDst = GD.CreateBuffer(
                new BufferDescription(totalDstAlignment + dataSize, BufferUsage.StructuredBufferReadWrite, sizeof(uint), true));

            ResourceLayout[] layouts;
            ResourceSet[] sets;

            DeviceBufferRange srcRange = new DeviceBufferRange(copySrc, srcSetMultiple * GD.StructuredBufferMinOffsetAlignment, dataSize);
            DeviceBufferRange dstRange = new DeviceBufferRange(copyDst, dstSetMultiple * GD.StructuredBufferMinOffsetAlignment, dataSize);

            if (combinedLayout)
            {
                layouts = new[]
                {
                    GD.CreateResourceLayout(new ResourceLayoutDescription(
                        new ResourceLayoutElementDescription(
                            "CopySrc",
                            ResourceKind.StructuredBufferReadOnly,
                            ShaderStages.Compute,
                            ResourceLayoutElementOptions.DynamicBinding),
                        new ResourceLayoutElementDescription(
                            "CopyDst",
                            ResourceKind.StructuredBufferReadWrite,
                            ShaderStages.Compute,
                            ResourceLayoutElementOptions.DynamicBinding)))
                };
                sets = new[]
                {
                    GD.CreateResourceSet(new ResourceSetDescription(layouts[0], srcRange, dstRange))
                };
            }
            else
            {
                layouts = new[]
                {
                    GD.CreateResourceLayout(new ResourceLayoutDescription(
                        new ResourceLayoutElementDescription(
                            "CopySrc",
                            ResourceKind.StructuredBufferReadOnly,
                            ShaderStages.Compute,
                            ResourceLayoutElementOptions.DynamicBinding))),
                    GD.CreateResourceLayout(new ResourceLayoutDescription(
                        new ResourceLayoutElementDescription(
                            "CopyDst",
                            ResourceKind.StructuredBufferReadWrite,
                            ShaderStages.Compute,
                            ResourceLayoutElementOptions.DynamicBinding)))
                };
                sets = new[]
                {
                    GD.CreateResourceSet(new ResourceSetDescription(layouts[0], srcRange)),
                    GD.CreateResourceSet(new ResourceSetDescription(layouts[1], dstRange)),
                };
            }

            Pipeline pipeline = GD.CreateComputePipeline(new ComputePipelineDescription(
                TestShaders.LoadCompute(GD, combinedLayout ? "FillBuffer" : "FillBuffer_SeparateLayout"),
                layouts,
                1, 1, 1));

            uint[] srcData = Enumerable.Range(0, (int)copySrc.SizeInBytes / sizeof(uint)).Select(i => (uint)i).ToArray();
            GD.UpdateBuffer(copySrc, 0, srcData);

            CommandList cl = GD.CreateCommandList();
            cl.Begin();
            cl.SetPipeline(pipeline);
            if (combinedLayout)
            {
                uint[] offsets = new[]
                {
                    srcBindingMultiple * GD.StructuredBufferMinOffsetAlignment,
                    dstBindingMultiple * GD.StructuredBufferMinOffsetAlignment
                };
                cl.SetComputeResourceSet(0, sets[0], offsets);
            }
            else
            {
                uint offset = srcBindingMultiple * GD.StructuredBufferMinOffsetAlignment;
                cl.SetComputeResourceSet(0, sets[0], 1, ref offset);
                offset = dstBindingMultiple * GD.StructuredBufferMinOffsetAlignment;
                cl.SetComputeResourceSet(1, sets[1], 1, ref offset);
            }
            cl.Dispatch(512, 1, 1);
            cl.End();
            GD.SubmitCommands(cl);
            GD.WaitForIdle();

            DeviceBuffer readback = GetReadback(copyDst);

            MappedResourceView<uint> readView = GD.Map<uint>(readback, MapMode.Read);
            for (uint i = 0; i < valueCount; i++)
            {
                uint srcIndex = totalSrcAlignment / sizeof(uint) + i;
                uint expected = srcData[(int)srcIndex];

                uint dstIndex = totalDstAlignment / sizeof(uint) + i;
                uint actual = readView[dstIndex];

                Assert.Equal(expected, actual);
            }
            GD.Unmap(readback);
        }

        public static IEnumerable<object[]> FillBuffer_WithOffsetsData()
        {
            foreach (uint srcSetMultiple in new[] { 0, 2, 10 })
                foreach (uint srcBindingMultiple in new[] { 0, 2, 10 })
                    foreach (uint dstSetMultiple in new[] { 0, 2, 10 })
                        foreach (uint dstBindingMultiple in new[] { 0, 2, 10 })
                            foreach (bool combinedLayout in new[] { false, true })
                            {
                                yield return new object[] { srcSetMultiple, srcBindingMultiple, dstSetMultiple, dstBindingMultiple, combinedLayout };
                            }
        }

    }

    [Trait("Backend", "Vulkan")]
    public class VulkanComputeTests : ComputeTests
    {
        public VulkanComputeTests(ITestOutputHelper textOutputHelper) : base(textOutputHelper)
        {
        }
    }
}

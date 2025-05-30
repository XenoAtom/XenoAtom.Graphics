using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

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


    [TestClass]
    public class ComputeTests : GraphicsDeviceTestBase
    {

        [TestMethod]
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
                computeLayout));

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

            using (var cbp = GD.CreateCommandBufferPool())
            using (var cb = cbp.CreateCommandBuffer())
            {
                cb.Begin();

                cb.UpdateBuffer(fillValueBuffer, 0, new FillValueStruct(FillValue));

                // Use the compute shader to fill the texture.
                cb.SetPipeline(computePipeline);
                cb.SetComputeResourceSet(0, computeResourceSet);
                const uint GroupDivisorXY = 16;
                cb.Dispatch(OutputTextureSize / GroupDivisorXY, OutputTextureSize / GroupDivisorXY, OutputTextureSize);

                cb.End();
                GD.SubmitCommands(cb);
                GD.WaitForIdle();
            }

            // Read back from our texture and make sure it has been properly filled.
            for (uint depth = 0; depth < computeTargetTexture.Depth; depth++)
            {
                RgbaFloat expectedFillValue = new RgbaFloat(new System.Numerics.Vector4(FillValue * (depth + 1)));
                int notFilledCount = CountTexelsNotFilledAtDepth(GD, computeTargetTexture, expectedFillValue, depth);

                Assert.AreEqual(0, notFilledCount);
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

            using (var cbp = GD.CreateCommandBufferPool())
            using (var cb = cbp.CreateCommandBuffer())
            {
                cb.Begin();

                cb.CopyTexture(texture,
                    srcX: 0, srcY: 0, srcZ: depth,
                    srcMipLevel: 0, srcBaseArrayLayer: 0,
                    staging,
                    dstX: 0, dstY: 0, dstZ: 0,
                    dstMipLevel: 0, dstBaseArrayLayer: 0,
                    staging.Width, staging.Height,
                    depth: 1, layerCount: 1);

                cb.End();
                device.SubmitCommands(cb);
                device.WaitForIdle();
            }

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

        [TestMethod]
        public void BasicCompute()
        {
            if (!GD.Features.ComputeShader)
            {
                Assert.Inconclusive("Compute shaders are not supported on this device.");
                return;
            }

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
                layout));

            using (var cbp = GD.CreateCommandBufferPool())
            using (var cb = cbp.CreateCommandBuffer())
            {
                cb.Begin();
                cb.SetPipeline(pipeline);
                cb.SetComputeResourceSet(0, rs);
                cb.Dispatch(width / 16, width / 16, 1);
                cb.End();
                GD.SubmitCommands(cb);
                GD.WaitForIdle();
            }

            DeviceBuffer sourceReadback = GetReadback(sourceBuffer);
            DeviceBuffer destinationReadback = GetReadback(destinationBuffer);

            MappedResourceView<float> sourceReadView = GD.Map<float>(sourceReadback, MapMode.Read);
            MappedResourceView<float> destinationReadView = GD.Map<float>(destinationReadback, MapMode.Read);
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int index = y * (int)width + x;
                    Assert.AreEqual(2 * sourceData[index], sourceReadView[index]);
                    Assert.AreEqual(sourceData[index], destinationReadView[index]);
                }

            GD.Unmap(sourceReadback);
            GD.Unmap(destinationReadback);
        }

        [TestMethod]
        public void ComputeCubemapGeneration()
        {
            if (!GD.Features.ComputeShader)
            {
                Assert.Inconclusive("Compute shaders are not supported on this device.");
                return;
            }

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
                computeLayout));

            using (var cbp = GD.CreateCommandBufferPool())
            using (var cb = cbp.CreateCommandBuffer())
            {
                cb.Begin();
                cb.SetPipeline(computePipeline);
                cb.SetComputeResourceSet(0, computeSet);
                cb.Dispatch(TexSize / 32, TexSize / 32, 6);
                cb.End();
                GD.SubmitCommands(cb);
                GD.WaitForIdle();
            }

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
                                Assert.AreEqual(expectedColor, readView[x, y]);
                            }
                        GD.Unmap(readback, subresource);
                    }
                }
            }
        }

        [TestMethod]
        public void ComputeCubemapBindSingleTextureMipLevelOutput()
        {
            if (!GD.Features.ComputeShader)
            {
                Assert.Inconclusive("Compute shaders are not supported on this device.");
                return;
            }

            const int TexSize = 128;
            const uint MipLevels = 7;

            const uint BoundMipLevel = 2;

            TextureDescription texDesc = TextureDescription.Texture2D(
                TexSize, TexSize,
                MipLevels,
                1,
                PixelFormat.R8_G8_B8_A8_UNorm,
                TextureUsage.Sampled | TextureUsage.Storage | TextureUsage.Cubemap | TextureUsage.RenderTarget);
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
                computeLayout));

            using var cbp = GD.CreateCommandBufferPool(new(CommandBufferPoolFlags.CanResetCommandBuffer));
            using var cb = cbp.CreateCommandBuffer();
            cb.Begin();
            cb.ClearTexture(computeOutput);
            cb.End();
            {
                var fence = GD.CreateFence(false);
                GD.SubmitCommands(cb, fence);
                GD.WaitForFence(fence);
            }

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
                                Assert.AreEqual(expectedColor, readView[x, y]);
                            }
                        GD.Unmap(readback, subresource);
                    }
                }
            }

            cb.Begin();
            cb.SetPipeline(computePipeline);
            cb.SetComputeResourceSet(0, computeSet);
            cb.Dispatch((TexSize >> (int)BoundMipLevel) / 32, (TexSize >> (int)BoundMipLevel) / 32, 6);
            cb.End();
            GD.SubmitCommands(cb);
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
                                Assert.AreEqual(expectedColor, readView[x, y]);
                            }
                        GD.Unmap(readback, subresource);
                    }
                }
            }
        }

        [TestMethod]
        [DynamicData(nameof(FillBuffer_WithOffsetsData))]
        public void FillBuffer_WithOffsets(uint srcSetMultiple, uint srcBindingMultiple, uint dstSetMultiple, uint dstBindingMultiple, bool combinedLayout)
        {
            if (!GD.Features.ComputeShader)
            {
                Assert.Inconclusive("Compute shaders are not supported on this device.");
                return;
            }

            if (!GD.Features.BufferRangeBinding && (srcSetMultiple != 0 || srcBindingMultiple != 0 || dstSetMultiple != 0 || dstBindingMultiple != 0))
            {
                Assert.Inconclusive("Buffer range binding is not supported on this device.");
                return;
            }

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
                layouts));

            uint[] srcData = Enumerable.Range(0, (int)copySrc.SizeInBytes / sizeof(uint)).Select(i => (uint)i).ToArray();
            GD.UpdateBuffer(copySrc, 0, srcData);

            using (var cbp = GD.CreateCommandBufferPool())
            using (var cb = cbp.CreateCommandBuffer())
            {
                cb.Begin();
                cb.SetPipeline(pipeline);
                if (combinedLayout)
                {
                    uint[] offsets = new[]
                    {
                        srcBindingMultiple * GD.StructuredBufferMinOffsetAlignment,
                        dstBindingMultiple * GD.StructuredBufferMinOffsetAlignment
                    };
                    cb.SetComputeResourceSet(0, sets[0], offsets);
                }
                else
                {
                    uint offset = srcBindingMultiple * GD.StructuredBufferMinOffsetAlignment;
                    cb.SetComputeResourceSet(0, sets[0], 1, ref offset);
                    offset = dstBindingMultiple * GD.StructuredBufferMinOffsetAlignment;
                    cb.SetComputeResourceSet(1, sets[1], 1, ref offset);
                }

                cb.Dispatch(512, 1, 1);
                cb.End();
                GD.SubmitCommands(cb);
                GD.WaitForIdle();
            }

            DeviceBuffer readback = GetReadback(copyDst);

            MappedResourceView<uint> readView = GD.Map<uint>(readback, MapMode.Read);
            for (uint i = 0; i < valueCount; i++)
            {
                uint srcIndex = totalSrcAlignment / sizeof(uint) + i;
                uint expected = srcData[(int)srcIndex];

                uint dstIndex = totalDstAlignment / sizeof(uint) + i;
                uint actual = readView[dstIndex];

                Assert.AreEqual(expected, actual);
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
}

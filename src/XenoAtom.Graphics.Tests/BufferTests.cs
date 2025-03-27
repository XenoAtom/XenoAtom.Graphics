using System;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace XenoAtom.Graphics.Tests
{
    [TestClass]
    public class BufferTests : GraphicsDeviceTestBase
    {
        [TestMethod]
        public void CreateBuffer_Succeeds()
        {
            uint expectedSize = 64;
            BufferUsage expectedUsage = BufferUsage.Dynamic | BufferUsage.UniformBuffer;

            DeviceBuffer buffer = GD.CreateBuffer(new BufferDescription(expectedSize, expectedUsage));

            Assert.AreEqual(expectedUsage, buffer.Usage);
            Assert.AreEqual(expectedSize, buffer.SizeInBytes);
        }

        [TestMethod]
        public void UpdateBuffer_NonDynamic_Succeeds()
        {
            DeviceBuffer buffer = CreateBuffer(64, BufferUsage.VertexBuffer);
            GD.UpdateBuffer(buffer, 0, Matrix4x4.Identity);
            GD.WaitForIdle();
        }

        [TestMethod]
        public void UpdateBuffer_Span_Succeeds()
        {
            DeviceBuffer buffer = CreateBuffer(64, BufferUsage.VertexBuffer);
            float[] data = new float[16];
            GD.UpdateBuffer(buffer, 0, (ReadOnlySpan<float>)data);
            GD.WaitForIdle();
        }

        [TestMethod]
        public void UpdateBuffer_ThenMapRead_Succeeds()
        {
            DeviceBuffer buffer = CreateBuffer(1024, BufferUsage.Staging);
            int[] data = Enumerable.Range(0, 256).Select(i => 2 * i).ToArray();
            GD.UpdateBuffer(buffer, 0, data);

            MappedResourceView<int> view = GD.Map<int>(buffer, MapMode.Read);
            for (int i = 0; i < view.Count; i++)
            {
                Assert.AreEqual(i * 2, view[i]);
            }
        }

        [TestMethod]
        public unsafe void Staging_Map_WriteThenRead()
        {
            DeviceBuffer buffer = CreateBuffer(256, BufferUsage.Staging);
            MappedResource map = GD.Map(buffer, MapMode.Write);
            byte* dataPtr = (byte*)map.Data.ToPointer();
            for (int i = 0; i < map.SizeInBytes; i++)
            {
                dataPtr[i] = (byte)i;
            }

            GD.Unmap(buffer);

            map = GD.Map(buffer, MapMode.Read);
            dataPtr = (byte*)map.Data.ToPointer();
            for (int i = 0; i < map.SizeInBytes; i++)
            {
                Assert.AreEqual((byte)i, dataPtr[i]);
            }
        }

        [TestMethod]
        public void Staging_MapGeneric_WriteThenRead()
        {
            DeviceBuffer buffer = CreateBuffer(1024, BufferUsage.Staging);
            MappedResourceView<int> view = GD.Map<int>(buffer, MapMode.Write);
            Assert.AreEqual(256, view.Count);
            for (int i = 0; i < view.Count; i++)
            {
                view[i] = i * 10;
            }

            GD.Unmap(buffer);

            view = GD.Map<int>(buffer, MapMode.Read);
            Assert.AreEqual(256, view.Count);
            for (int i = 0; i < view.Count; i++)
            {
                view[i] = 1 * 10;
            }

            GD.Unmap(buffer);
        }

        [TestMethod]
        public void MapGeneric_OutOfBounds_ThrowsIndexOutOfRange()
        {
            DeviceBuffer buffer = CreateBuffer(1024, BufferUsage.Staging);
            MappedResourceView<byte> view = GD.Map<byte>(buffer, MapMode.ReadWrite);
            Assert.Throws<IndexOutOfRangeException>(() => _ = view[1024]);
            Assert.Throws<IndexOutOfRangeException>(() => _ = view[-1]);
        }

        [TestMethod]
        public void Map_WrongFlags_Throws()
        {
            DeviceBuffer buffer = CreateBuffer(1024, BufferUsage.VertexBuffer);
            Assert.Throws<GraphicsException>(() => GD.Map(buffer, MapMode.Read));
            Assert.Throws<GraphicsException>(() => GD.Map(buffer, MapMode.Write));
            Assert.Throws<GraphicsException>(() => GD.Map(buffer, MapMode.ReadWrite));
        }

        [TestMethod]
        public void CopyBuffer_Succeeds()
        {
            DeviceBuffer src = CreateBuffer(1024, BufferUsage.Staging);
            int[] data = Enumerable.Range(0, 256).Select(i => 2 * i).ToArray();
            GD.UpdateBuffer(src, 0, data);

            DeviceBuffer dst = CreateBuffer(1024, BufferUsage.Staging);

            using (var cbp = GD.CreateCommandBufferPool())
            using (var copyCL = cbp.CreateCommandBuffer())
            {
                copyCL.Begin();
                copyCL.CopyBuffer(src, 0, dst, 0, src.SizeInBytes);
                copyCL.End();
                GD.SubmitCommands(copyCL);
                GD.WaitForIdle();
            }

            src.Dispose();

            MappedResourceView<int> view = GD.Map<int>(dst, MapMode.Read);
            for (int i = 0; i < view.Count; i++)
            {
                Assert.AreEqual(i * 2, view[i]);
            }
        }

        [TestMethod]
        public void CopyBuffer_Chain_Succeeds()
        {
            DeviceBuffer src = CreateBuffer(1024, BufferUsage.Staging);
            int[] data = Enumerable.Range(0, 256).Select(i => 2 * i).ToArray();
            GD.UpdateBuffer(src, 0, data);

            DeviceBuffer finalDst = CreateBuffer(1024, BufferUsage.Staging);

            for (int chainLength = 2; chainLength <= 10; chainLength += 4)
            {
                DeviceBuffer[] dsts = Enumerable.Range(0, chainLength)
                    .Select(i => GD.CreateBuffer(new BufferDescription(1024, BufferUsage.UniformBuffer)))
                    .ToArray();

                using (var cbp = GD.CreateCommandBufferPool())
                using (var copyCL = cbp.CreateCommandBuffer())
                {
                    copyCL.Begin();
                    copyCL.CopyBuffer(src, 0, dsts[0], 0, src.SizeInBytes);
                    for (int i = 0; i < chainLength - 1; i++)
                    {
                        copyCL.CopyBuffer(dsts[i], 0, dsts[i + 1], 0, src.SizeInBytes);
                    }

                    copyCL.CopyBuffer(dsts[dsts.Length - 1], 0, finalDst, 0, src.SizeInBytes);
                    copyCL.End();
                    GD.SubmitCommands(copyCL);
                    GD.WaitForIdle();
                }

                MappedResourceView<int> view = GD.Map<int>(finalDst, MapMode.Read);
                for (int i = 0; i < view.Count; i++)
                {
                    Assert.AreEqual(i * 2, view[i]);
                }

                GD.Unmap(finalDst);
            }
        }

        [TestMethod]
        public void MapThenUpdate_Fails()
        {
            if (GD.BackendType == GraphicsBackend.Vulkan)
            {
                return; // TODO
            }

            if (GD.BackendType == GraphicsBackend.Metal)
            {
                return; // TODO
            }

            DeviceBuffer buffer = GD.CreateBuffer(new BufferDescription(1024, BufferUsage.Staging));
            MappedResourceView<int> view = GD.Map<int>(buffer, MapMode.ReadWrite);
            int[] data = Enumerable.Range(0, 256).Select(i => 2 * i).ToArray();
            Assert.Throws<GraphicsException>(() => GD.UpdateBuffer(buffer, 0, data));
        }

        [TestMethod]
        public void Map_MultipleTimes_Succeeds()
        {
            DeviceBuffer buffer = GD.CreateBuffer(new BufferDescription(1024, BufferUsage.Staging));
            MappedResource map = GD.Map(buffer, MapMode.ReadWrite);
            IntPtr dataPtr = map.Data;
            map = GD.Map(buffer, MapMode.ReadWrite);
            Assert.AreEqual(map.Data, dataPtr);
            map = GD.Map(buffer, MapMode.ReadWrite);
            Assert.AreEqual(map.Data, dataPtr);
            GD.Unmap(buffer);
            GD.Unmap(buffer);
            GD.Unmap(buffer);
        }

        [TestMethod]
        public void Map_DifferentMode_Fails()
        {
            if (GD.BackendType == GraphicsBackend.Vulkan)
            {
                return; // TODO
            }

            if (GD.BackendType == GraphicsBackend.Metal)
            {
                return; // TODO
            }

            DeviceBuffer buffer = GD.CreateBuffer(new BufferDescription(1024, BufferUsage.Staging));
            MappedResource map = GD.Map(buffer, MapMode.Read);
            Assert.Throws<GraphicsException>(() => GD.Map(buffer, MapMode.Write));
        }

        [TestMethod]
        public unsafe void UnusualSize()
        {
            DeviceBuffer src = GD.CreateBuffer(
                new BufferDescription(208, BufferUsage.UniformBuffer));
            DeviceBuffer dst = GD.CreateBuffer(
                new BufferDescription(208, BufferUsage.Staging));

            byte[] data = Enumerable.Range(0, 208).Select(i => (byte)(i * 150)).ToArray();
            GD.UpdateBuffer(src, 0, data);

            using (var cbp = GD.CreateCommandBufferPool())
            using (var cb = cbp.CreateCommandBuffer())
            {
                cb.Begin();
                cb.CopyBuffer(src, 0, dst, 0, src.SizeInBytes);
                cb.End();
                GD.SubmitCommands(cb);
                GD.WaitForIdle();
            }

            MappedResource readMap = GD.Map(dst, MapMode.Read);
            for (int i = 0; i < readMap.SizeInBytes; i++)
            {
                Assert.AreEqual((byte)(i * 150), ((byte*)readMap.Data)[i]);
            }
        }

        [TestMethod]
        public void Update_Dynamic_NonZeroOffset()
        {
            DeviceBuffer dynamic = GD.CreateBuffer(
                new BufferDescription(1024, BufferUsage.Dynamic | BufferUsage.UniformBuffer));

            byte[] initialData = Enumerable.Range(0, 1024).Select(i => (byte)i).ToArray();
            GD.UpdateBuffer(dynamic, 0, initialData);

            byte[] replacementData = Enumerable.Repeat((byte)255, 512).ToArray();
            DeviceBuffer dst = GD.CreateBuffer(new BufferDescription(1024, BufferUsage.Staging));

            using (var cbp = GD.CreateCommandBufferPool(new(CommandBufferPoolFlags.CanResetCommandBuffer)))
            using (var cb = cbp.CreateCommandBuffer())
            {
                cb.Begin();

                cb.UpdateBuffer(dynamic, 512, replacementData);
                cb.End();
                GD.SubmitCommands(cb);
                GD.WaitForIdle();


                cb.Begin();
                cb.CopyBuffer(dynamic, 0, dst, 0, dynamic.SizeInBytes);
                cb.End();
                GD.SubmitCommands(cb);
                GD.WaitForIdle();
            }

            MappedResourceView<byte> readView = GD.Map<byte>(dst, MapMode.Read);
            for (uint i = 0; i < 512; i++)
            {
                Assert.AreEqual((byte)i, readView[i]);
            }

            for (uint i = 512; i < 1024; i++)
            {
                Assert.AreEqual((byte)255, readView[i]);
            }
        }

        [TestMethod]
        public void Dynamic_MapRead_Fails()
        {
            DeviceBuffer dynamic = GD.CreateBuffer(
                new BufferDescription(1024, BufferUsage.Dynamic | BufferUsage.UniformBuffer));
            Assert.Throws<GraphicsException>(() => GD.Map(dynamic, MapMode.Read));
            Assert.Throws<GraphicsException>(() => GD.Map(dynamic, MapMode.ReadWrite));
        }

        [TestMethod]
        public void CommandList_Update_Staging()
        {
            DeviceBuffer staging = GD.CreateBuffer(
                new BufferDescription(1024, BufferUsage.Staging));
            byte[] data = Enumerable.Range(0, 1024).Select(i => (byte)i).ToArray();

            using (var cbp = GD.CreateCommandBufferPool())
            using (var cb = cbp.CreateCommandBuffer())
            {
                cb.Begin();
                cb.UpdateBuffer(staging, 0, data);
                cb.End();
                GD.SubmitCommands(cb);
                GD.WaitForIdle();
            }

            MappedResourceView<byte> readView = GD.Map<byte>(staging, MapMode.Read);
            for (uint i = 0; i < staging.SizeInBytes; i++)
            {
                Assert.AreEqual((byte)i, readView[i]);
            }
        }

        [TestMethod]
        [DataRow(
            60u, BufferUsage.VertexBuffer, 1u,
            70u, BufferUsage.VertexBuffer, 13u,
            11u)]
        [DataRow(
            60u, BufferUsage.Staging, 1u,
            70u, BufferUsage.VertexBuffer, 13u,
            11u)]
        [DataRow(
            60u, BufferUsage.VertexBuffer, 1u,
            70u, BufferUsage.Staging, 13u,
            11u)]
        [DataRow(
            60u, BufferUsage.Staging, 1u,
            70u, BufferUsage.Staging, 13u,
            11u)]
        [DataRow(
            5u, BufferUsage.VertexBuffer, 3u,
            10u, BufferUsage.VertexBuffer, 7u,
            2u)]
        public void Copy_UnalignedRegion(
            uint srcBufferSize, BufferUsage srcUsage, uint srcCopyOffset,
            uint dstBufferSize, BufferUsage dstUsage, uint dstCopyOffset,
            uint copySize)
        {
            DeviceBuffer src = CreateBuffer(srcBufferSize, srcUsage);
            DeviceBuffer dst = CreateBuffer(dstBufferSize, dstUsage);

            byte[] data = Enumerable.Range(0, (int)srcBufferSize).Select(i => (byte)i).ToArray();
            GD.UpdateBuffer(src, 0, data);

            using (var cbp = GD.CreateCommandBufferPool())
            using (var cb = cbp.CreateCommandBuffer())
            {
                cb.Begin();
                cb.CopyBuffer(src, srcCopyOffset, dst, dstCopyOffset, copySize);
                cb.End();

                GD.SubmitCommands(cb);
                GD.WaitForIdle();
            }

            DeviceBuffer readback = GetReadback(dst);

            MappedResourceView<byte> readView = GD.Map<byte>(readback, MapMode.Read);
            for (uint i = 0; i < copySize; i++)
            {
                byte expected = data[i + srcCopyOffset];
                byte actual = readView[i + dstCopyOffset];
                Assert.AreEqual(expected, actual);
            }

            GD.Unmap(readback);
        }

        [DataTestMethod]
        [DataRow(BufferUsage.VertexBuffer, 13U, 5U, 1U)]
        [DataRow(BufferUsage.Staging, 13U, 5U, 1U)]
        public void CommandList_UpdateNonStaging_Unaligned(BufferUsage usage, uint bufferSize, uint dataSize, uint offset)
        {
            DeviceBuffer buffer = CreateBuffer(bufferSize, usage);
            byte[] data = Enumerable.Range(0, (int)dataSize).Select(i => (byte)i).ToArray();
            using (var cbp = GD.CreateCommandBufferPool())
            using (var cb = cbp.CreateCommandBuffer())
            {
                cb.Begin();
                cb.UpdateBuffer(buffer, offset, data);
                cb.End();
                GD.SubmitCommands(cb);
                GD.WaitForIdle();
            }

            DeviceBuffer readback = GetReadback(buffer);
            MappedResourceView<byte> readView = GD.Map<byte>(readback, MapMode.Read);
            for (uint i = 0; i < dataSize; i++)
            {
                byte expected = data[i];
                byte actual = readView[i + offset];
                Assert.AreEqual(expected, actual);
            }

            GD.Unmap(readback);
        }

        [TestMethod]
        [DataRow(BufferUsage.UniformBuffer | BufferUsage.Dynamic)]
        [DataRow(BufferUsage.UniformBuffer)]
        [DataRow(BufferUsage.Staging)]
        public void UpdateUniform_Offset_GraphicsDevice(BufferUsage usage)
        {
            DeviceBuffer buffer = CreateBuffer(128, usage);
            Matrix4x4 mat1 = new Matrix4x4(1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1);
            GD.UpdateBuffer(buffer, 0, mat1);
            Matrix4x4 mat2 = new Matrix4x4(2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2);
            GD.UpdateBuffer(buffer, 64, mat2);

            DeviceBuffer readback = GetReadback(buffer);
            MappedResourceView<Matrix4x4> readView = GD.Map<Matrix4x4>(readback, MapMode.Read);
            Assert.AreEqual(mat1, readView[0]);
            Assert.AreEqual(mat2, readView[1]);
            GD.Unmap(readback);
        }

        [TestMethod]
        [DataRow(BufferUsage.UniformBuffer | BufferUsage.Dynamic)]
        [DataRow(BufferUsage.UniformBuffer)]
        [DataRow(BufferUsage.Staging)]
        public void UpdateUniform_Offset_CommandList(BufferUsage usage)
        {
            DeviceBuffer buffer = CreateBuffer(128, usage);
            Matrix4x4 mat1 = new Matrix4x4(1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1);
            Matrix4x4 mat2 = new Matrix4x4(2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2);

            using (var cbp = GD.CreateCommandBufferPool())
            using (var cb = cbp.CreateCommandBuffer())
            {
                cb.Begin();
                cb.UpdateBuffer(buffer, 0, mat1);
                cb.UpdateBuffer(buffer, 64, mat2);
                cb.End();
                GD.SubmitCommands(cb);
                GD.WaitForIdle();
            }

            DeviceBuffer readback = GetReadback(buffer);
            MappedResourceView<Matrix4x4> readView = GD.Map<Matrix4x4>(readback, MapMode.Read);
            Assert.AreEqual(mat1, readView[0]);
            Assert.AreEqual(mat2, readView[1]);
            GD.Unmap(readback);
        }

        [TestMethod]
        [DataRow(BufferUsage.UniformBuffer)]
        [DataRow(BufferUsage.UniformBuffer | BufferUsage.Dynamic)]
        [DataRow(BufferUsage.VertexBuffer)]
        [DataRow(BufferUsage.VertexBuffer | BufferUsage.Dynamic)]
        [DataRow(BufferUsage.IndexBuffer)]
        [DataRow(BufferUsage.IndexBuffer | BufferUsage.Dynamic)]
        [DataRow(BufferUsage.IndirectBuffer)]
        [DataRow(BufferUsage.StructuredBufferReadOnly)]
        [DataRow(BufferUsage.StructuredBufferReadOnly | BufferUsage.Dynamic)]
        [DataRow(BufferUsage.StructuredBufferReadWrite)]
        [DataRow(BufferUsage.VertexBuffer | BufferUsage.IndexBuffer)]
        [DataRow(BufferUsage.VertexBuffer | BufferUsage.IndexBuffer | BufferUsage.Dynamic)]
        [DataRow(BufferUsage.VertexBuffer | BufferUsage.IndexBuffer | BufferUsage.IndirectBuffer)]
        [DataRow(BufferUsage.IndexBuffer | BufferUsage.IndirectBuffer)]
        [DataRow(BufferUsage.Staging)]
        public void CreateBuffer_UsageFlagsCoverage(BufferUsage usage)
        {
            if ((usage & BufferUsage.StructuredBufferReadOnly) != 0
                || (usage & BufferUsage.StructuredBufferReadWrite) != 0)
            {
                return;
            }

            BufferDescription description = new BufferDescription(64, usage);
            if ((usage & BufferUsage.StructuredBufferReadOnly) != 0 || (usage & BufferUsage.StructuredBufferReadWrite) != 0)
            {
                description.StructureByteStride = 16;
            }

            DeviceBuffer buffer = GD.CreateBuffer(description);
            GD.UpdateBuffer(buffer, 0, new Vector4[4]);
            GD.WaitForIdle();
        }

        [TestMethod]
        [DataRow(BufferUsage.UniformBuffer)]
        [DataRow(BufferUsage.UniformBuffer | BufferUsage.Dynamic)]
        [DataRow(BufferUsage.VertexBuffer)]
        [DataRow(BufferUsage.VertexBuffer | BufferUsage.Dynamic)]
        [DataRow(BufferUsage.IndexBuffer)]
        [DataRow(BufferUsage.IndexBuffer | BufferUsage.Dynamic)]
        [DataRow(BufferUsage.IndirectBuffer)]
        [DataRow(BufferUsage.VertexBuffer | BufferUsage.IndexBuffer)]
        [DataRow(BufferUsage.VertexBuffer | BufferUsage.IndexBuffer | BufferUsage.Dynamic)]
        [DataRow(BufferUsage.VertexBuffer | BufferUsage.IndexBuffer | BufferUsage.IndirectBuffer)]
        [DataRow(BufferUsage.IndexBuffer | BufferUsage.IndirectBuffer)]
        [DataRow(BufferUsage.Staging)]
        public unsafe void CopyBuffer_ZeroSize(BufferUsage usage)
        {
            DeviceBuffer src = CreateBuffer(1024, usage);
            DeviceBuffer dst = CreateBuffer(1024, usage);

            byte[] initialDataSrc = Enumerable.Range(0, 1024).Select(i => (byte)i).ToArray();
            byte[] initialDataDst = Enumerable.Range(0, 1024).Select(i => (byte)(i * 2)).ToArray();
            GD.UpdateBuffer(src, 0, initialDataSrc);
            GD.UpdateBuffer(dst, 0, initialDataDst);

            using (var cbp = GD.CreateCommandBufferPool())
            using (var cb = cbp.CreateCommandBuffer())
            {
                cb.Begin();
                cb.CopyBuffer(src, 0, dst, 0, 0);
                cb.End();
                GD.SubmitCommands(cb);
                GD.WaitForIdle();
            }

            DeviceBuffer readback = GetReadback(dst);

            MappedResourceView<byte> readMap = GD.Map<byte>(readback, MapMode.Read);
            for (int i = 0; i < 1024; i++)
            {
                Assert.AreEqual((byte)(i * 2), readMap[i]);
            }

            GD.Unmap(readback);
        }

        [TestMethod]
        [DataRow(BufferUsage.UniformBuffer, false)]
        [DataRow(BufferUsage.UniformBuffer, true)]
        [DataRow(BufferUsage.UniformBuffer | BufferUsage.Dynamic, false)]
        [DataRow(BufferUsage.UniformBuffer | BufferUsage.Dynamic, true)]
        [DataRow(BufferUsage.VertexBuffer, false)]
        [DataRow(BufferUsage.VertexBuffer, true)]
        [DataRow(BufferUsage.VertexBuffer | BufferUsage.Dynamic, false)]
        [DataRow(BufferUsage.VertexBuffer | BufferUsage.Dynamic, true)]
        [DataRow(BufferUsage.IndexBuffer, false)]
        [DataRow(BufferUsage.IndexBuffer, true)]
        [DataRow(BufferUsage.IndirectBuffer, false)]
        [DataRow(BufferUsage.IndirectBuffer, true)]
        [DataRow(BufferUsage.Staging, false)]
        [DataRow(BufferUsage.Staging, true)]
        public unsafe void UpdateBuffer_ZeroSize(BufferUsage usage, bool useCommandListUpdate)
        {
            DeviceBuffer buffer = CreateBuffer(1024, usage);

            byte[] initialData = Enumerable.Range(0, 1024).Select(i => (byte)i).ToArray();
            byte[] otherData = Enumerable.Range(0, 1024).Select(i => (byte)(i + 10)).ToArray();
            GD.UpdateBuffer(buffer, 0, initialData);

            if (useCommandListUpdate)
            {
                using var cbp = GD.CreateCommandBufferPool();
                using var cl = cbp.CreateCommandBuffer();
                cl.Begin();
                fixed (byte* dataPtr = otherData)
                {
                    cl.UpdateBuffer(buffer, 0, (IntPtr)dataPtr, 0);
                }

                cl.End();
                GD.SubmitCommands(cl);
                GD.WaitForIdle();
            }
            else
            {
                fixed (byte* dataPtr = otherData)
                {
                    GD.UpdateBuffer(buffer, 0, (IntPtr)dataPtr, 0);
                }
            }

            DeviceBuffer readback = GetReadback(buffer);

            MappedResourceView<byte> readMap = GD.Map<byte>(readback, MapMode.Read);
            for (int i = 0; i < 1024; i++)
            {
                Assert.AreEqual((byte)i, readMap[i]);
            }

            GD.Unmap(readback);
        }

        private DeviceBuffer CreateBuffer(uint size, BufferUsage usage)
        {
            return GD.CreateBuffer(new BufferDescription(size, usage));
        }
    }
}

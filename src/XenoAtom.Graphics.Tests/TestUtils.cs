using System;
using System.Text;
using XenoAtom.Graphics.Utilities;

namespace XenoAtom.Graphics.Tests
{
    public static class TestUtils
    {
        public static GraphicsDevice CreateVulkanDevice()
        {
            return GraphicsDevice.Create(new GraphicsDeviceOptions(true));
        }

        internal static unsafe string GetString(byte* stringStart)
        {
            int characters = 0;
            while (stringStart[characters] != 0)
            {
                characters++;
            }

            return Encoding.UTF8.GetString(stringStart, characters);
        }
    }

    public abstract class GraphicsDeviceTestBase<T> : IDisposable where T : GraphicsDeviceCreator
    {
        private readonly GraphicsDevice _gd;
        private readonly DisposeCollectorResourceFactory _factory;

        public GraphicsDevice GD => _gd;
        public ResourceFactory RF => _factory;

        public GraphicsDeviceTestBase()
        {
            Activator.CreateInstance<T>().CreateGraphicsDevice(out _gd);
            _factory = new DisposeCollectorResourceFactory(_gd.ResourceFactory);
        }

        protected DeviceBuffer GetReadback(DeviceBuffer buffer)
        {
            DeviceBuffer readback;
            if ((buffer.Usage & BufferUsage.Staging) != 0)
            {
                readback = buffer;
            }
            else
            {
                readback = RF.CreateBuffer(new BufferDescription(buffer.SizeInBytes, BufferUsage.Staging));
                CommandList cl = RF.CreateCommandList();
                cl.Begin();
                cl.CopyBuffer(buffer, 0, readback, 0, buffer.SizeInBytes);
                cl.End();
                GD.SubmitCommands(cl);
                GD.WaitForIdle();
            }

            return readback;
        }

        protected Texture GetReadback(Texture texture)
        {
            if ((texture.Usage & TextureUsage.Staging) != 0)
            {
                return texture;
            }
            else
            {
                uint layers = texture.ArrayLayers;
                if ((texture.Usage & TextureUsage.Cubemap) != 0)
                {
                    layers *= 6;
                }
                TextureDescription desc = new TextureDescription(
                    texture.Width, texture.Height, texture.Depth,
                    texture.MipLevels, layers,
                    texture.Format,
                    TextureUsage.Staging, texture.Type);
                Texture readback = RF.CreateTexture(ref desc);
                CommandList cl = RF.CreateCommandList();
                cl.Begin();
                cl.CopyTexture(texture, readback);
                cl.End();
                GD.SubmitCommands(cl);
                GD.WaitForIdle();
                return readback;
            }
        }

        public void Dispose()
        {
            GD.WaitForIdle();
            _factory.DisposeCollector.DisposeAll();
            GD.Dispose();
        }
    }

    public interface GraphicsDeviceCreator
    {
        void CreateGraphicsDevice(out GraphicsDevice gd);
    }

    public class VulkanDeviceCreator : GraphicsDeviceCreator
    {
        public void CreateGraphicsDevice(out GraphicsDevice gd)
        {
            gd = TestUtils.CreateVulkanDevice();
        }
    }
}

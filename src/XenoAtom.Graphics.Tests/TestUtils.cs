using System;
using System.IO;
using System.Text;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using XenoAtom.Graphics.Utilities;
using Xunit;
using Xunit.Abstractions;

namespace XenoAtom.Graphics.Tests
{
    public static class TestUtils
    {
        public static GraphicsDevice CreateVulkanDevice(DebugLogDelegate log)
        {
            var manager = GraphicsManager.Create(new GraphicsManagerOptions(true)
            {
                DebugLog = log
            });

            Assert.True(manager.Adapters.Length > 0, "No Graphics adapters found");


            var adapter = manager.Adapters[0];
            return adapter.CreateDevice();
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
        private readonly ITestOutputHelper _textOutputHelper;
        private readonly GraphicsDevice _gd;
        private readonly DisposeCollectorResourceFactory _factory;
        private bool _hasWarningOrErrorLogs;

        public GraphicsDevice GD => _gd;
        public ResourceFactory RF => _factory;

        public GraphicsDeviceTestBase(ITestOutputHelper textOutputHelper)
        {
            _textOutputHelper = textOutputHelper;
            Activator.CreateInstance<T>().CreateGraphicsDevice(out _gd, DebugLogImpl);
            _factory = new DisposeCollectorResourceFactory(_gd.ResourceFactory);
        }


        private void DebugLogImpl(DebugLogLevel debugLogLevel, DebugLogKind kind, string message)
        {
            // Skip warning messages related to ICD during the tests
            if (message.Contains("in ICD") && debugLogLevel == DebugLogLevel.Warning)
            {
                return;
            }
            _hasWarningOrErrorLogs = true;
            _textOutputHelper.WriteLine($"[{debugLogLevel}] {kind} - {message}");
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

            var manager = GD.Adapter.Manager;
            manager.Dispose();
            
            Assert.False(_hasWarningOrErrorLogs);
        }
    }

    public interface GraphicsDeviceCreator
    {
        void CreateGraphicsDevice(out GraphicsDevice gd, DebugLogDelegate log);
    }

    public class VulkanDeviceCreator : GraphicsDeviceCreator
    {
        public void CreateGraphicsDevice(out GraphicsDevice gd, DebugLogDelegate log)
        {
            gd = TestUtils.CreateVulkanDevice(log);
        }
    }
}

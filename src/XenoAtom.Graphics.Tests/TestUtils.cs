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
        public static GraphicsDevice CreateVulkanDevice(DebugLogDelegate log, Action<GraphicsObject> resourcesCreated)
        {
            var manager = GraphicsManager.Create(new GraphicsManagerOptions(true)
            {
                DebugLog = log
            });

            var adapter = manager.GetBestAdapter();
            return adapter.CreateDevice(new GraphicsDeviceOptions()
            {
                OnResourceCreated = resourcesCreated
            });
        }
    }

    public abstract class GraphicsDeviceTestBase : IDisposable
    {
        private readonly ITestOutputHelper _textOutputHelper;
        private readonly GraphicsDevice _gd;
        private readonly DisposeCollector _disposeCollector;
        private bool _hasWarningOrErrorLogs;

        public GraphicsDevice GD => _gd;

        public GraphicsDeviceTestBase(ITestOutputHelper textOutputHelper)
        {
            _textOutputHelper = textOutputHelper;
            _disposeCollector = new DisposeCollector();
            _gd = TestUtils.CreateVulkanDevice(DebugLogImpl, o => _disposeCollector.Add(o));
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
                readback = GD.CreateBuffer(new BufferDescription(buffer.SizeInBytes, BufferUsage.Staging));
                CommandList cl = GD.CreateCommandList();
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
                    TextureUsage.Staging, texture.Kind);
                Texture readback = GD.CreateTexture(desc);
                CommandList cl = GD.CreateCommandList();
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
            _disposeCollector.DisposeAll();
            GD.Dispose();

            var manager = GD.Adapter.Manager;
            manager.Dispose();
            
            Assert.False(_hasWarningOrErrorLogs);
        }
    }
}

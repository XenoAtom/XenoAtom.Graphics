using System;
using System.IO;
using System.Text;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XenoAtom.Graphics.Utilities;

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

    [TestClass]
    public abstract class GraphicsDeviceTestBase : IDisposable
    {
        private readonly GraphicsDevice _gd;
        private readonly DisposeCollector _disposeCollector;
        private bool _hasWarningOrErrorLogs;
        private readonly StringBuilder _logBuilder = new();

        public GraphicsDevice GD => _gd;

        public TestContext? TestContext { get; set; }
        
        public GraphicsDeviceTestBase()
        {
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
            TestContext?.WriteLine($"[{debugLogLevel}] {kind} - {message}");
            _logBuilder.AppendLine($"[{debugLogLevel}] {kind} - {message}");
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
                using (var cbp = GD.CreateCommandBufferPool())
                using (var cb = cbp.CreateCommandBuffer())
                {
                    cb.Begin();
                    cb.CopyBuffer(buffer, 0, readback, 0, buffer.SizeInBytes);
                    cb.End();
                    GD.SubmitCommands(cb);
                    GD.WaitForIdle();
                }
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
                using (var cbp = GD.CreateCommandBufferPool())
                using (var cb = cbp.CreateCommandBuffer())
                {
                    cb.Begin();
                    cb.CopyTexture(texture, readback);
                    cb.End();
                    GD.SubmitCommands(cb);
                    GD.WaitForIdle();
                }

                return readback;
            }
        }

        public void Dispose()
        {
            GD.WaitForIdle();

            // Uncomment to get more statistics about the memory manager

            //var builder = new StringBuilder();
            //GD.DumpStatistics(builder);
            //_textOutputHelper.WriteLine(builder.ToString());

            _disposeCollector.DisposeAll();
            GD.Dispose();

            var manager = GD.Adapter.Manager;
            manager.Dispose();
            
            Assert.IsFalse(_hasWarningOrErrorLogs, $"There are unexpected warning/errors in the logs:\n{_logBuilder}");
        }
    }
}

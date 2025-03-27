using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace XenoAtom.Graphics.Tests
{
    [TestClass]
    public class FormatSizeHelpersTests : IDisposable
    {
        private TraceListener[] _traceListeners;

        public FormatSizeHelpersTests()
        {
            // temporarily disables debug trace listeners to prevent Debug.Assert
            // from causing test failures in cases where we're explicitly trying
            // to test invalid inputs
            _traceListeners = new TraceListener[Trace.Listeners.Count];
            Trace.Listeners.CopyTo(_traceListeners, 0);
            Trace.Listeners.Clear();
        }

        public void Dispose()
        {
            Trace.Listeners.AddRange(_traceListeners);
        }

        [TestMethod]
        public void GetSizeInBytes_DefinedForAllVertexElementFormats()
        {
            foreach (VertexElementFormat format in System.Enum.GetValues(typeof(VertexElementFormat)))
            {
                Assert.IsTrue(0 < FormatSizeHelpers.GetSizeInBytes(format));
            }
        }

        private static HashSet<PixelFormat> CompressedPixelFormats = new HashSet<PixelFormat>() {
            PixelFormat.BC1_Rgba_UNorm,
            PixelFormat.BC1_Rgba_UNorm_SRgb,
            PixelFormat.BC1_Rgb_UNorm,
            PixelFormat.BC1_Rgb_UNorm_SRgb,

            PixelFormat.BC2_UNorm,
            PixelFormat.BC2_UNorm_SRgb,

            PixelFormat.BC3_UNorm,
            PixelFormat.BC3_UNorm_SRgb,

            PixelFormat.BC4_SNorm,
            PixelFormat.BC4_UNorm,

            PixelFormat.BC5_SNorm,
            PixelFormat.BC5_UNorm,

            PixelFormat.BC7_UNorm,
            PixelFormat.BC7_UNorm_SRgb,

            PixelFormat.ETC2_R8_G8_B8_A1_UNorm,
            PixelFormat.ETC2_R8_G8_B8_A8_UNorm,
            PixelFormat.ETC2_R8_G8_B8_UNorm,
        };
        private static IEnumerable<PixelFormat> UncompressedPixelFormats
            = System.Enum.GetValues(typeof(PixelFormat)).Cast<PixelFormat>()
                .Where(format => !CompressedPixelFormats.Contains(format));
        public static IEnumerable<object[]> CompressedPixelFormatMemberData => CompressedPixelFormats.Select(format => new object[] { format });
        public static IEnumerable<object[]> UncompressedPixelFormatMemberData => UncompressedPixelFormats.Select(format => new object[] { format });

        [TestMethod]
        [DynamicData(nameof(UncompressedPixelFormatMemberData))]
        public void GetSizeInBytes_DefinedForAllNonCompressedPixelFormats(PixelFormat format)
        {
            Assert.IsTrue(0 < FormatSizeHelpers.GetSizeInBytes(format));
        }

        [TestMethod]
        [DynamicData(nameof(CompressedPixelFormatMemberData))]
        public void GetSizeInBytes_ThrowsForAllCompressedPixelFormats(PixelFormat format)
        {
            Assert.Throws<GraphicsException>(() => FormatSizeHelpers.GetSizeInBytes(format));
        }
    }
}

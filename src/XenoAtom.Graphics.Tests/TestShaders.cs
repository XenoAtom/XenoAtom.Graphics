using System;
using System.IO;

namespace XenoAtom.Graphics.Tests
{
    internal static class TestShaders
    {
        public static Shader[] LoadVertexFragment(ResourceFactory factory, string setName)
        {
            return factory.CreateFromSpirv(
                new ShaderDescription(ShaderStages.Vertex, File.ReadAllBytes(GetPath(setName, ShaderStages.Vertex)), "main"u8),
                new ShaderDescription(ShaderStages.Fragment, File.ReadAllBytes(GetPath(setName, ShaderStages.Fragment)), "main"u8),
                new CrossCompileOptions(false, false, new SpecializationConstant[]
                {
                    new SpecializationConstant(100, false)
                }));
        }

        public static Shader LoadCompute(ResourceFactory factory, string setName)
        {
            return factory.CreateFromSpirv(
                new ShaderDescription(ShaderStages.Compute, File.ReadAllBytes(GetPath(setName, ShaderStages.Compute)), "main"u8),
                new CrossCompileOptions(false, false, new SpecializationConstant[]
                {
                    new SpecializationConstant(100, false)
                }));
        }

        public static string GetPath(string setName, ShaderStages stage)
        {
            return Path.Combine(
                AppContext.BaseDirectory,
                "Shaders",
                $"{setName}.{stage.ToString().ToLowerInvariant().Substring(0, 4)}.spv");
        }
    }
}

using System;
using XenoAtom.Interop;

namespace XenoAtom.Graphics
{
    /// <summary>
    /// Describes a <see cref="Shader"/>, for creation using a <see cref="ResourceFactory"/>.
    /// </summary>
    public ref struct ShaderDescription
    {
        /// <summary>
        /// The shader stage this instance describes.
        /// </summary>
        public ShaderStages Stage;

        /// <summary>
        /// An array containing the raw shader bytes.
        /// For Direct3D11 shaders, this array must contain HLSL bytecode or HLSL text.
        /// For Vulkan shaders, this array must contain SPIR-V bytecode.
        /// For OpenGL and OpenGL ES shaders, this array must contain the ASCII-encoded text of the shader code.
        /// For Metal shaders, this array must contain Metal bitcode (a "metallib" file), or UTF8-encoded Metal shading language
        /// text.
        /// </summary>
        public ReadOnlySpan<byte> ShaderBytes;

        /// <summary>
        /// The name of the entry point function in the shader module to be used in this stage.
        /// </summary>
        public ReadOnlyMemoryUtf8 EntryPoint;

        /// <summary>
        /// Indicates whether the shader should be debuggable. This flag only has an effect if <see cref="ShaderBytes"/> contains
        /// shader code that will be compiled.
        /// </summary>
        public bool Debug;

        /// <summary>
        /// Constructs a new ShaderDescription with a default `main` entry point and no debug information.
        /// </summary>
        /// <param name="stage">The shader stage to create.</param>
        /// <param name="shaderBytes">An array containing the raw shader bytes.</param>
        public ShaderDescription(ShaderStages stage, ReadOnlySpan<byte> shaderBytes)
        {
            Stage = stage;
            ShaderBytes = shaderBytes;
            EntryPoint = "main"u8;
            Debug = false;
        }

        /// <summary>
        /// Constructs a new ShaderDescription.
        /// </summary>
        /// <param name="stage">The shader stage to create.</param>
        /// <param name="shaderBytes">An array containing the raw shader bytes.</param>
        /// <param name="entryPoint">The name of the entry point function in the shader module to be used in this stage.</param>
        public ShaderDescription(ShaderStages stage, ReadOnlySpan<byte> shaderBytes, ReadOnlyMemoryUtf8 entryPoint)
        {
            Stage = stage;
            ShaderBytes = shaderBytes;
            EntryPoint = entryPoint;
            Debug = false;
        }

        /// <summary>
        /// Constructs a new ShaderDescription.
        /// </summary>
        /// <param name="stage">The shader stage to create.</param>
        /// <param name="shaderBytes">An array containing the raw shader bytes.</param>
        /// <param name="entryPoint">The name of the entry point function in the shader module to be used in this stage.</param>
        /// <param name="debug">Indicates whether the shader should be debuggable. This flag only has an effect if
        /// <paramref name="shaderBytes"/> contains shader code that will be compiled.</param>
        public ShaderDescription(ShaderStages stage, ReadOnlySpan<byte> shaderBytes, ReadOnlyMemoryUtf8 entryPoint, bool debug)
        {
            Stage = stage;
            ShaderBytes = shaderBytes;
            EntryPoint = entryPoint;
            Debug = debug;
        }
    }
}

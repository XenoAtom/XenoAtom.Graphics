// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;
using System.Text;

namespace XenoAtom.Graphics.Tests;

/// <summary>
/// Contains extension methods for loading <see cref="Shader"/> modules from SPIR-V bytecode.
/// </summary>
public static class ResourceFactoryExtensions
{
    /// <summary>
    /// Creates a vertex and fragment shader pair from the given <see cref="ShaderDescription"/> pair containing SPIR-V
    /// bytecode or GLSL source code.
    /// </summary>
    /// <param name="device">The <see cref="ResourceFactory"/> used to compile the translated shader code.</param>
    /// <param name="vertexShaderDescription">The vertex shader's description. <see cref="ShaderDescription.ShaderBytes"/>
    /// should contain SPIR-V bytecode or Vulkan-style GLSL source code which can be compiled to SPIR-V.</param>
    /// <param name="fragmentShaderDescription">The fragment shader's description.
    /// <see cref="ShaderDescription.ShaderBytes"/> should contain SPIR-V bytecode or Vulkan-style GLSL source code which
    /// can be compiled to SPIR-V.</param>
    /// <returns>A two-element array, containing the vertex shader (element 0) and the fragment shader (element 1).</returns>
    public static Shader[] CreateFromSpirv(
        this GraphicsDevice device,
        ShaderDescription vertexShaderDescription,
        ShaderDescription fragmentShaderDescription)
    {
        return CreateFromSpirv(device, vertexShaderDescription, fragmentShaderDescription, new CrossCompileOptions());
    }

    /// <summary>
    /// Creates a vertex and fragment shader pair from the given <see cref="ShaderDescription"/> pair containing SPIR-V
    /// bytecode or GLSL source code.
    /// </summary>
    /// <param name="device">The <see cref="ResourceFactory"/> used to compile the translated shader code.</param>
    /// <param name="vertexShaderDescription">The vertex shader's description. <see cref="ShaderDescription.ShaderBytes"/>
    /// should contain SPIR-V bytecode or Vulkan-style GLSL source code which can be compiled to SPIR-V.</param>
    /// <param name="fragmentShaderDescription">The fragment shader's description.
    /// <see cref="ShaderDescription.ShaderBytes"/> should contain SPIR-V bytecode or Vulkan-style GLSL source code which
    /// can be compiled to SPIR-V.</param>
    /// <param name="options">The <see cref="CrossCompileOptions"/> which will control the parameters used to translate the
    /// shaders from SPIR-V to the target language.</param>
    /// <returns>A two-element array, containing the vertex shader (element 0) and the fragment shader (element 1).</returns>
    public static Shader[] CreateFromSpirv(
        this GraphicsDevice device,
        ShaderDescription vertexShaderDescription,
        ShaderDescription fragmentShaderDescription,
        CrossCompileOptions options)
    {
        GraphicsBackend backend = device.BackendType;
        vertexShaderDescription.ShaderBytes = EnsureSpirv(vertexShaderDescription);
        fragmentShaderDescription.ShaderBytes = EnsureSpirv(fragmentShaderDescription);

        return new Shader[]
        {
                device.CreateShader(vertexShaderDescription),
                device.CreateShader(fragmentShaderDescription)
        };
    }

    /// <summary>
    /// Creates a compute shader from the given <see cref="ShaderDescription"/> containing SPIR-V bytecode or GLSL source
    /// code.
    /// </summary>
    /// <param name="device">The <see cref="ResourceFactory"/> used to compile the translated shader code.</param>
    /// <param name="computeShaderDescription">The compute shader's description.
    /// <see cref="ShaderDescription.ShaderBytes"/> should contain SPIR-V bytecode or Vulkan-style GLSL source code which
    /// can be compiled to SPIR-V.</param>
    /// <returns>The compiled compute <see cref="Shader"/>.</returns>
    public static Shader CreateFromSpirv(
        this GraphicsDevice device,
        ShaderDescription computeShaderDescription)
    {
        return CreateFromSpirv(device, computeShaderDescription, new CrossCompileOptions());
    }

    /// <summary>
    /// Creates a compute shader from the given <see cref="ShaderDescription"/> containing SPIR-V bytecode or GLSL source
    /// code.
    /// </summary>
    /// <param name="device">The <see cref="ResourceFactory"/> used to compile the translated shader code.</param>
    /// <param name="computeShaderDescription">The compute shader's description.
    /// <see cref="ShaderDescription.ShaderBytes"/> should contain SPIR-V bytecode or Vulkan-style GLSL source code which
    /// can be compiled to SPIR-V.</param>
    /// <param name="options">The <see cref="CrossCompileOptions"/> which will control the parameters used to translate the
    /// shaders from SPIR-V to the target language.</param>
    /// <returns>The compiled compute <see cref="Shader"/>.</returns>
    public static Shader CreateFromSpirv(
        this GraphicsDevice device,
        ShaderDescription computeShaderDescription,
        CrossCompileOptions options)
    {
        GraphicsBackend backend = device.BackendType;
        computeShaderDescription.ShaderBytes = EnsureSpirv(computeShaderDescription);
        return device.CreateShader(computeShaderDescription);
    }

    private static unsafe byte[] EnsureSpirv(ShaderDescription description)
    {
        return description.ShaderBytes;
        //if (Util.HasSpirvHeader(description.ShaderBytes))
        //{
        //    return description.ShaderBytes;
        //}
        //else
        //{
        //    fixed (byte* sourceAsciiPtr = description.ShaderBytes)
        //    {
        //        SpirvCompilationResult glslCompileResult = SpirvCompilation.CompileGlslToSpirv(
        //            (uint)description.ShaderBytes.Length,
        //            sourceAsciiPtr,
        //            null,
        //            description.Stage,
        //            description.Debug,
        //            0,
        //            null);
        //        return glslCompileResult.SpirvBytes;
        //    }
        //}
    }
}



/// <summary>
/// An object used to control the parameters of shader translation from SPIR-V to some target language.
/// </summary>
public class CrossCompileOptions
{
    /// <summary>
    /// Indicates whether or not the compiled shader output should include a clip-space Z-range fixup at the end of the
    /// vertex shader.
    /// If true, then the shader will include code that assumes the clip space needs to be corrected from the
    /// "wrong" range into the "right" range for the particular type of shader. For example, if an OpenGL shader is being
    /// generated, then the vertex shader will include a fixup that converts the depth range from [0, 1] to [-1, 1].
    /// If a Direct3D shader is being generated, then the vertex shader will include a fixup that converts the depth range
    /// from [-1, 1] to [0, 1].
    /// </summary>
    public bool FixClipSpaceZ { get; set; }
    /// <summary>
    /// Indicates whether or not the compiled shader output should include a fixup at the end of the vertex shader which
    /// inverts the clip-space Y value.
    /// </summary>
    public bool InvertVertexOutputY { get; set; }
    /// <summary>
    /// Indicates whether all resource names should be forced into a normalized form. This has functional impact
    /// on compilation targets where resource names are meaningful, like GLSL.
    /// </summary>
    public bool NormalizeResourceNames { get; set; }
    /// <summary>
    /// An array of <see cref="SpecializationConstant"/> which will be substituted into the shader as new constants. Each
    /// element in the array will be matched by ID with the SPIR-V specialization constants defined in the shader.
    /// </summary>
    public SpecializationConstant[] Specializations { get; set; }

    /// <summary>
    /// Constructs a new <see cref="CrossCompileOptions"/> with default values.
    /// </summary>
    public CrossCompileOptions()
    {
        Specializations = Array.Empty<SpecializationConstant>();
    }

    /// <summary>
    /// Constructs a new <see cref="CrossCompileOptions"/>, used to control the parameters of shader translation.
    /// </summary>
    /// <param name="fixClipSpaceZ">Indicates whether or not the compiled shader output should include a clip-space Z-range
    /// fixup at the end of the vertex shader.</param>
    /// <param name="invertVertexOutputY">Indicates whether or not the compiled shader output should include a fixup at the
    /// end of the vertex shader which inverts the clip-space Y value.</param>
    public CrossCompileOptions(bool fixClipSpaceZ, bool invertVertexOutputY)
        : this(fixClipSpaceZ, invertVertexOutputY, Array.Empty<SpecializationConstant>())
    {
    }

    /// <summary>
    /// Constructs a new <see cref="CrossCompileOptions"/>, used to control the parameters of shader translation.
    /// </summary>
    /// <param name="fixClipSpaceZ">Indicates whether or not the compiled shader output should include a clip-space Z-range
    /// fixup at the end of the vertex shader.</param>
    /// <param name="invertVertexOutputY">Indicates whether or not the compiled shader output should include a fixup at the
    /// end of the vertex shader which inverts the clip-space Y value.</param>
    /// <param name="normalizeResourceNames">Indicates whether all resource names should be forced into a normalized form.
    /// This has functional impact on compilation targets where resource names are meaningful, like GLSL.</param>
    public CrossCompileOptions(bool fixClipSpaceZ, bool invertVertexOutputY, bool normalizeResourceNames)
        : this(fixClipSpaceZ, invertVertexOutputY, normalizeResourceNames, Array.Empty<SpecializationConstant>())
    {
    }

    /// <summary>
    /// Constructs a new <see cref="CrossCompileOptions"/>, used to control the parameters of shader translation.
    /// </summary>
    /// <param name="fixClipSpaceZ">Indicates whether or not the compiled shader output should include a clip-space Z-range
    /// fixup at the end of the vertex shader.</param>
    /// <param name="invertVertexOutputY">Indicates whether or not the compiled shader output should include a fixup at the
    /// end of the vertex shader which inverts the clip-space Y value.</param>
    /// <param name="specializations">An array of <see cref="SpecializationConstant"/> which will be substituted into the
    /// shader as new constants.</param>
    public CrossCompileOptions(bool fixClipSpaceZ, bool invertVertexOutputY, params SpecializationConstant[] specializations)
    {
        FixClipSpaceZ = fixClipSpaceZ;
        InvertVertexOutputY = invertVertexOutputY;
        Specializations = specializations;
    }

    /// <summary>
    /// Constructs a new <see cref="CrossCompileOptions"/>, used to control the parameters of shader translation.
    /// </summary>
    /// <param name="fixClipSpaceZ">Indicates whether or not the compiled shader output should include a clip-space Z-range
    /// fixup at the end of the vertex shader.</param>
    /// <param name="invertVertexOutputY">Indicates whether or not the compiled shader output should include a fixup at the
    /// end of the vertex shader which inverts the clip-space Y value.</param>
    /// <param name="normalizeResourceNames">Indicates whether all resource names should be forced into a normalized form.
    /// This has functional impact on compilation targets where resource names are meaningful, like GLSL.</param>
    /// <param name="specializations">An array of <see cref="SpecializationConstant"/> which will be substituted into the
    /// shader as new constants.</param>
    public CrossCompileOptions(
        bool fixClipSpaceZ,
        bool invertVertexOutputY,
        bool normalizeResourceNames,
        params SpecializationConstant[] specializations)
    {
        FixClipSpaceZ = fixClipSpaceZ;
        InvertVertexOutputY = invertVertexOutputY;
        NormalizeResourceNames = normalizeResourceNames;
        Specializations = specializations;
    }
}

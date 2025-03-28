using System;

namespace XenoAtom.Graphics;

/// <summary>
/// Describes a compute <see cref="Pipeline"/>, for creation using a <see cref="ResourceFactory"/>.
/// </summary>
public readonly struct ComputePipelineDescription
{
    /// <summary>
    /// The compute <see cref="Shader"/> to be used in the Pipeline. This must be a Shader with
    /// <see cref="ShaderStages.Compute"/>.
    /// </summary>
    public Shader ComputeShader { get; init; }

    /// <summary>
    /// An array of <see cref="ResourceLayout"/>, which controls the layout of shader resoruces in the <see cref="Pipeline"/>.
    /// </summary>
    public ResourceLayout[] ResourceLayouts { get; init; }

    /// <summary>
    /// An array of <see cref="SpecializationConstant"/> used to override specialization constants in the created
    /// <see cref="Pipeline"/>. Each element in this array describes a single ID-value pair, which will be matched with the
    /// constants specified in the <see cref="Shader"/>.
    /// </summary>
    public SpecializationConstant[]? Specializations { get; init; }

    /// <summary>
    /// Gets or sets the push constant ranges.
    /// </summary>
    public PushConstantRangeDescription[] PushConstantRanges { get; init; }

    /// <summary>
    /// This is the requested subgroup size for the compute shader. Must be a power of 2 and be in the range <see cref="GraphicsDeviceFeatures.MinSubgroupSize"/> and <see cref="GraphicsDeviceFeatures.MaxSubgroupSize"/>. Default is 0 means the default subgroup size <see cref="GraphicsDeviceFeatures.SubgroupSize"/>.
    /// </summary>
    public uint RequestedSubgroupSize { get; init; }

    /// <summary>
    /// Constructs a new ComputePipelineDescription.
    /// </summary>
    /// <param name="computeShader">The compute <see cref="Shader"/> to be used in the Pipeline. This must be a Shader with
    /// <see cref="ShaderStages.Compute"/>.</param>
    /// <param name="resourceLayouts">The set of resource layouts available to the Pipeline.</param>
    public ComputePipelineDescription(
        Shader computeShader,
        ResourceLayout[] resourceLayouts)
    {
        ComputeShader = computeShader;
        ResourceLayouts = resourceLayouts;
        Specializations = null;
        PushConstantRanges = [];
    }

    /// <summary>
    /// Constructs a new ComputePipelineDescription.
    /// </summary>
    /// <param name="shaderStage">The compute <see cref="Shader"/> to be used in the Pipeline. This must be a Shader with
    /// <see cref="ShaderStages.Compute"/>.</param>
    /// <param name="resourceLayout">The resource layout available to the Pipeline.</param>
    public ComputePipelineDescription(
        Shader shaderStage,
        ResourceLayout resourceLayout)
    {
        ComputeShader = shaderStage;
        ResourceLayouts = [resourceLayout];
        Specializations = null;
        PushConstantRanges = [];
    }

    /// <summary>
    /// Constructs a new ComputePipelineDescription.
    /// </summary>
    /// <param name="shaderStage">The compute <see cref="Shader"/> to be used in the Pipeline. This must be a Shader with
    /// <see cref="ShaderStages.Compute"/>.</param>
    /// <param name="resourceLayout">The resource layout available to the Pipeline.</param>
    /// <param name="specializations">An array of <see cref="SpecializationConstant"/> used to override specialization
    /// constants in the created <see cref="Pipeline"/>. Each element in this array describes a single ID-value pair, which
    /// will be matched with the constants specified in the <see cref="Shader"/>.</param>
    public ComputePipelineDescription(
        Shader shaderStage,
        ResourceLayout resourceLayout,
        SpecializationConstant[] specializations)
    {
        ComputeShader = shaderStage;
        ResourceLayouts = new[] { resourceLayout };
        Specializations = specializations;
        PushConstantRanges = [];
    }
}
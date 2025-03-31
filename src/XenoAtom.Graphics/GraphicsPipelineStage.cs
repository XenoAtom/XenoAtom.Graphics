namespace XenoAtom.Graphics;

/// <summary>
/// Specifies the stages of a graphics pipeline.
/// </summary>
public enum GraphicsPipelineStage
{
    /// <summary>
    /// No specific stage.
    /// </summary>
    None = 0,
    /// <summary>
    /// The top of the pipeline.
    /// </summary>
    TopOfPipe = 1,
    /// <summary>
    /// The bottom of the pipeline.
    /// </summary>
    BottomOfPipe = 2,
    /// <summary>
    /// The stage of the pipeline where vertex shading occurs.
    /// </summary>
    VertexShader = 3,
    /// <summary>
    /// The stage of the pipeline where tessellation control shading occurs.
    /// </summary>
    TessellationControlShader = 4,
    /// <summary>
    /// The stage of the pipeline where tessellation evaluation shading occurs.
    /// </summary>
    TessellationEvaluationShader = 5,
    /// <summary>
    /// The stage of the pipeline where geometry shading occurs.
    /// </summary>
    GeometryShader = 6,
    /// <summary>
    /// The stage of the pipeline where fragment shading occurs.
    /// </summary>
    FragmentShader = 7,
    /// <summary>
    /// The stage of the pipeline where early fragment tests (depth and stencil tests before fragment shading) occur.
    /// </summary>
    EarlyFragmentTests = 8,
    /// <summary>
    /// The stage of the pipeline where late fragment tests (depth and stencil tests after fragment shading) occur.
    /// </summary>
    LateFragmentTests = 9,
    /// <summary>
    /// The stage of the pipeline where color attachment output occurs.
    /// </summary>
    ColorAttachmentOutput = 10,
    /// <summary>
    /// The stage of the pipeline where compute shading occurs.
    /// </summary>
    ComputeShader = 11,
    /// <summary>
    /// The stage of the pipeline where transfer operations occur.
    /// </summary>
    Transfer = 12,
    /// <summary>
    /// The stage of the pipeline where all graphics pipeline stages occur.
    /// </summary>
    AllGraphics = 13,
    /// <summary>
    /// The stage of the pipeline where all commands occur.
    /// </summary>
    AllCommands = 14
}

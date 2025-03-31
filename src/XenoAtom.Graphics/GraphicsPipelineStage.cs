// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

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
}
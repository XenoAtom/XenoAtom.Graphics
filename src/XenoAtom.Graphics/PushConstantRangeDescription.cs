// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace XenoAtom.Graphics;

/// <summary>
/// Describes a range of push constants.
/// </summary>
/// <param name="ShaderStage">Defines which stages the push constant is valid for.</param>
/// <param name="Offset">The offset into the constant buffer.</param>
/// <param name="Size">The size of the data.</param>
public readonly record struct PushConstantRangeDescription(ShaderStages ShaderStage, uint Offset, uint Size);

// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace XenoAtom.Graphics;

/// <summary>
/// Represents an index for a graphics query.
/// </summary>
public record struct GraphicsQueryIndex(uint Value)
{
    /// <summary>
    /// Adds an integer value to a <see cref="GraphicsQueryIndex"/>.
    /// </summary>
    /// <param name="index">The query index.</param>
    /// <param name="value">The value to add.</param>
    /// <returns>A new <see cref="GraphicsQueryIndex"/> with the added value.</returns>
    public static GraphicsQueryIndex operator +(GraphicsQueryIndex index, int value) => new((uint)(index.Value + value));
}
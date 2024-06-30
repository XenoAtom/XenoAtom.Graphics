// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using XenoAtom.Graphics.Vk;

namespace XenoAtom.Graphics;

/// <summary>
/// Represents an abstract graphics manager, capable of iterating over <see cref="GraphicsAdapter"/>.
/// </summary>
public abstract class GraphicsManager : GraphicsObject
{
    internal GraphicsManager()
    {
    }

    /// <summary>
    /// Gets the list of available <see cref="GraphicsAdapter"/>.
    /// </summary>
    public abstract ReadOnlySpan<GraphicsAdapter> Adapters { get; }
    
    /// <summary>
    /// Tries to get the best adapter (first from discrete GPU, then integrated GPU, then virtual GPU, CPU, other).
    /// </summary>
    /// <param name="adapter"></param>
    /// <returns><c>true</c> if an adapter was found; otherwise <c>false</c></returns>
    public bool TryGetBestAdapter([NotNullWhen(true)] out GraphicsAdapter? adapter)
    {
        adapter = null;
        var kind = (int)GraphicsAdapterKind.Other + 1;
        foreach (var adapter1 in Adapters)
        {
            if ((int)adapter1.Kind < kind)
            {
                adapter = adapter1;
                kind = (int)adapter1.Kind;
            }
        }

        return adapter != null;
    }

    /// <summary>
    /// Gets the best adapter (first from discrete GPU, then integrated GPU, then virtual GPU, CPU, other).
    /// </summary>
    /// <returns>The best adapter</returns>
    /// <exception cref="GraphicsException">If no adapter were found.</exception>
    public GraphicsAdapter GetBestAdapter()
    {
        if (TryGetBestAdapter(out var adapter))
        {
            return adapter;
        }

        throw new GraphicsException("No adapter found");
    }
    
    /// <summary>
    /// Creates a new <see cref="GraphicsManager"/> with the default options.
    /// </summary>
    public static GraphicsManager Create() => Create(new());


    /// <summary>
    /// Creates a new <see cref="GraphicsManager"/> with the specified options.
    /// </summary>
    /// <param name="options">Options for the graphics manager.</param>
    public static GraphicsManager Create(in GraphicsManagerOptions options) => new VkGraphicsManager(options);
}
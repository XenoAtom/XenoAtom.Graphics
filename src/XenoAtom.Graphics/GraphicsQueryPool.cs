// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace XenoAtom.Graphics;

/// <summary>
/// Represents a pool of graphics queries.
/// </summary>
public abstract class GraphicsQueryPool : GraphicsDeviceObject
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GraphicsQueryPool"/> class.
    /// </summary>
    /// <param name="device">The graphics device associated with this query pool.</param>
    /// <param name="kind">The kind of queries in this pool.</param>
    internal GraphicsQueryPool(GraphicsDevice device, GraphicsQueryKind kind) : base(device)
    {
        Kind = kind;
    }

    /// <summary>
    /// Gets the kind of queries in this pool.
    /// </summary>
    public GraphicsQueryKind Kind { get; }

    /// <summary>
    /// Gets the number of queries in this pool.
    /// </summary>
    public abstract uint QueryCount { get; }

    /// <summary>
    /// Gets the handle to the underlying native object.
    /// </summary>
    public abstract nint Handle { get; }

    /// <summary>
    /// Resets all queries in this pool.
    /// </summary>
    public void Reset() => Reset(default, QueryCount);

    /// <summary>
    /// Resets a range of queries in this pool.
    /// </summary>
    /// <param name="start">The starting index of the queries to reset.</param>
    /// <param name="count">The number of queries to reset.</param>
    public abstract void Reset(GraphicsQueryIndex start, uint count);
}

/// <summary>
/// Represents a pool of graphics queries with specific query data.
/// </summary>
/// <typeparam name="TQueryData">The type of the query data.</typeparam>
public abstract class GraphicsQueryPool<TQueryData> : GraphicsQueryPool where TQueryData : struct
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GraphicsQueryPool{TQueryData}"/> class.
    /// </summary>
    /// <param name="device">The graphics device associated with this query pool.</param>
    /// <param name="kind">The kind of queries in this pool.</param>
    internal GraphicsQueryPool(GraphicsDevice device, GraphicsQueryKind kind) : base(device, kind)
    {
    }

    /// <summary>
    /// Tries to get the query data for a specific query.
    /// </summary>
    /// <param name="query">The index of the query.</param>
    /// <param name="data">When this method returns, contains the query data if the operation succeeded.</param>
    /// <returns><c>true</c> if the query data was successfully retrieved; otherwise, <c>false</c>.</returns>
    public abstract bool TryGetQueryData(GraphicsQueryIndex query, out TQueryData data);
}
// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;
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
    /// Creates a new <see cref="GraphicsManager"/> with the default options.
    /// </summary>
    public static GraphicsManager Create() => Create(new());


    /// <summary>
    /// Creates a new <see cref="GraphicsManager"/> with the specified options.
    /// </summary>
    /// <param name="options">Options for the graphics manager.</param>
    public static GraphicsManager Create(GraphicsManagerOptions options) => new VkGraphicsManager(options, new VulkanManagerOptions());
}

/// <summary>
/// Represents an abstract graphics adapter, capable of creating <see cref="GraphicsDevice"/>.
/// </summary>
public abstract class GraphicsAdapter : GraphicsObject
{
    internal GraphicsAdapter(GraphicsManager manager)
    {
        Manager = manager;
    }

    /// <summary>
    /// Gets the manager associated with this adapter.
    /// </summary>
    public readonly GraphicsManager Manager;

    /// <summary>
    /// Gets the name of the device.
    /// </summary>
    public abstract string DeviceName { get; }

    /// <summary>
    /// Gets the name of the driver.
    /// </summary>
    public abstract string DriverName { get; }

    /// <summary>
    /// Gets information about the driver.
    /// </summary>
    public abstract string DriverInfo { get; }

    /// <summary>
    /// Gets the name of the device vendor.
    /// </summary>
    public abstract string VendorName { get; }

    /// <summary>
    /// Gets the version of the API supported by this adapter.
    /// </summary>
    public abstract GraphicsVersion ApiVersion { get; }

    /// <summary>
    /// Gets the version of the driver for this adapter.
    /// </summary>
    public abstract GraphicsVersion DriverVersion { get; }

    /// <summary>
    /// Creates a new <see cref="GraphicsDevice"/> with the default options.
    /// </summary>
    /// <returns></returns>
    public GraphicsDevice CreateDevice() => CreateDevice(new GraphicsDeviceOptions());

    /// <summary>
    /// Creates a new <see cref="GraphicsDevice"/> with the specified options.
    /// </summary>
    /// <param name="options">The graphics device options.</param>
    /// <returns>The graphics device associated with this adapter.</returns>
    public abstract GraphicsDevice CreateDevice(GraphicsDeviceOptions options);
}
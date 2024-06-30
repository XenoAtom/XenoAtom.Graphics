// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;

namespace XenoAtom.Graphics;

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
    /// Gets the vendor id of the adapter.
    /// </summary>
    public abstract uint VendorId { get; }

    /// <summary>
    /// Gets the device id of the adapter.
    /// </summary>
    public abstract uint DeviceId { get; }

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
    public abstract ref readonly GraphicsVersion ApiVersion { get; }

    /// <summary>
    /// Gets the version of the driver for this adapter.
    /// </summary>
    public abstract ref readonly GraphicsVersion DriverVersion { get; }

    /// <summary>
    /// Gets the UUID of the device.
    /// </summary>
    /// <remarks>
    /// Notice that this GUID is a Variant 1 UUID, big-endian (see https://en.wikipedia.org/wiki/Universally_unique_identifier#Encoding).
    /// </remarks>
    // ReSharper disable once InconsistentNaming
    public abstract ref readonly Guid DeviceUUID { get; }

    /// <summary>
    /// Gets the UUID of the driver.
    /// </summary>
    /// <remarks>
    /// Notice that this GUID is a Variant 1 UUID, big-endian (see https://en.wikipedia.org/wiki/Universally_unique_identifier#Encoding).
    /// </remarks>
    // ReSharper disable once InconsistentNaming
    public abstract ref readonly Guid DriverUUID { get; }

    /// <summary>
    /// Gets the LUID of the device.
    /// </summary>
    // ReSharper disable once InconsistentNaming
    public abstract ulong DeviceLUID { get; }
    
    /// <summary>
    /// Gets the kind of the adapter.
    /// </summary>
    public abstract GraphicsAdapterKind Kind { get; }

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
    public abstract GraphicsDevice CreateDevice(in GraphicsDeviceOptions options);
}
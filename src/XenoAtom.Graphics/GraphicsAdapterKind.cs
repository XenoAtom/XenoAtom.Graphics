// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace XenoAtom.Graphics;

/// <summary>
/// Supported <see cref="GraphicsAdapter"/> types
/// </summary>
public enum GraphicsAdapterKind
{
    /// <summary>
    /// The device is typically a separate processor connected to the host via an interlink.
    /// </summary>
    DiscreteGpu,

    /// <summary>
    /// The device is typically one embedded in or tightly coupled with the host.
    /// </summary>
    IntegratedGpu,

    /// <summary>
    /// The device is typically a virtual node in a virtualization environment.
    /// </summary>
    VirtualGpu,

    /// <summary>
    /// The device is typically running on the same processors as the host.
    /// </summary>
    Cpu,

    /// <summary>
    /// The device does not match any other available types.
    /// </summary>
    Other,
}
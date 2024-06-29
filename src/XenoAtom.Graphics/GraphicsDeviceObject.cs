// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;

namespace XenoAtom.Graphics
{
    /// <summary>
    /// Base class for all graphics objects.
    /// </summary>
    public abstract class GraphicsDeviceObject : GraphicsObject, IDeviceResource
    {
        private string? _name;

        internal GraphicsDeviceObject(GraphicsDevice device)
        {
            Device = device;
        }

        /// <summary>
        /// Gets the <see cref="GraphicsDevice"/> associated with this instance.
        /// </summary>
        public readonly GraphicsDevice Device;

        /// <summary>
        /// A string identifying this instance. Can be used to differentiate between objects in graphics debuggers and other
        /// tools.
        /// </summary>
        public string? Name
        {
            get => _name;
            set
            {
                if (IsDisposed)
                {
                    throw new ObjectDisposedException(GetType().FullName);
                }

                if (!string.Equals(_name, value, StringComparison.Ordinal))
                {
                    _name = value;
                    Device.SetResourceName(this, value);
                }
            }
        }
    }
}
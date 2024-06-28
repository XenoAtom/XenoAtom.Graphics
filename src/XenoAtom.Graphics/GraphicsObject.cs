// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;
using System.Threading;

namespace XenoAtom.Graphics
{
    /// <summary>
    /// Base class for all graphics objects.
    /// </summary>
    public abstract class GraphicsObject : IDeviceResource, IDisposable
    {
        private int _refCount;
        private string? _name;

        internal GraphicsObject(GraphicsDevice device)
        {
            Device = device;
            _refCount = 1;
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

        /// <summary>
        /// A bool indicating whether this instance has been disposed.
        /// </summary>
        public bool IsDisposed { get; private set; }


        /// <summary>
        /// Frees unmanaged device resources controlled by this instance.
        /// </summary>
        public void Dispose()
        {
            ReleaseReference();
        }

        internal int AddReference()
        {
            int ret = Interlocked.Increment(ref _refCount);
#if VALIDATE_USAGE
            if (ret == 0)
            {
                throw new GraphicsException("An attempt was made to reference a disposed resource.");
            }
#endif
            return ret;
        }

        internal int ReleaseReference()
        {
            int ret = Interlocked.Decrement(ref _refCount);
            if (ret == 0)
            {
                DestroyInternal();
            }

            return ret;
        }


        private void DestroyInternal()
        {
            if (!IsDisposed)
            {
                IsDisposed = true;
                DisposeCore();
            }
        }

        internal abstract void DisposeCore();
    }
}
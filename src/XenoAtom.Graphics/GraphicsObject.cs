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
    public abstract class GraphicsObject : IDisposable
    {
        private int _refCount;

        internal GraphicsObject()
        {
            _refCount = 1;
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

        /// <summary>
        /// Forces the destruction of this object.
        /// </summary>
        /// <remarks>
        /// Internally, this method will call <see cref="ReleaseReference"/> until the reference count reaches 0.
        /// </remarks>
        public void UnsafeDestroy()
        {
            int ret;
            do
            {
                ret = ReleaseReference();
            } while (ret > 0);
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
                Destroy();
            }
        }

        internal abstract void Destroy();
    }
}
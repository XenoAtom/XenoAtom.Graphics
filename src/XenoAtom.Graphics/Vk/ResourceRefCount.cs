using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace XenoAtom.Graphics.Vk
{
    /// <summary>
    /// Tracks the reference count of a resource.
    /// </summary>
    /// <remarks>
    /// We are using a struct wrapping a class to avoid the additional overhead of shared generics/covariance when using a reference types in HashSet&lt;T&gt; or T[].
    /// </remarks>
    internal readonly struct ResourceRefCount : IEquatable<ResourceRefCount>
    {
        private readonly InternalRefCount _internalInstance;

        public ResourceRefCount(Action disposeAction)
        {
            _internalInstance = new InternalRefCount(disposeAction);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Increment() => _internalInstance.Increment();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Decrement() => _internalInstance.Decrement();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(ResourceRefCount other) => _internalInstance == other._internalInstance;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(object? obj) => obj is ResourceRefCount other && Equals(other);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode() => RuntimeHelpers.GetHashCode(_internalInstance);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(ResourceRefCount left, ResourceRefCount right) => left.Equals(right);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(ResourceRefCount left, ResourceRefCount right) => !left.Equals(right);

        internal sealed class InternalRefCount
        {
            private readonly Action _disposeAction;
            private int _refCount;

            public InternalRefCount(Action disposeAction)
            {
                _disposeAction = disposeAction;
                _refCount = 1;
            }

            public int Increment()
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

            public int Decrement()
            {
                int ret = Interlocked.Decrement(ref _refCount);
                if (ret == 0)
                {
                    _disposeAction();
                }

                return ret;
            }
        }
    }
}

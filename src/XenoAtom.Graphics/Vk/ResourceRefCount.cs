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
        private readonly GraphicsObject _internalInstance;

        public ResourceRefCount(GraphicsObject instance)
        {
            _internalInstance = instance;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Increment() => _internalInstance.AddReference();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Decrement() => _internalInstance.ReleaseReference();

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

        public static implicit operator ResourceRefCount(GraphicsObject instance) => new(instance);
    }
}

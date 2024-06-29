using System;

namespace XenoAtom.Graphics
{
    /// <summary>
    /// A device resource used to store arbitrary graphics data in various formats.
    /// The size of a <see cref="DeviceBuffer"/> is fixed upon creation, and resizing is not possible.
    /// See <see cref="BufferDescription"/>.
    /// </summary>
    public abstract class DeviceBuffer : GraphicsDeviceObject, IBindableResource, IMappableResource
    {
        internal DeviceBuffer(GraphicsDevice device) : base(device)
        {
        }

        /// <summary>
        /// The total capacity, in bytes, of the buffer. This value is fixed upon creation.
        /// </summary>
        public abstract uint SizeInBytes { get; }

        /// <summary>
        /// A bitmask indicating how this instance is permitted to be used.
        /// </summary>
        public abstract BufferUsage Usage { get; }
    }
}

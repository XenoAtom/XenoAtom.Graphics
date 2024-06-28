using System;

namespace XenoAtom.Graphics
{
    // A GPU-CPU sync point
    /// <summary>
    /// A synchronization primitive which allows the GPU to communicate when submitted work items have finished executing.
    /// </summary>
    public abstract class Fence : GraphicsObject
    {
        internal Fence(GraphicsDevice device) : base(device)
        {
        }

        /// <summary>
        /// Gets a value indicating whether the Fence is currently signaled. A Fence is signaled after a CommandList finishes
        /// execution after it was submitted with a Fence instance.
        /// </summary>
        public abstract bool Signaled { get; }

        /// <summary>
        /// Sets this instance to the unsignaled state.
        /// </summary>
        public abstract void Reset();
    }
}

using System;
using XenoAtom.Interop;

namespace XenoAtom.Graphics
{
    /// <summary>
    /// A device resource encapsulating a single shader module.
    /// See <see cref="ShaderDescription"/>.
    /// </summary>
    public abstract class Shader : GraphicsObject
    {
        internal Shader(GraphicsDevice device, ShaderStages stage, ReadOnlyMemoryUtf8 entryPoint) : base(device)
        {
            Stage = stage;
            EntryPoint = entryPoint;
        }

        /// <summary>
        /// The shader stage this instance can be used in.
        /// </summary>
        public ShaderStages Stage { get; }

        /// <summary>
        /// The name of the entry point function.
        /// </summary>
        public ReadOnlyMemoryUtf8 EntryPoint { get; }
    }
}

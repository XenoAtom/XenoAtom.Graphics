using System;

namespace XenoAtom.Graphics
{
    /// <summary>
    /// A bindable device resource which controls how texture values are sampled within a shader.
    /// See <see cref="SamplerDescription"/>.
    /// </summary>
    public abstract class Sampler : GraphicsDeviceObject, IBindableResource
    {
        internal Sampler(GraphicsDevice device) : base(device)
        {
        }
    }
}

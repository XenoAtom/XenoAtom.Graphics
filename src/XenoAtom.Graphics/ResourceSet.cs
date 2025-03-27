using System;
using System.Diagnostics;

namespace XenoAtom.Graphics
{
    /// <summary>
    /// A device resource used to bind a particular set of <see cref="IBindableResource"/> objects to a <see cref="CommandBuffer"/>.
    /// See <see cref="ResourceSetDescription"/>.
    /// </summary>
    public abstract class ResourceSet : GraphicsDeviceObject
    {
        internal ResourceSet(GraphicsDevice device, in ResourceSetDescription description) : base(device)
        {
#if VALIDATE_USAGE
            Layout = description.Layout;
            Resources = description.BoundResources;
#endif
        }

#if VALIDATE_USAGE
        internal ResourceLayout Layout { get; }
        internal IBindableResource[] Resources { get; }
#endif
    }
}

using System;
using System.Diagnostics;

namespace XenoAtom.Graphics
{
    /// <summary>
    /// A device resource used to bind a particular set of <see cref="BindableResource"/> objects to a <see cref="CommandList"/>.
    /// See <see cref="ResourceSetDescription"/>.
    /// </summary>
    public abstract class ResourceSet : GraphicsObject
    {
        internal ResourceSet(GraphicsDevice device, ref ResourceSetDescription description) : base(device)
        {
#if VALIDATE_USAGE
            Layout = description.Layout;
            Resources = description.BoundResources;
#endif
        }

#if VALIDATE_USAGE
        internal ResourceLayout Layout { get; }
        internal BindableResource[] Resources { get; }
#endif
    }
}

using System;

namespace XenoAtom.Graphics
{
    /// <summary>
    /// A device resource which describes the layout and kind of <see cref="IBindableResource"/> objects available
    /// to a shader set.
    /// See <see cref="ResourceLayoutDescription"/>.
    /// </summary>
    public abstract class ResourceLayout : GraphicsDeviceObject
    {
#if VALIDATE_USAGE
        internal readonly ResourceLayoutDescription Description;
        internal readonly uint DynamicBufferCount;
#endif

        internal ResourceLayout(GraphicsDevice device, in ResourceLayoutDescription description) : base(device)
        {
#if VALIDATE_USAGE
            Description = description;
            foreach (ResourceLayoutElementDescription element in description.Elements)
            {
                if ((element.Options & ResourceLayoutElementOptions.DynamicBinding) != 0)
                {
                    DynamicBufferCount += 1;
                }
            }
#endif
        }
    }
}

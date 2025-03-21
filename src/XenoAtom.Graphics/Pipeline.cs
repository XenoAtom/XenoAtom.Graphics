using System;

namespace XenoAtom.Graphics
{
    /// <summary>
    /// A device resource encapsulating all state in a graphics pipeline. Used in 
    /// <see cref="CommandList.SetPipeline(Pipeline)"/> to prepare a <see cref="CommandList"/> for draw commands.
    /// See <see cref="GraphicsPipelineDescription"/>.
    /// </summary>
    public abstract class Pipeline : GraphicsDeviceObject
    {
        internal Pipeline(GraphicsDevice device, in GraphicsPipelineDescription graphicsDescription)
            : this(device, graphicsDescription.ResourceLayouts)
        {
#if VALIDATE_USAGE
            GraphicsOutputDescription = graphicsDescription.Outputs;
#endif
        }

        internal Pipeline(GraphicsDevice device, in ComputePipelineDescription computeDescription)
            : this(device, computeDescription.ResourceLayouts)
        { }

        internal Pipeline(GraphicsDevice device, ResourceLayout[] resourceLayouts) : base(device)
        {
#if VALIDATE_USAGE
            ResourceLayouts = Util.ShallowClone(resourceLayouts);
#endif
        }

        /// <summary>
        /// Gets the handle to the underlying native object.
        /// </summary>
        /// <remarks>
        /// For Vulkan, this is a <code>VkPipelineLayout</code>.
        /// </remarks>
        public abstract nint Handle { get; }

        /// <summary>
        /// Gets a value indicating whether this instance represents a compute Pipeline.
        /// If false, this instance is a graphics pipeline.
        /// </summary>
        public abstract bool IsComputePipeline { get; }

#if VALIDATE_USAGE
        internal OutputDescription GraphicsOutputDescription { get; }
        internal ResourceLayout[] ResourceLayouts { get; }
#endif
    }
}

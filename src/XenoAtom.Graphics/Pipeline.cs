using System;

namespace XenoAtom.Graphics
{
    /// <summary>
    /// A device resource encapsulating all state in a graphics pipeline. Used in 
    /// <see cref="CommandList.SetPipeline(Pipeline)"/> to prepare a <see cref="CommandList"/> for draw commands.
    /// See <see cref="GraphicsPipelineDescription"/>.
    /// </summary>
    public abstract class Pipeline : GraphicsObject
    {
        internal Pipeline(GraphicsDevice device, ref GraphicsPipelineDescription graphicsDescription)
            : this(device, graphicsDescription.ResourceLayouts)
        {
#if VALIDATE_USAGE
            GraphicsOutputDescription = graphicsDescription.Outputs;
#endif
        }

        internal Pipeline(GraphicsDevice device, ref ComputePipelineDescription computeDescription)
            : this(device, computeDescription.ResourceLayouts)
        { }

        internal Pipeline(GraphicsDevice device, ResourceLayout[] resourceLayouts) : base(device)
        {
#if VALIDATE_USAGE
            ResourceLayouts = Util.ShallowClone(resourceLayouts);
#endif
        }

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

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using XenoAtom.Interop;

namespace XenoAtom.Graphics
{
    /// <summary>
    /// A device resource which allows the recording of graphics commands, which can later be executed by a
    /// <see cref="GraphicsDevice"/>.
    /// Before graphics commands can be issued, the <see cref="Begin"/> method must be invoked.
    /// When the <see cref="CommandBuffer"/> is ready to be executed, <see cref="End"/> must be invoked, and then
    /// <see cref="GraphicsDevice.SubmitCommands(CommandBuffer)"/> should be used.
    /// NOTE: The use of <see cref="CommandBuffer"/> is not thread-safe. Access to the <see cref="CommandBuffer"/> must be
    /// externally synchronized.
    /// There are some limitations dictating proper usage and ordering of graphics commands. For example, a
    /// <see cref="Framebuffer"/>, <see cref="Pipeline"/>, vertex buffer, and index buffer must all be
    /// bound before a call to <see cref="DrawIndexed(uint, uint, uint, int, uint)"/> will succeed.
    /// These limitations are described in each function, where applicable.
    /// <see cref="CommandBuffer"/> instances cannot be executed multiple times per-recording. When executed by a
    /// <see cref="GraphicsDevice"/>, they must be reset and commands must be issued again.
    /// </summary>
    public abstract class CommandBuffer : GraphicsDeviceObject
    {
        private protected Framebuffer? _framebuffer;
        private protected Pipeline? _graphicsPipeline;
        private protected Pipeline? _computePipeline;
        private CommandBufferState _previousState;
        private CommandBufferState _state;

#if VALIDATE_USAGE
        private DeviceBuffer? _indexBuffer;
        private IndexFormat _indexFormat;
#endif

        internal CommandBuffer(CommandBufferPool pool) : base(pool.Device)
        {
            Pool = pool;
        }

        internal void ClearCachedState()
        {
            _framebuffer = null;
            _graphicsPipeline = null;
            _computePipeline = null;
#if VALIDATE_USAGE
            _indexBuffer = null;
#endif
        }


        /// <summary>
        /// Gets the current state of this <see cref="CommandBuffer"/>.
        /// </summary>
        public CommandBufferState State
        {
            get => _state;
            private protected set
            {
                if (_state == value) return;
                _previousState = _state;
                _state = value;
                Pool.NotifyBufferStateChanged(this);
            }
        }

        /// <summary>
        /// Gets the previous state of this <see cref="CommandBuffer"/>.
        /// </summary>
        internal CommandBufferState PreviousState => _previousState;

        /// <summary>
        /// Gets the <see cref="CommandBufferPool"/> that owns this <see cref="CommandBuffer"/>.
        /// </summary>
        public CommandBufferPool Pool { get; }

        /// <summary>
        /// Gets the underlying native handle for this <see cref="CommandBuffer"/>.
        /// </summary>
        /// <remarks>
        /// For Vulkan, this is a <code>VkCommandBuffer</code>.
        /// </remarks>
        public abstract nint Handle { get; }

        /// <summary>
        /// Resets this <see cref="CommandBuffer"/> to its initial state.
        /// </summary>
        /// <remarks>
        /// This can be called only if the <see cref="CommandBufferPool"/> was created with <see cref="CommandBufferPoolFlags.CanResetCommandBuffer"/> bit set.
        /// </remarks>
        public void Reset() => Reset(false);

        /// <summary>
        /// Reset this <see cref="CommandBuffer"/> to its initial state.
        /// </summary>
        /// <remarks>
        /// This can be called only if the <see cref="CommandBufferPool"/> was created with <see cref="CommandBufferPoolFlags.CanResetCommandBuffer"/> bit set.
        /// </remarks>
        public abstract void Reset(bool allowReleasingSystemResources);

        /// <summary>
        /// Puts this <see cref="CommandBuffer"/> into the initial state.
        /// This function must be called before other graphics commands can be issued.
        /// Begin must only be called if it has not been previously called, if <see cref="End"/> has been called,
        /// or if <see cref="GraphicsDevice.SubmitCommands(CommandBuffer)"/> has been called on this instance.
        /// </summary>
        public void Begin() => Begin(CommandBufferBeginFlags.None);

        /// <summary>
        /// Puts this <see cref="CommandBuffer"/> into the initial state.
        /// This function must be called before other graphics commands can be issued.
        /// Begin must only be called if it has not been previously called, if <see cref="End"/> has been called,
        /// or if <see cref="GraphicsDevice.SubmitCommands(CommandBuffer)"/> has been called on this instance.
        /// </summary>
        public abstract void Begin(CommandBufferBeginFlags flags);

        /// <summary>
        /// Completes this list of graphics commands, putting it into an executable state for a <see cref="GraphicsDevice"/>.
        /// This function must only be called after <see cref="Begin"/> has been called.
        /// It is an error to call this function in succession, unless <see cref="Begin"/> has been called in between invocations.
        /// </summary>
        public abstract void End();

        /// <summary>
        /// Sets the active <see cref="Pipeline"/> used for rendering.
        /// When drawing, the active <see cref="Pipeline"/> must be compatible with the bound <see cref="Framebuffer"/>,
        /// <see cref="ResourceSet"/>, and <see cref="DeviceBuffer"/> objects.
        /// When a new Pipeline is set, the previously-bound ResourceSets on this CommandList become invalidated and must be
        /// re-bound.
        /// </summary>
        /// <param name="pipeline">The new <see cref="Pipeline"/> object.</param>
        public void SetPipeline(Pipeline pipeline)
        {
            if (pipeline.IsComputePipeline)
            {
                _computePipeline = pipeline;
            }
            else
            {
                _graphicsPipeline = pipeline;
            }

            SetPipelineCore(pipeline);
        }

        /// <summary>
        /// Write a timestamp query.
        /// </summary>
        /// <param name="queryPool">The timestamp query</param>
        /// <param name="queryIndex"></param>
        /// <param name="stage">The stage to insert the query.</param>
        /// <returns>The query index.</returns>
        public abstract void WriteTimestampQuery(GraphicsQueryPool<TimeSpan> queryPool, GraphicsQueryIndex queryIndex, GraphicsPipelineStage stage);
        
        /// <summary>
        /// Pushes a constant value to the active <see cref="Pipeline"/> for the given shader stage.
        /// </summary>
        public abstract void PushConstant(ShaderStages shaderStage, ReadOnlySpan<byte> buffer, uint offset = 0);

        private protected abstract void SetPipelineCore(Pipeline pipeline);

        /// <summary>
        /// Sets the active <see cref="DeviceBuffer"/> for the given index.
        /// When drawing, the bound <see cref="DeviceBuffer"/> objects must be compatible with the bound <see cref="Pipeline"/>.
        /// The given buffer must be non-null. It is not necessary to un-bind vertex buffers for Pipelines which will not
        /// use them. All extra vertex buffers are simply ignored.
        /// </summary>
        /// <param name="index">The buffer slot.</param>
        /// <param name="buffer">The new <see cref="DeviceBuffer"/>.</param>
        public void SetVertexBuffer(uint index, DeviceBuffer buffer)
        {
            SetVertexBuffer(index, buffer, 0);
        }

        /// <summary>
        /// Sets the active <see cref="DeviceBuffer"/> for the given index.
        /// When drawing, the bound <see cref="DeviceBuffer"/> objects must be compatible with the bound <see cref="Pipeline"/>.
        /// The given buffer must be non-null. It is not necessary to un-bind vertex buffers for Pipelines which will not
        /// use them. All extra vertex buffers are simply ignored.
        /// </summary>
        /// <param name="index">The buffer slot.</param>
        /// <param name="buffer">The new <see cref="DeviceBuffer"/>.</param>
        /// <param name="offset">The offset from the start of the buffer, in bytes, from which data will start to be read.
        /// </param>
        public void SetVertexBuffer(uint index, DeviceBuffer buffer, uint offset)
        {
#if VALIDATE_USAGE
            if ((buffer.Usage & BufferUsage.VertexBuffer) == 0)
            {
                throw new GraphicsException(
                    $"Buffer cannot be bound as a vertex buffer because it was not created with BufferUsage.VertexBuffer.");
            }
#endif
            SetVertexBufferCore(index, buffer, offset);
        }

        private protected abstract void SetVertexBufferCore(uint index, DeviceBuffer buffer, uint offset);

        /// <summary>
        /// Sets the active <see cref="DeviceBuffer"/>.
        /// When drawing, an <see cref="DeviceBuffer"/> must be bound.
        /// </summary>
        /// <param name="buffer">The new <see cref="DeviceBuffer"/>.</param>
        /// <param name="format">The format of data in the <see cref="DeviceBuffer"/>.</param>
        public void SetIndexBuffer(DeviceBuffer buffer, IndexFormat format)
        {
            SetIndexBuffer(buffer, format, 0);
        }

        /// <summary>
        /// Sets the active <see cref="DeviceBuffer"/>.
        /// When drawing, an <see cref="DeviceBuffer"/> must be bound.
        /// </summary>
        /// <param name="buffer">The new <see cref="DeviceBuffer"/>.</param>
        /// <param name="format">The format of data in the <see cref="DeviceBuffer"/>.</param>
        /// <param name="offset">The offset from the start of the buffer, in bytes, from which data will start to be read.
        /// </param>
        public void SetIndexBuffer(DeviceBuffer buffer, IndexFormat format, uint offset)
        {
#if VALIDATE_USAGE
            if ((buffer.Usage & BufferUsage.IndexBuffer) == 0)
            {
                throw new GraphicsException(
                    $"Buffer cannot be bound as an index buffer because it was not created with BufferUsage.IndexBuffer.");
            }
            _indexBuffer = buffer;
            _indexFormat = format;
#endif
            SetIndexBufferCore(buffer, format, offset);
        }

        private protected abstract void SetIndexBufferCore(DeviceBuffer buffer, IndexFormat format, uint offset);

        /// <summary>
        /// Sets the active <see cref="ResourceSet"/> for the given index. This ResourceSet is only active for the graphics
        /// Pipeline.
        /// </summary>
        /// <param name="slot">The resource slot.</param>
        /// <param name="rs">The new <see cref="ResourceSet"/>.</param>
        public unsafe void SetGraphicsResourceSet(uint slot, ResourceSet rs)
            => SetGraphicsResourceSet(slot, rs, 0, ref Unsafe.AsRef<uint>(null));

        /// <summary>
        /// Sets the active <see cref="ResourceSet"/> for the given index. This ResourceSet is only active for the graphics
        /// Pipeline.
        /// </summary>
        /// <param name="slot">The resource slot.</param>
        /// <param name="rs">The new <see cref="ResourceSet"/>.</param>
        /// <param name="dynamicOffsets">An array containing the offsets to apply to the dynamic
        /// buffers contained in the <see cref="ResourceSet"/>. The number of elements in this array must be equal to the number
        /// of dynamic buffers (<see cref="ResourceLayoutElementOptions.DynamicBinding"/>) contained in the
        /// <see cref="ResourceSet"/>. These offsets are applied in the order that dynamic buffer
        /// elements appear in the <see cref="ResourceSet"/>. Each of these offsets must be a multiple of either
        /// <see cref="GraphicsDevice.UniformBufferMinOffsetAlignment"/> or
        /// <see cref="GraphicsDevice.StructuredBufferMinOffsetAlignment"/>, depending on the kind of resource.</param>
        public void SetGraphicsResourceSet(uint slot, ResourceSet rs, uint[] dynamicOffsets)
            => SetGraphicsResourceSet(slot, rs, (uint)dynamicOffsets.Length, ref dynamicOffsets[0]);

        /// <summary>
        /// Sets the active <see cref="ResourceSet"/> for the given index. This ResourceSet is only active for the graphics
        /// Pipeline.
        /// </summary>
        /// <param name="slot">The resource slot.</param>
        /// <param name="rs">The new <see cref="ResourceSet"/>.</param>
        /// <param name="dynamicOffsetsCount">The number of dynamic offsets being used. This must be equal to the number of
        /// dynamic buffers (<see cref="ResourceLayoutElementOptions.DynamicBinding"/>) contained in the
        /// <see cref="ResourceSet"/>.</param>
        /// <param name="dynamicOffsets">A reference to the first of a series of offsets which will be applied to the dynamic
        /// buffers contained in the <see cref="ResourceSet"/>. These offsets are applied in the order that dynamic buffer
        /// elements appear in the <see cref="ResourceSet"/>. Each of these offsets must be a multiple of either
        /// <see cref="GraphicsDevice.UniformBufferMinOffsetAlignment"/> or
        /// <see cref="GraphicsDevice.StructuredBufferMinOffsetAlignment"/>, depending on the kind of resource.</param>
        public void SetGraphicsResourceSet(uint slot, ResourceSet rs, uint dynamicOffsetsCount, ref uint dynamicOffsets)
        {
#if VALIDATE_USAGE
            if (_graphicsPipeline == null)
            {
                throw new GraphicsException($"A graphics Pipeline must be active before {nameof(SetGraphicsResourceSet)} can be called.");
            }

            int layoutsCount = _graphicsPipeline.ResourceLayouts.Length;
            if (layoutsCount <= slot)
            {
                throw new GraphicsException(
                    $"Failed to bind ResourceSet to slot {slot}. The active graphics Pipeline only contains {layoutsCount} ResourceLayouts.");
            }

            ResourceLayout layout = _graphicsPipeline.ResourceLayouts[slot];
            int pipelineLength = layout.Description.Elements.Length;
            ResourceLayoutDescription layoutDesc = rs.Layout.Description;
            int setLength = layoutDesc.Elements.Length;
            if (pipelineLength != setLength)
            {
                throw new GraphicsException($"Failed to bind ResourceSet to slot {slot}. The number of resources in the ResourceSet ({setLength}) does not match the number expected by the active Pipeline ({pipelineLength}).");
            }

            for (int i = 0; i < pipelineLength; i++)
            {
                ResourceKind pipelineKind = layout.Description.Elements[i].Kind;
                ResourceKind setKind = layoutDesc.Elements[i].Kind;
                if (pipelineKind != setKind)
                {
                    throw new GraphicsException(
                        $"Failed to bind ResourceSet to slot {slot}. Resource element {i} was of the incorrect type. The bound Pipeline expects {pipelineKind}, but the ResourceSet contained {setKind}.");
                }
            }

            if (rs.Layout.DynamicBufferCount != dynamicOffsetsCount)
            {
                throw new GraphicsException(
                    $"A dynamic offset must be provided for each resource that specifies " +
                    $"{nameof(ResourceLayoutElementOptions)}.{nameof(ResourceLayoutElementOptions.DynamicBinding)}. " +
                    $"{rs.Layout.DynamicBufferCount} offsets were expected, but only {dynamicOffsetsCount} were provided.");
            }

            uint dynamicOffsetIndex = 0;
            for (uint i = 0; i < layoutDesc.Elements.Length; i++)
            {
                if ((layoutDesc.Elements[i].Options & ResourceLayoutElementOptions.DynamicBinding) != 0)
                {
                    uint requiredAlignment = layoutDesc.Elements[i].Kind == ResourceKind.UniformBuffer
                        ? Device.UniformBufferMinOffsetAlignment
                        : Device.StructuredBufferMinOffsetAlignment;
                    uint desiredOffset = Unsafe.Add(ref dynamicOffsets, (int)dynamicOffsetIndex);
                    dynamicOffsetIndex += 1;
                    DeviceBufferRange range = Util.GetBufferRange(rs.Resources[i], desiredOffset);

                    if ((range.Offset % requiredAlignment) != 0)
                    {
                        throw new GraphicsException(
                            $"The effective offset of the buffer in slot {i} does not meet the alignment " +
                            $"requirements of this device. The offset must be a multiple of {requiredAlignment}, but it is " +
                            $"{range.Offset}");
                    }
                }
            }

#endif
            SetGraphicsResourceSetCore(slot, rs, dynamicOffsetsCount, ref dynamicOffsets);
        }

        // TODO: private protected
        /// <summary>
        /// </summary>
        /// <param name="slot"></param>
        /// <param name="rs"></param>
        /// <param name="dynamicOffsets"></param>
        /// <param name="dynamicOffsetsCount"></param>
        protected abstract void SetGraphicsResourceSetCore(uint slot, ResourceSet rs, uint dynamicOffsetsCount, ref uint dynamicOffsets);

        /// <summary>
        /// Sets the active <see cref="ResourceSet"/> for the given index. This ResourceSet is only active for the compute
        /// <see cref="Pipeline"/>.
        /// </summary>
        /// <param name="slot">The resource slot.</param>
        /// <param name="rs">The new <see cref="ResourceSet"/>.</param>
        public unsafe void SetComputeResourceSet(uint slot, ResourceSet rs)
            => SetComputeResourceSet(slot, rs, 0, ref Unsafe.AsRef<uint>(null));

        /// <summary>
        /// Sets the active <see cref="ResourceSet"/> for the given index. This ResourceSet is only active for the compute
        /// <see cref="Pipeline"/>.
        /// </summary>
        /// <param name="slot">The resource slot.</param>
        /// <param name="rs">The new <see cref="ResourceSet"/>.</param>
        /// <param name="dynamicOffsets">An array containing the offsets to apply to the dynamic buffers contained in the
        /// <see cref="ResourceSet"/>. The number of elements in this array must be equal to the number of dynamic buffers
        /// (<see cref="ResourceLayoutElementOptions.DynamicBinding"/>) contained in the <see cref="ResourceSet"/>. These offsets
        /// are applied in the order that dynamic buffer elements appear in the <see cref="ResourceSet"/>.</param>
        public void SetComputeResourceSet(uint slot, ResourceSet rs, uint[] dynamicOffsets)
            => SetComputeResourceSet(slot, rs, (uint)dynamicOffsets.Length, ref dynamicOffsets[0]);

        /// <summary>
        /// Sets the active <see cref="ResourceSet"/> for the given index. This ResourceSet is only active for the compute
        /// <see cref="Pipeline"/>.
        /// </summary>
        /// <param name="slot">The resource slot.</param>
        /// <param name="rs">The new <see cref="ResourceSet"/>.</param>
        /// <param name="dynamicOffsetsCount">The number of dynamic offsets being used. This must be equal to the number of
        /// dynamic buffers (<see cref="ResourceLayoutElementOptions.DynamicBinding"/>) contained in the
        /// <see cref="ResourceSet"/>.</param>
        /// <param name="dynamicOffsets">A reference to the first of a series of offsets which will be applied to the dynamic
        /// buffers contained in the <see cref="ResourceSet"/>. These offsets are applied in the order that dynamic buffer
        /// elements appear in the <see cref="ResourceSet"/>. Each of these offsets must be a multiple of either
        /// <see cref="GraphicsDevice.UniformBufferMinOffsetAlignment"/> or
        /// <see cref="GraphicsDevice.StructuredBufferMinOffsetAlignment"/>, depending on the kind of resource.</param>
        public unsafe void SetComputeResourceSet(uint slot, ResourceSet rs, uint dynamicOffsetsCount, ref uint dynamicOffsets)
        {
#if VALIDATE_USAGE
            if (_computePipeline == null)
            {
                throw new GraphicsException($"A compute Pipeline must be active before {nameof(SetComputeResourceSet)} can be called.");
            }

            int layoutsCount = _computePipeline.ResourceLayouts.Length;
            if (layoutsCount <= slot)
            {
                throw new GraphicsException(
                    $"Failed to bind ResourceSet to slot {slot}. The active compute Pipeline only contains {layoutsCount} ResourceLayouts.");
            }

            ResourceLayout layout = _computePipeline.ResourceLayouts[slot];
            int pipelineLength = layout.Description.Elements.Length;
            int setLength = rs.Layout.Description.Elements.Length;
            if (pipelineLength != setLength)
            {
                throw new GraphicsException($"Failed to bind ResourceSet to slot {slot}. The number of resources in the ResourceSet ({setLength}) does not match the number expected by the active Pipeline ({pipelineLength}).");
            }

            for (int i = 0; i < pipelineLength; i++)
            {
                ResourceKind pipelineKind = layout.Description.Elements[i].Kind;
                ResourceKind setKind = rs.Layout.Description.Elements[i].Kind;
                if (pipelineKind != setKind)
                {
                    throw new GraphicsException(
                        $"Failed to bind ResourceSet to slot {slot}. Resource element {i} was of the incorrect type. The bound Pipeline expects {pipelineKind}, but the ResourceSet contained {setKind}.");
                }
            }
#endif
            SetComputeResourceSetCore(slot, rs, dynamicOffsetsCount, ref dynamicOffsets);
        }

        // TODO: private protected
        /// <summary>
        /// </summary>
        /// <param name="slot"></param>
        /// <param name="set"></param>
        /// <param name="dynamicOffsetsCount"></param>
        /// <param name="dynamicOffsets"></param>
        protected abstract void SetComputeResourceSetCore(uint slot, ResourceSet set, uint dynamicOffsetsCount, ref uint dynamicOffsets);

        /// <summary>
        /// Sets the active <see cref="Framebuffer"/> which will be rendered to.
        /// When drawing, the active <see cref="Framebuffer"/> must be compatible with the active <see cref="Pipeline"/>.
        /// A compatible <see cref="Pipeline"/> has the same number of output attachments with matching formats.
        /// </summary>
        /// <param name="fb">The new <see cref="Framebuffer"/>.</param>
        public void SetFramebuffer(Framebuffer fb)
        {
            if (_framebuffer != fb)
            {
                _framebuffer = fb;
                SetFramebufferCore(fb);
                SetFullViewports();
                SetFullScissorRects();
            }
        }

        /// <summary>
        /// Performs API-specific handling of the <see cref="Framebuffer"/> resource.
        /// </summary>
        /// <param name="fb"></param>
        protected abstract void SetFramebufferCore(Framebuffer fb);

        /// <summary>
        /// Clears the color target at the given index of the active <see cref="Framebuffer"/>.
        /// The index given must be less than the number of color attachments in the active <see cref="Framebuffer"/>.
        /// </summary>
        /// <param name="index">The color target index.</param>
        /// <param name="clearColor">The value to clear the target to.</param>
        public void ClearColorTarget(uint index, RgbaFloat clearColor)
        {
#if VALIDATE_USAGE
            if (_framebuffer == null)
            {
                throw new GraphicsException($"Cannot use {nameof(ClearColorTarget)}. There is no Framebuffer bound.");
            }
            if (_framebuffer.ColorTargets.Length <= index)
            {
                throw new GraphicsException(
                    "ClearColorTarget index must be less than the current Framebuffer's color target count.");
            }
#endif
            ClearColorTargetCore(index, clearColor);
        }

        private protected abstract void ClearColorTargetCore(uint index, RgbaFloat clearColor);

        public void ClearTexture(Texture texture)
        {
            ClearTextureCore(texture);
        }

        private protected abstract void ClearTextureCore(Texture texture);

        /// <summary>
        /// Clears the depth-stencil target of the active <see cref="Framebuffer"/>.
        /// The active <see cref="Framebuffer"/> must have a depth attachment.
        /// With this overload, the stencil buffer is cleared to 0.
        /// </summary>
        /// <param name="depth">The value to clear the depth buffer to.</param>
        public void ClearDepthStencil(float depth)
        {
            ClearDepthStencil(depth, 0);
        }

        /// <summary>
        /// Clears the depth-stencil target of the active <see cref="Framebuffer"/>.
        /// The active <see cref="Framebuffer"/> must have a depth attachment.
        /// </summary>
        /// <param name="depth">The value to clear the depth buffer to.</param>
        /// <param name="stencil">The value to clear the stencil buffer to.</param>
        public void ClearDepthStencil(float depth, byte stencil)
        {
#if VALIDATE_USAGE
            if (_framebuffer == null)
            {
                throw new GraphicsException($"Cannot use {nameof(ClearDepthStencil)}. There is no Framebuffer bound.");
            }
            if (_framebuffer.DepthTarget == null)
            {
                throw new GraphicsException(
                    "The current Framebuffer has no depth target, so ClearDepthStencil cannot be used.");
            }
#endif

            ClearDepthStencilCore(depth, stencil);
        }

        private protected abstract void ClearDepthStencilCore(float depth, byte stencil);

        /// <summary>
        /// Sets all active viewports to cover the entire active <see cref="Framebuffer"/>.
        /// </summary>
        public void SetFullViewports()
        {
            if (_framebuffer == null)
            {
                throw new GraphicsException($"Cannot use {nameof(SetFullViewports)}. There is no Framebuffer bound.");
            }

            SetViewport(0, new Viewport(0, 0, _framebuffer.Width, _framebuffer.Height, 0, 1));

            for (uint index = 1; index < _framebuffer.ColorTargets.Length; index++)
            {
                SetViewport(index, new Viewport(0, 0, _framebuffer.Width, _framebuffer.Height, 0, 1));
            }
        }

        /// <summary>
        /// Sets the active viewport at the given index to cover the entire active <see cref="Framebuffer"/>.
        /// </summary>
        /// <param name="index">The color target index.</param>
        public void SetFullViewport(uint index)
        {
            if (_framebuffer == null)
            {
                throw new GraphicsException($"Cannot use {nameof(SetFullViewport)}. There is no Framebuffer bound.");
            }

            SetViewport(index, new Viewport(0, 0, _framebuffer.Width, _framebuffer.Height, 0, 1));
        }

        /// <summary>
        /// Sets the active <see cref="Viewport"/> at the given index.
        /// The index given must be less than the number of color attachments in the active <see cref="Framebuffer"/>.
        /// </summary>
        /// <param name="index">The color target index.</param>
        /// <param name="viewport">The new <see cref="Viewport"/>.</param>
        public void SetViewport(uint index, Viewport viewport) => SetViewport(index, ref viewport);

        /// <summary>
        /// Sets the active <see cref="Viewport"/> at the given index.
        /// The index given must be less than the number of color attachments in the active <see cref="Framebuffer"/>.
        /// </summary>
        /// <param name="index">The color target index.</param>
        /// <param name="viewport">The new <see cref="Viewport"/>.</param>
        public abstract void SetViewport(uint index, ref Viewport viewport);

        /// <summary>
        /// Sets all active scissor rectangles to cover the active <see cref="Framebuffer"/>.
        /// </summary>
        public void SetFullScissorRects()
        {
            if (_framebuffer == null)
            {
                throw new GraphicsException($"Cannot use {nameof(SetFullScissorRects)}. There is no Framebuffer bound.");
            }

            SetScissorRect(0, 0, 0, _framebuffer.Width, _framebuffer.Height);

            for (uint index = 1; index < _framebuffer.ColorTargets.Length; index++)
            {
                SetScissorRect(index, 0, 0, _framebuffer.Width, _framebuffer.Height);
            }
        }

        /// <summary>
        /// Sets the active scissor rectangle at the given index to cover the active <see cref="Framebuffer"/>.
        /// </summary>
        /// <param name="index">The color target index.</param>
        public void SetFullScissorRect(uint index)
        {
            if (_framebuffer == null)
            {
                throw new GraphicsException($"Cannot use {nameof(SetFullScissorRect)}. There is no Framebuffer bound.");
            }

            SetScissorRect(index, 0, 0, _framebuffer.Width, _framebuffer.Height);
        }

        /// <summary>
        /// Sets the active scissor rectangle at the given index.
        /// The index given must be less than the number of color attachments in the active <see cref="Framebuffer"/>.
        /// </summary>
        /// <param name="index">The color target index.</param>
        /// <param name="x">The X value of the scissor rectangle.</param>
        /// <param name="y">The Y value of the scissor rectangle.</param>
        /// <param name="width">The width of the scissor rectangle.</param>
        /// <param name="height">The height of the scissor rectangle.</param>
        public abstract void SetScissorRect(uint index, uint x, uint y, uint width, uint height);

        /// <summary>
        /// Draws primitives from the currently-bound state in this CommandList. An index Buffer is not used.
        /// </summary>
        /// <param name="vertexCount">The number of vertices.</param>
        public void Draw(uint vertexCount) => Draw(vertexCount, 1, 0, 0);

        /// <summary>
        /// Draws primitives from the currently-bound state in this CommandList. An index Buffer is not used.
        /// </summary>
        /// <param name="vertexCount">The number of vertices.</param>
        /// <param name="instanceCount">The number of instances.</param>
        /// <param name="vertexStart">The first vertex to use when drawing.</param>
        /// <param name="instanceStart">The starting instance value.</param>
        public void Draw(uint vertexCount, uint instanceCount, uint vertexStart, uint instanceStart)
        {
            PreDrawValidation();
            DrawCore(vertexCount, instanceCount, vertexStart, instanceStart);
        }

        private protected abstract void DrawCore(uint vertexCount, uint instanceCount, uint vertexStart, uint instanceStart);

        /// <summary>
        /// Draws indexed primitives from the currently-bound state in this <see cref="CommandBuffer"/>.
        /// </summary>
        /// <param name="indexCount">The number of indices.</param>
        public void DrawIndexed(uint indexCount) => DrawIndexed(indexCount, 1, 0, 0, 0);

        /// <summary>
        /// Draws indexed primitives from the currently-bound state in this <see cref="CommandBuffer"/>.
        /// </summary>
        /// <param name="indexCount">The number of indices.</param>
        /// <param name="instanceCount">The number of instances.</param>
        /// <param name="indexStart">The number of indices to skip in the active index buffer.</param>
        /// <param name="vertexOffset">The base vertex value, which is added to each index value read from the index buffer.</param>
        /// <param name="instanceStart">The starting instance value.</param>
        public void DrawIndexed(uint indexCount, uint instanceCount, uint indexStart, int vertexOffset, uint instanceStart)
        {
            ValidateIndexBuffer(indexCount);
            PreDrawValidation();

#if VALIDATE_USAGE
            if (!Device.Features.DrawBaseVertex && vertexOffset != 0)
            {
                throw new GraphicsException("Drawing with a non-zero base vertex is not supported on this device.");
            }
            if (!Device.Features.DrawBaseInstance && instanceStart != 0)
            {
                throw new GraphicsException("Drawing with a non-zero base instance is not supported on this device.");
            }
#endif
            DrawIndexedCore(indexCount, instanceCount, indexStart, vertexOffset, instanceStart);
        }

        private protected abstract void DrawIndexedCore(uint indexCount, uint instanceCount, uint indexStart, int vertexOffset, uint instanceStart);

        /// <summary>
        /// Issues indirect draw commands based on the information contained in the given indirect <see cref="DeviceBuffer"/>.
        /// The information stored in the indirect Buffer should conform to the structure of <see cref="IndirectDrawArguments"/>.
        /// </summary>
        /// <param name="indirectBuffer">The indirect Buffer to read from. Must have been created with the
        /// <see cref="BufferUsage.IndirectBuffer"/> flag.</param>
        /// <param name="offset">An offset, in bytes, from the start of the indirect buffer from which the draw commands will be
        /// read. This value must be a multiple of 4.</param>
        /// <param name="drawCount">The number of draw commands to read and issue from the indirect Buffer.</param>
        /// <param name="stride">The stride, in bytes, between consecutive draw commands in the indirect Buffer. This value must
        /// be a multiple of four, and must be larger than the size of <see cref="IndirectDrawArguments"/>.</param>
        public unsafe void DrawIndirect(DeviceBuffer indirectBuffer, uint offset, uint drawCount, uint stride)
        {
            ValidateDrawIndirectSupport();
            ValidateIndirectBuffer(indirectBuffer);
            ValidateIndirectOffset(offset);
            ValidateIndirectStride(stride, sizeof(IndirectDrawArguments));
            PreDrawValidation();

            DrawIndirectCore(indirectBuffer, offset, drawCount, stride);
        }

        // TODO: private protected
        /// <summary>
        /// </summary>
        /// <param name="indirectBuffer"></param>
        /// <param name="offset"></param>
        /// <param name="drawCount"></param>
        /// <param name="stride"></param>
        protected abstract void DrawIndirectCore(DeviceBuffer indirectBuffer, uint offset, uint drawCount, uint stride);

        /// <summary>
        /// Issues indirect, indexed draw commands based on the information contained in the given indirect <see cref="DeviceBuffer"/>.
        /// The information stored in the indirect Buffer should conform to the structure of
        /// <see cref="IndirectDrawIndexedArguments"/>.
        /// </summary>
        /// <param name="indirectBuffer">The indirect Buffer to read from. Must have been created with the
        /// <see cref="BufferUsage.IndirectBuffer"/> flag.</param>
        /// <param name="offset">An offset, in bytes, from the start of the indirect buffer from which the draw commands will be
        /// read. This value must be a multiple of 4.</param>
        /// <param name="drawCount">The number of draw commands to read and issue from the indirect Buffer.</param>
        /// <param name="stride">The stride, in bytes, between consecutive draw commands in the indirect Buffer. This value must
        /// be a multiple of four, and must be larger than the size of <see cref="IndirectDrawIndexedArguments"/>.</param>
        public unsafe void DrawIndexedIndirect(DeviceBuffer indirectBuffer, uint offset, uint drawCount, uint stride)
        {
            ValidateDrawIndirectSupport();
            ValidateIndirectBuffer(indirectBuffer);
            ValidateIndirectOffset(offset);
            ValidateIndirectStride(stride, sizeof(IndirectDrawIndexedArguments));
            PreDrawValidation();

            DrawIndexedIndirectCore(indirectBuffer, offset, drawCount, stride);
        }

        // TODO: private protected
        /// <summary>
        /// </summary>
        /// <param name="indirectBuffer"></param>
        /// <param name="offset"></param>
        /// <param name="drawCount"></param>
        /// <param name="stride"></param>
        protected abstract void DrawIndexedIndirectCore(DeviceBuffer indirectBuffer, uint offset, uint drawCount, uint stride);

        [Conditional("VALIDATE_USAGE")]
        private static void ValidateIndirectOffset(uint offset)
        {
            if ((offset % 4) != 0)
            {
                throw new GraphicsException($"{nameof(offset)} must be a multiple of 4.");
            }
        }

        [Conditional("VALIDATE_USAGE")]
        private void ValidateDrawIndirectSupport()
        {
            if (!Device.Features.DrawIndirect)
            {
                throw new GraphicsException($"Indirect drawing is not supported by this device.");
            }
        }

        [Conditional("VALIDATE_USAGE")]
        private static void ValidateIndirectBuffer(DeviceBuffer indirectBuffer)
        {
            if ((indirectBuffer.Usage & BufferUsage.IndirectBuffer) != BufferUsage.IndirectBuffer)
            {
                throw new GraphicsException(
                    $"{nameof(indirectBuffer)} parameter must have been created with BufferUsage.IndirectBuffer. Instead, it was {indirectBuffer.Usage}.");
            }
        }

        [Conditional("VALIDATE_USAGE")]
        private static void ValidateIndirectStride(uint stride, int argumentSize)
        {
            if (stride < argumentSize || ((stride % 4) != 0))
            {
                throw new GraphicsException(
                    $"{nameof(stride)} parameter must be a multiple of 4, and must be larger than the size of the corresponding argument structure.");
            }
        }

        /// <summary>
        /// Dispatches a compute operation from the currently-bound compute state of this Pipeline.
        /// </summary>
        /// <param name="groupCountX">The X dimension of the compute thread groups that are dispatched.</param>
        /// <param name="groupCountY">The Y dimension of the compute thread groups that are dispatched.</param>
        /// <param name="groupCountZ">The Z dimension of the compute thread groups that are dispatched.</param>
        public abstract void Dispatch(uint groupCountX, uint groupCountY, uint groupCountZ);

        /// <summary>
        /// Issues an indirect compute dispatch command based on the information contained in the given indirect
        /// <see cref="DeviceBuffer"/>. The information stored in the indirect Buffer should conform to the structure of
        /// <see cref="IndirectDispatchArguments"/>.
        /// </summary>
        /// <param name="indirectBuffer">The indirect Buffer to read from. Must have been created with the
        /// <see cref="BufferUsage.IndirectBuffer"/> flag.</param>
        /// <param name="offset">An offset, in bytes, from the start of the indirect buffer from which the draw commands will be
        /// read. This value must be a multiple of 4.</param>
        public void DispatchIndirect(DeviceBuffer indirectBuffer, uint offset)
        {
            ValidateIndirectBuffer(indirectBuffer);
            ValidateIndirectOffset(offset);
            DispatchIndirectCore(indirectBuffer, offset);
        }

        // TODO: private protected
        /// <summary>
        /// </summary>
        /// <param name="indirectBuffer"></param>
        /// <param name="offset"></param>
        protected abstract void DispatchIndirectCore(DeviceBuffer indirectBuffer, uint offset);

        /// <summary>
        /// Resolves a multisampled source <see cref="Texture"/> into a non-multisampled destination <see cref="Texture"/>.
        /// </summary>
        /// <param name="source">The source of the resolve operation. Must be a multisampled <see cref="Texture"/>
        /// (<see cref="Texture.SampleCount"/> > 1).</param>
        /// <param name="destination">The destination of the resolve operation. Must be a non-multisampled <see cref="Texture"/>
        /// (<see cref="Texture.SampleCount"/> == 1).</param>
        public void ResolveTexture(Texture source, Texture destination)
        {
#if VALIDATE_USAGE
            if (source.SampleCount == TextureSampleCount.Count1)
            {
                throw new GraphicsException(
                    $"The {nameof(source)} parameter of {nameof(ResolveTexture)} must be a multisample texture.");
            }
            if (destination.SampleCount != TextureSampleCount.Count1)
            {
                throw new GraphicsException(
                    $"The {nameof(destination)} parameter of {nameof(ResolveTexture)} must be a non-multisample texture. Instead, it is a texture with {FormatHelpers.GetSampleCountUInt32(source.SampleCount)} samples.");
            }
#endif

            ResolveTextureCore(source, destination);
        }

        /// <summary>
        /// Resolves a multisampled source <see cref="Texture"/> into a non-multisampled destination <see cref="Texture"/>.
        /// </summary>
        /// <param name="source">The source of the resolve operation. Must be a multisampled <see cref="Texture"/>
        /// (<see cref="Texture.SampleCount"/> > 1).</param>
        /// <param name="destination">The destination of the resolve operation. Must be a non-multisampled <see cref="Texture"/>
        /// (<see cref="Texture.SampleCount"/> == 1).</param>
        protected abstract void ResolveTextureCore(Texture source, Texture destination);

        /// <summary>
        /// Updates a <see cref="DeviceBuffer"/> region with new data.
        /// This function must be used with a blittable value type <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">The type of data to upload.</typeparam>
        /// <param name="buffer">The resource to update.</param>
        /// <param name="bufferOffsetInBytes">An offset, in bytes, from the beginning of the <see cref="DeviceBuffer"/>'s storage, at
        /// which new data will be uploaded.</param>
        /// <param name="source">A reference to the single value to upload.</param>
        public unsafe void UpdateBuffer<T>(
            DeviceBuffer buffer,
            uint bufferOffsetInBytes,
            in T source) where T : unmanaged
        {
            ref byte sourceByteRef = ref Unsafe.AsRef<byte>(Unsafe.AsPointer(ref Unsafe.AsRef(in source)));
            fixed (byte* ptr = &sourceByteRef)
            {
                UpdateBuffer(buffer, bufferOffsetInBytes, (IntPtr)ptr, Util.USizeOf<T>());
            }
        }

        /// <summary>
        /// Updates a <see cref="DeviceBuffer"/> region with new data.
        /// This function must be used with a blittable value type <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">The type of data to upload.</typeparam>
        /// <param name="buffer">The resource to update.</param>
        /// <param name="bufferOffsetInBytes">An offset, in bytes, from the beginning of the <see cref="DeviceBuffer"/>'s storage, at
        /// which new data will be uploaded.</param>
        /// <param name="source">A reference to the first of a series of values to upload.</param>
        /// <param name="sizeInBytes">The total size of the uploaded data, in bytes.</param>
        public unsafe void UpdateBuffer<T>(
            DeviceBuffer buffer,
            uint bufferOffsetInBytes,
            in T source,
            uint sizeInBytes) where T : unmanaged
        {
            ref byte sourceByteRef = ref Unsafe.AsRef<byte>(Unsafe.AsPointer(ref Unsafe.AsRef(in source)));
            fixed (byte* ptr = &sourceByteRef)
            {
                UpdateBuffer(buffer, bufferOffsetInBytes, (IntPtr)ptr, sizeInBytes);
            }
        }

        /// <summary>
        /// Updates a <see cref="DeviceBuffer"/> region with new data.
        /// This function must be used with a blittable value type <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">The type of data to upload.</typeparam>
        /// <param name="buffer">The resource to update.</param>
        /// <param name="bufferOffsetInBytes">An offset, in bytes, from the beginning of the <see cref="DeviceBuffer"/>'s storage, at
        /// which new data will be uploaded.</param>
        /// <param name="source">An array containing the data to upload.</param>
        public void UpdateBuffer<T>(
            DeviceBuffer buffer,
            uint bufferOffsetInBytes,
            T[] source) where T : unmanaged
        {
            UpdateBuffer(buffer, bufferOffsetInBytes, (ReadOnlySpan<T>)source);
        }

        /// <summary>
        /// Updates a <see cref="DeviceBuffer"/> region with new data.
        /// This function must be used with a blittable value type <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">The type of data to upload.</typeparam>
        /// <param name="buffer">The resource to update.</param>
        /// <param name="bufferOffsetInBytes">An offset, in bytes, from the beginning of the <see cref="DeviceBuffer"/>'s storage, at
        /// which new data will be uploaded.</param>
        /// <param name="source">An readonly span containing the data to upload.</param>
        public unsafe void UpdateBuffer<T>(
            DeviceBuffer buffer,
            uint bufferOffsetInBytes,
            ReadOnlySpan<T> source) where T : unmanaged
        {
            fixed (void* pin = &MemoryMarshal.GetReference(source))
            {
                UpdateBuffer(buffer, bufferOffsetInBytes, (IntPtr)pin, (uint)(sizeof(T) * source.Length));
            }
        }

        /// <summary>
        /// Updates a <see cref="DeviceBuffer"/> region with new data.
        /// This function must be used with a blittable value type <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">The type of data to upload.</typeparam>
        /// <param name="buffer">The resource to update.</param>
        /// <param name="bufferOffsetInBytes">An offset, in bytes, from the beginning of the <see cref="DeviceBuffer"/>'s storage, at
        /// which new data will be uploaded.</param>
        /// <param name="source">An span containing the data to upload.</param>
        public void UpdateBuffer<T>(
            DeviceBuffer buffer,
            uint bufferOffsetInBytes,
            Span<T> source) where T : unmanaged
        {
            UpdateBuffer(buffer, bufferOffsetInBytes, (ReadOnlySpan<T>)source);
        }

        /// <summary>
        /// Updates a <see cref="DeviceBuffer"/> region with new data.
        /// </summary>
        /// <param name="buffer">The resource to update.</param>
        /// <param name="bufferOffsetInBytes">An offset, in bytes, from the beginning of the <see cref="DeviceBuffer"/>'s storage, at
        /// which new data will be uploaded.</param>
        /// <param name="source">A pointer to the start of the data to upload.</param>
        /// <param name="sizeInBytes">The total size of the uploaded data, in bytes.</param>
        public void UpdateBuffer(
            DeviceBuffer buffer,
            uint bufferOffsetInBytes,
            IntPtr source,
            uint sizeInBytes)
        {
            if (bufferOffsetInBytes + sizeInBytes > buffer.SizeInBytes)
            {
                throw new GraphicsException(
                    $"The DeviceBuffer's capacity ({buffer.SizeInBytes}) is not large enough to store the amount of " +
                    $"data specified ({sizeInBytes}) at the given offset ({bufferOffsetInBytes}).");
            }
            if (sizeInBytes == 0)
            {
                return;
            }

            UpdateBufferCore(buffer, bufferOffsetInBytes, source, sizeInBytes);
        }

        private protected abstract void UpdateBufferCore(
            DeviceBuffer buffer,
            uint bufferOffsetInBytes,
            IntPtr source,
            uint sizeInBytes);

        /// <summary>
        /// Copies a region from the source <see cref="DeviceBuffer"/> to another region in the destination <see cref="DeviceBuffer"/>.
        /// </summary>
        /// <param name="source">The source <see cref="DeviceBuffer"/> from which data will be copied.</param>
        /// <param name="sourceOffset">An offset into <paramref name="source"/> at which the copy region begins.</param>
        /// <param name="destination">The destination <see cref="DeviceBuffer"/> into which data will be copied.</param>
        /// <param name="destinationOffset">An offset into <paramref name="destination"/> at which the data will be copied.
        /// </param>
        /// <param name="sizeInBytes">The number of bytes to copy.</param>
        public void CopyBuffer(DeviceBuffer source, uint sourceOffset, DeviceBuffer destination, uint destinationOffset, uint sizeInBytes)
        {
#if VALIDATE_USAGE
#endif
            if (sizeInBytes == 0)
            {
                return;
            }

            CopyBufferCore(source, sourceOffset, destination, destinationOffset, sizeInBytes);
        }

        /// <summary>
        /// </summary>
        /// <param name="source"></param>
        /// <param name="sourceOffset"></param>
        /// <param name="destination"></param>
        /// <param name="destinationOffset"></param>
        /// <param name="sizeInBytes"></param>
        protected abstract void CopyBufferCore(DeviceBuffer source, uint sourceOffset, DeviceBuffer destination, uint destinationOffset, uint sizeInBytes);

        /// <summary>
        /// Copies all subresources from one <see cref="Texture"/> to another.
        /// </summary>
        /// <param name="source">The source of Texture data.</param>
        /// <param name="destination">The destination of Texture data.</param>
        public void CopyTexture(Texture source, Texture destination)
        {
            uint effectiveSrcArrayLayers = (source.Usage & TextureUsage.Cubemap) != 0
                ? source.ArrayLayers * 6
                : source.ArrayLayers;
#if VALIDATE_USAGE
            uint effectiveDstArrayLayers = (destination.Usage & TextureUsage.Cubemap) != 0
                ? destination.ArrayLayers * 6
                : destination.ArrayLayers;
            if (effectiveSrcArrayLayers != effectiveDstArrayLayers || source.MipLevels != destination.MipLevels
                || source.SampleCount != destination.SampleCount || source.Width != destination.Width
                || source.Height != destination.Height || source.Depth != destination.Depth
                || source.Format != destination.Format)
            {
                throw new GraphicsException("Source and destination Textures are not compatible to be copied.");
            }
#endif

            for (uint level = 0; level < source.MipLevels; level++)
            {
                Util.GetMipDimensions(source, level, out uint mipWidth, out uint mipHeight, out uint mipDepth);
                CopyTexture(
                    source, 0, 0, 0, level, 0,
                    destination, 0, 0, 0, level, 0,
                    mipWidth, mipHeight, mipDepth,
                    effectiveSrcArrayLayers);
            }
        }

        /// <summary>
        /// Copies one subresource from one <see cref="Texture"/> to another.
        /// </summary>
        /// <param name="source">The source of Texture data.</param>
        /// <param name="destination">The destination of Texture data.</param>
        /// <param name="mipLevel">The mip level to copy.</param>
        /// <param name="arrayLayer">The array layer to copy.</param>
        public void CopyTexture(Texture source, Texture destination, uint mipLevel, uint arrayLayer)
        {
#if VALIDATE_USAGE
            uint effectiveSrcArrayLayers = (source.Usage & TextureUsage.Cubemap) != 0
                ? source.ArrayLayers * 6
                : source.ArrayLayers;
            uint effectiveDstArrayLayers = (destination.Usage & TextureUsage.Cubemap) != 0
                ? destination.ArrayLayers * 6
                : destination.ArrayLayers;
            if (source.SampleCount != destination.SampleCount || source.Width != destination.Width
                || source.Height != destination.Height || source.Depth != destination.Depth
                || source.Format != destination.Format)
            {
                throw new GraphicsException("Source and destination Textures are not compatible to be copied.");
            }
            if (mipLevel >= source.MipLevels || mipLevel >= destination.MipLevels || arrayLayer >= effectiveSrcArrayLayers || arrayLayer >= effectiveDstArrayLayers)
            {
                throw new GraphicsException(
                    $"{nameof(mipLevel)} and {nameof(arrayLayer)} must be less than the given Textures' mip level count and array layer count.");
            }
#endif

            Util.GetMipDimensions(source, mipLevel, out uint width, out uint height, out uint depth);
            CopyTexture(
                source, 0, 0, 0, mipLevel, arrayLayer,
                destination, 0, 0, 0, mipLevel, arrayLayer,
                width, height, depth,
                1);
        }

        /// <summary>
        /// Copies a region from one <see cref="Texture"/> into another.
        /// </summary>
        /// <param name="source">The source <see cref="Texture"/> from which data is copied.</param>
        /// <param name="srcX">The X coordinate of the source copy region.</param>
        /// <param name="srcY">The Y coordinate of the source copy region.</param>
        /// <param name="srcZ">The Z coordinate of the source copy region.</param>
        /// <param name="srcMipLevel">The mip level to copy from the source Texture.</param>
        /// <param name="srcBaseArrayLayer">The starting array layer to copy from the source Texture.</param>
        /// <param name="destination">The destination <see cref="Texture"/> into which data is copied.</param>
        /// <param name="dstX">The X coordinate of the destination copy region.</param>
        /// <param name="dstY">The Y coordinate of the destination copy region.</param>
        /// <param name="dstZ">The Z coordinate of the destination copy region.</param>
        /// <param name="dstMipLevel">The mip level to copy the data into.</param>
        /// <param name="dstBaseArrayLayer">The starting array layer to copy data into.</param>
        /// <param name="width">The width in texels of the copy region.</param>
        /// <param name="height">The height in texels of the copy region.</param>
        /// <param name="depth">The depth in texels of the copy region.</param>
        /// <param name="layerCount">The number of array layers to copy.</param>
        public void CopyTexture(
            Texture source,
            uint srcX, uint srcY, uint srcZ,
            uint srcMipLevel,
            uint srcBaseArrayLayer,
            Texture destination,
            uint dstX, uint dstY, uint dstZ,
            uint dstMipLevel,
            uint dstBaseArrayLayer,
            uint width, uint height, uint depth,
            uint layerCount)
        {
#if VALIDATE_USAGE
            if (width == 0 || height == 0 || depth == 0)
            {
                throw new GraphicsException($"The given copy region is empty.");
            }
            if (layerCount == 0)
            {
                throw new GraphicsException($"{nameof(layerCount)} must be greater than 0.");
            }
            Util.GetMipDimensions(source, srcMipLevel, out uint srcWidth, out uint srcHeight, out uint srcDepth);
            uint srcBlockSize = FormatHelpers.IsCompressedFormat(source.Format) ? 4u : 1u;
            uint roundedSrcWidth = (srcWidth + srcBlockSize - 1) / srcBlockSize * srcBlockSize;
            uint roundedSrcHeight = (srcHeight + srcBlockSize - 1) / srcBlockSize * srcBlockSize;
            if (srcX + width > roundedSrcWidth || srcY + height > roundedSrcHeight || srcZ + depth > srcDepth)
            {
                throw new GraphicsException($"The given copy region is not valid for the source Texture.");
            }
            Util.GetMipDimensions(destination, dstMipLevel, out uint dstWidth, out uint dstHeight, out uint dstDepth);
            uint dstBlockSize = FormatHelpers.IsCompressedFormat(destination.Format) ? 4u : 1u;
            uint roundedDstWidth = (dstWidth + dstBlockSize - 1) / dstBlockSize * dstBlockSize;
            uint roundedDstHeight = (dstHeight + dstBlockSize - 1) / dstBlockSize * dstBlockSize;
            if (dstX + width > roundedDstWidth || dstY + height > roundedDstHeight || dstZ + depth > dstDepth)
            {
                throw new GraphicsException($"The given copy region is not valid for the destination Texture.");
            }
            if (srcMipLevel >= source.MipLevels)
            {
                throw new GraphicsException($"{nameof(srcMipLevel)} must be less than the number of mip levels in the source Texture.");
            }
            uint effectiveSrcArrayLayers = (source.Usage & TextureUsage.Cubemap) != 0
                ? source.ArrayLayers * 6
                : source.ArrayLayers;
            if (srcBaseArrayLayer + layerCount > effectiveSrcArrayLayers)
            {
                throw new GraphicsException($"An invalid mip range was given for the source Texture.");
            }
            if (dstMipLevel >= destination.MipLevels)
            {
                throw new GraphicsException($"{nameof(dstMipLevel)} must be less than the number of mip levels in the destination Texture.");
            }
            uint effectiveDstArrayLayers = (destination.Usage & TextureUsage.Cubemap) != 0
                ? destination.ArrayLayers * 6
                : destination.ArrayLayers;
            if (dstBaseArrayLayer + layerCount > effectiveDstArrayLayers)
            {
                throw new GraphicsException($"An invalid mip range was given for the destination Texture.");
            }
#endif
            CopyTextureCore(
                source,
                srcX, srcY, srcZ,
                srcMipLevel,
                srcBaseArrayLayer,
                destination,
                dstX, dstY, dstZ,
                dstMipLevel,
                dstBaseArrayLayer,
                width, height, depth,
                layerCount);
        }

        /// <summary>
        /// </summary>
        /// <param name="source"></param>
        /// <param name="srcX"></param>
        /// <param name="srcY"></param>
        /// <param name="srcZ"></param>
        /// <param name="srcMipLevel"></param>
        /// <param name="srcBaseArrayLayer"></param>
        /// <param name="destination"></param>
        /// <param name="dstX"></param>
        /// <param name="dstY"></param>
        /// <param name="dstZ"></param>
        /// <param name="dstMipLevel"></param>
        /// <param name="dstBaseArrayLayer"></param>
        /// <param name="width"></param>
        /// <param name="height"></param>
        /// <param name="depth"></param>
        /// <param name="layerCount"></param>
        protected abstract void CopyTextureCore(
            Texture source,
            uint srcX, uint srcY, uint srcZ,
            uint srcMipLevel,
            uint srcBaseArrayLayer,
            Texture destination,
            uint dstX, uint dstY, uint dstZ,
            uint dstMipLevel,
            uint dstBaseArrayLayer,
            uint width, uint height, uint depth,
            uint layerCount);

        /// <summary>
        /// Generates mipmaps for the given <see cref="Texture"/>. The largest mipmap is used to generate all of the lower mipmap
        /// levels contained in the Texture. The previous contents of all lower mipmap levels are overwritten by this operation.
        /// The target Texture must have been created with <see cref="TextureUsage"/>.<see cref="TextureUsage.GenerateMipmaps"/>.
        /// </summary>
        /// <param name="texture">The <see cref="Texture"/> to generate mipmaps for. This Texture must have been created with
        /// <see cref="TextureUsage"/>.<see cref="TextureUsage.GenerateMipmaps"/>.</param>
        public void GenerateMipmaps(Texture texture)
        {
            if ((texture.Usage & TextureUsage.GenerateMipmaps) == 0)
            {
                throw new GraphicsException(
                    $"{nameof(GenerateMipmaps)} requires a target Texture with {nameof(TextureUsage)}.{nameof(TextureUsage.GenerateMipmaps)}");
            }

            if (texture.MipLevels > 1)
            {
                GenerateMipmapsCore(texture);
            }
        }

        private protected abstract void GenerateMipmapsCore(Texture texture);

        /// <summary>
        /// Pushes a debug group at the current position in the <see cref="CommandBuffer"/>. This allows subsequent commands to be
        /// categorized and filtered when viewed in external debugging tools. This method can be called multiple times in order
        /// to create nested debug groupings. Each call to PushDebugGroup must be followed by a matching call to
        /// <see cref="PopDebugGroup"/>.
        /// </summary>
        /// <param name="name">The name of the group. This is an opaque identifier used for display by graphics debuggers.</param>
        /// <param name="color">An optional color used for the marker.</param>
        public void PushDebugGroup(string name, in RgbaFloat color = default)
        {
            var utf8Count = Encoding.UTF8.GetByteCount(name);
            Span<byte> utf8Buffer = stackalloc byte[utf8Count + 1];
            Encoding.UTF8.GetBytes(name, utf8Buffer);
            utf8Buffer[utf8Count] = 0;
            PushDebugGroup((ReadOnlySpan<byte>)utf8Buffer, color);
        }

        /// <summary>
        /// Pushes a debug group at the current position in the <see cref="CommandBuffer"/>. This allows subsequent commands to be
        /// categorized and filtered when viewed in external debugging tools. This method can be called multiple times in order
        /// to create nested debug groupings. Each call to PushDebugGroup must be followed by a matching call to
        /// <see cref="PopDebugGroup"/>.
        /// </summary>
        /// <param name="name">The name of the group. This is an opaque identifier used for display by graphics debuggers.</param>
        /// <param name="color">An optional color used for the marker.</param>
        public abstract void PushDebugGroup(ReadOnlySpanUtf8 name, in RgbaFloat color = default);

        /// <summary>
        /// Pops the current debug group. This method must only be called after <see cref="PushDebugGroup(string,in XenoAtom.Graphics.RgbaFloat)"/> has been
        /// called on this instance.
        /// </summary>
        public abstract void PopDebugGroup();

        /// <summary>
        /// Inserts a debug marker into the CommandList at the current position. This is used by graphics debuggers to identify
        /// points of interest in a command stream.
        /// </summary>
        /// <param name="color">An optional color used for the marker.</param>
        /// <param name="name">The name of the marker. This is an opaque identifier used for display by graphics debuggers.</param>
        public void InsertDebugMarker(string name, in RgbaFloat color = default)
        {
            var utf8Count = Encoding.UTF8.GetByteCount(name);
            Span<byte> utf8Buffer = stackalloc byte[utf8Count + 1];
            Encoding.UTF8.GetBytes(name, utf8Buffer);
            utf8Buffer[utf8Count] = 0;
            InsertDebugMarker((ReadOnlySpan<byte>)utf8Buffer, color);
        }

        /// <summary>
        /// Inserts a debug marker into the CommandList at the current position. This is used by graphics debuggers to identify
        /// points of interest in a command stream.
        /// </summary>
        /// <param name="name">The name of the marker. This is an opaque identifier used for display by graphics debuggers.</param>
        /// <param name="color">An optional color used for the marker.</param>
        public abstract void InsertDebugMarker(ReadOnlySpanUtf8 name, in RgbaFloat color = default);

        [Conditional("VALIDATE_USAGE")]
        private void ValidateIndexBuffer(uint indexCount)
        {
#if VALIDATE_USAGE
            if (_indexBuffer == null)
            {
                throw new GraphicsException($"An index buffer must be bound before {nameof(CommandBuffer)}.{nameof(DrawIndexed)} can be called.");
            }

            uint indexFormatSize = _indexFormat == IndexFormat.UInt16 ? 2u : 4u;
            uint bytesNeeded = indexCount * indexFormatSize;
            if (_indexBuffer.SizeInBytes < bytesNeeded)
            {
                throw new GraphicsException(
                    $"The active index buffer does not contain enough data to satisfy the given draw command. {bytesNeeded} bytes are needed, but the buffer only contains {_indexBuffer.SizeInBytes}.");
            }
#endif
        }

        [Conditional("VALIDATE_USAGE")]
        private void PreDrawValidation()
        {
#if VALIDATE_USAGE

            if (_graphicsPipeline == null)
            {
                throw new GraphicsException($"A graphics {nameof(Pipeline)} must be set in order to issue draw commands.");
            }
            if (_framebuffer == null)
            {
                throw new GraphicsException($"A {nameof(Framebuffer)} must be set in order to issue draw commands.");
            }
            if (!_graphicsPipeline.GraphicsOutputDescription.Equals(_framebuffer.OutputDescription))
            {
                throw new GraphicsException($"The {nameof(OutputDescription)} of the current graphics {nameof(Pipeline)} is not compatible with the current {nameof(Framebuffer)}.");
            }
#endif
        }
    }
}

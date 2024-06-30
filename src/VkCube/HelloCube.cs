// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using NWindows;
using XenoAtom.Graphics;

namespace VkCube;

/// <summary>
/// Demonstrates how to create a simple cube using XenoAtom.Graphics.
/// </summary>
public class HelloCube
{
    private readonly Window _window;
    private readonly GraphicsManager _graphicsManager;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly Swapchain _swapChain;
    private readonly DeviceBuffer _vertexBuffer;
    private readonly Shader _vertexShader;
    private readonly Shader _fragmentShader;
    private readonly Pipeline _pipeline;
    private readonly Matrix4x4 _view;
    private Matrix4x4 _projection;
    private readonly Stopwatch _time;
    private readonly CommandList _cl;
    private readonly DeviceBuffer _matrixBuffer;
    private readonly ResourceLayout _resourceLayout;
    private readonly ResourceSet _resourceSet;
    private System.Drawing.SizeF _windowClientSize;

    public HelloCube(Window window)
    {
        _window = window;
        _graphicsManager = GraphicsManager.Create(new GraphicsManagerOptions(true));
        var adapter = _graphicsManager.GetBestAdapter();
        _graphicsDevice = adapter.CreateDevice(new GraphicsDeviceOptions());

        _windowClientSize = _window.Dpi.LogicalToPixel(_window.ClientSize);
        var swapchainDesc = new SwapchainDescription(SwapchainSource.CreateWin32(_window.Handle, 0), (uint)_windowClientSize.Width, (uint)_windowClientSize.Height, PixelFormat.D32_Float_S8_UInt, true);
        _swapChain = _graphicsDevice.CreateSwapchain(swapchainDesc);

        // Create command list
        _cl = _graphicsDevice.CreateCommandList();

        // Define vertex and index buffers
        _vertexBuffer = _graphicsDevice.CreateBuffer(new BufferDescription((uint)(Vertices.Length * Unsafe.SizeOf<VertexPositionColor>()), BufferUsage.VertexBuffer));

        // Fill buffers with cube data
        _graphicsDevice.UpdateBuffer(_vertexBuffer, 0, Vertices);
        
        // Create shaders
        _vertexShader = _graphicsDevice.CreateShader(new ShaderDescription(ShaderStages.Vertex, CompiledShaders.ColoredQuadRenderer_vert));
        _fragmentShader = _graphicsDevice.CreateShader(new ShaderDescription(ShaderStages.Fragment, CompiledShaders.ColoredQuadRenderer_frag));

        var shaders = new Shader[] { _vertexShader, _fragmentShader };


        var shaderSet = new ShaderSetDescription(
            vertexLayouts: [
                new(
                    (uint)Unsafe.SizeOf<VertexPositionColor>(),
                    0,
                    new ("Position", VertexElementSemantic.Position, VertexElementFormat.Float3),
                    new("Color", VertexElementSemantic.Color, VertexElementFormat.Float4)
                )
            ],
            shaders: shaders);

        _resourceLayout = _graphicsDevice.CreateResourceLayout(new (
            new ResourceLayoutElementDescription("MVP", ResourceKind.UniformBuffer, ShaderStages.Vertex)));

        // Create pipeline
        var pipelineDescription = new GraphicsPipelineDescription(
            BlendStateDescription.SingleOverrideBlend,
            DepthStencilStateDescription.DepthOnlyLessEqual,
            RasterizerStateDescription.Default,
            PrimitiveTopology.TriangleList,
            shaderSet,
            _resourceLayout,
            _swapChain.Framebuffer.OutputDescription);


        _matrixBuffer = _graphicsDevice.CreateBuffer(new(64, BufferUsage.UniformBuffer));

        _resourceSet = _graphicsDevice.CreateResourceSet(new(_resourceLayout, _matrixBuffer));

        _pipeline = _graphicsDevice.CreateGraphicsPipeline(pipelineDescription);
        
        // Create the view-projection matrix
        _view = Matrix4x4.CreateLookAt(new Vector3(0, 0, 4), Vector3.Zero, Vector3.UnitY);
        _projection = Matrix4x4.CreatePerspectiveFieldOfView(1.0f, _windowClientSize.Width / (float)_windowClientSize.Height, 0.1f, 100f);
        
        _time = Stopwatch.StartNew();
    }

    public void Draw()
    {
        if (_window.IsDisposed) return;

        var newWindowClientSize = _window.Dpi.LogicalToPixel(_window.ClientSize);
        if (newWindowClientSize != _windowClientSize)
        {
            _windowClientSize = newWindowClientSize;
            _swapChain.Resize((uint)_windowClientSize.Width, (uint)_windowClientSize.Height);
            _projection = Matrix4x4.CreatePerspectiveFieldOfView(1.0f, _windowClientSize.Width / (float)_windowClientSize.Height, 0.1f, 100f);
        }

        // Update the cube's model matrix (rotation)
        float time = (float)_time.ElapsedMilliseconds / 1000f;
        Matrix4x4 model = Matrix4x4.CreateRotationY(time) * Matrix4x4.CreateRotationX(time * 0.5f);

        Matrix4x4 mvp = model * _view * _projection;

        // Update command list
        _cl.Begin();
        _cl.SetFramebuffer(_swapChain.Framebuffer);
        _cl.SetFullViewports();
        _cl.ClearColorTarget(0, RgbaFloat.Black);
        _cl.ClearDepthStencil(1f);
        _cl.SetPipeline(_pipeline);
        _cl.SetGraphicsResourceSet(0, _resourceSet);
        _cl.UpdateBuffer(_matrixBuffer, 0, mvp);
        _cl.SetVertexBuffer(0, _vertexBuffer);
        _cl.Draw((uint)Vertices.Length);
        _cl.End();
        _graphicsDevice.SubmitCommands(_cl);

        _swapChain.SwapBuffers();
    }
    
    public void Dispose()
    {
        _graphicsDevice.WaitForIdle();

        _vertexBuffer.Dispose();
        _vertexShader.Dispose();
        _fragmentShader.Dispose();
        _pipeline.Dispose();
        _cl.Dispose();
        _matrixBuffer.Dispose();
        _resourceLayout.Dispose();
        _resourceSet.Dispose();
        _swapChain.Dispose();
        _graphicsDevice.Dispose();
        _graphicsManager.Dispose();
    }
    
    private static readonly VertexPositionColor[] Vertices =
    [
        new(new(-1.0f, -1.0f, -1.0f), RgbaFloat.Red), // Front
        new(new(-1.0f, 1.0f, -1.0f), RgbaFloat.Red),
        new(new(1.0f, 1.0f, -1.0f), RgbaFloat.Red),
        new(new(-1.0f, -1.0f, -1.0f), RgbaFloat.Red),
        new(new(1.0f, 1.0f, -1.0f), RgbaFloat.Red),
        new(new(1.0f, -1.0f, -1.0f), RgbaFloat.Red),

        new(new(-1.0f, -1.0f, 1.0f), RgbaFloat.Green), // Back
        new(new(1.0f, 1.0f, 1.0f), RgbaFloat.Green),
        new(new(-1.0f, 1.0f, 1.0f), RgbaFloat.Green),
        new(new(-1.0f, -1.0f, 1.0f), RgbaFloat.Green),
        new(new(1.0f, -1.0f, 1.0f), RgbaFloat.Green),
        new(new(1.0f, 1.0f, 1.0f), RgbaFloat.Green),

        new(new(-1.0f, 1.0f, -1.0f), RgbaFloat.Blue), // Top
        new(new(-1.0f, 1.0f, 1.0f), RgbaFloat.Blue),
        new(new(1.0f, 1.0f, 1.0f), RgbaFloat.Blue),
        new(new(-1.0f, 1.0f, -1.0f), RgbaFloat.Blue),
        new(new(1.0f, 1.0f, 1.0f), RgbaFloat.Blue),
        new(new(1.0f, 1.0f, -1.0f), RgbaFloat.Blue),

        new(new(-1.0f, -1.0f, -1.0f), RgbaFloat.Yellow), // Bottom
        new(new(1.0f, -1.0f, 1.0f), RgbaFloat.Yellow),
        new(new(-1.0f, -1.0f, 1.0f), RgbaFloat.Yellow),
        new(new(-1.0f, -1.0f, -1.0f), RgbaFloat.Yellow),
        new(new(1.0f, -1.0f, -1.0f), RgbaFloat.Yellow),
        new(new(1.0f, -1.0f, 1.0f), RgbaFloat.Yellow),

        new(new(-1.0f, -1.0f, -1.0f), RgbaFloat.Cyan), // Left
        new(new(-1.0f, -1.0f, 1.0f), RgbaFloat.Cyan),
        new(new(-1.0f, 1.0f, 1.0f), RgbaFloat.Cyan),
        new(new(-1.0f, -1.0f, -1.0f), RgbaFloat.Cyan),
        new(new(-1.0f, 1.0f, 1.0f), RgbaFloat.Cyan),
        new(new(-1.0f, 1.0f, -1.0f), RgbaFloat.Cyan),

        new(new(1.0f, -1.0f, -1.0f), RgbaFloat.Magenta), // Right
        new(new(1.0f, 1.0f, 1.0f), RgbaFloat.Magenta),
        new(new(1.0f, -1.0f, 1.0f), RgbaFloat.Magenta),
        new(new(1.0f, -1.0f, -1.0f), RgbaFloat.Magenta),
        new(new(1.0f, 1.0f, -1.0f), RgbaFloat.Magenta),
        new(new(1.0f, 1.0f, 1.0f), RgbaFloat.Magenta),
    ];
    
    private struct VertexPositionColor(Vector3 position, Vector4 color)
    {
        public Vector3 Position = position;
        public Vector4 Color = color;
    }
}
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Rendering;
using Avalonia.Threading;
using EasyClothes.Core.Rage;
using EasyClothes.Core.Textures;
using EasyClothes.Core.Viewing;
using Silk.NET.OpenGL;

namespace EasyClothes.App.Viewer;

public enum ShadingMode
{
    Solid,
    Wireframe,
    SolidWithWireframe,
}

public sealed class MeshViewport : OpenGlControlBase, ICustomHitTest
{
    public static readonly StyledProperty<ShadingMode> ShadingProperty =
        AvaloniaProperty.Register<MeshViewport, ShadingMode>(nameof(Shading));

    private readonly OrbitCamera _camera = new();
    private GL? _gl;
    private uint _program;
    private readonly List<GpuLayer> _layers = [];

    private IReadOnlyList<(RenderItem Item, bool Context)>? _pendingScene;
    private RgbaImage? _pendingItemTexture;
    private Point? _dragOrigin;
    private bool _panning;
    private (Vector3 Min, Vector3 Max) _bounds;

    public Action<RgbaImage>? SnapshotRequested { get; set; }

    public OrbitCamera Camera => _camera;

    public ShadingMode Shading
    {
        get => GetValue(ShadingProperty);
        set => SetValue(ShadingProperty, value);
    }

    // Context items (the mannequin) are drawn without the wireframe overlay. The camera is only
    // reframed when the scene's extent changes, so switching textures keeps the current view.
    public void ShowScene(IReadOnlyList<RenderItem> items, IReadOnlyList<RenderItem> context)
    {
        _pendingScene = [.. items.Select(i => (i, false)), .. context.Select(i => (i, true))];
        var bounds = MeshBounds.Of(items.Concat(context).SelectMany(i => i.Meshes));
        if (bounds != _bounds)
        {
            _bounds = bounds;
            ResetCamera();
        }
        RequestNextFrameRendering();
    }

    // Swaps the texture of the first non-context item without re-uploading the rest of the scene.
    public void ShowItemTexture(RgbaImage texture)
    {
        _pendingItemTexture = texture;
        RequestNextFrameRendering();
    }

    public void ResetCamera()
    {
        _camera.Frame(_bounds.Min, _bounds.Max);
        _camera.Yaw = 0;
        _camera.Pitch = 0.15f;
        RequestNextFrameRendering();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ShadingProperty) RequestNextFrameRendering();
    }

    public event Action<string>? RenderFailed;

    protected override void OnOpenGlInit(GlInterface gl)
    {
        try
        {
            Initialize(gl);
        }
        catch (Exception ex)
        {
            Fail("No se pudo inicializar OpenGL", ex);
        }
    }

    private void Initialize(GlInterface gl)
    {
        _gl = GL.GetApi(gl.GetProcAddress);
        var isEs = GlVersion.Type == GlProfileType.OpenGLES;
        _program = CompileProgram(_gl, Shaders.Header(isEs) + Shaders.Vertex, Shaders.Header(isEs) + Shaders.Fragment);
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        if (_gl is null) return;
        ClearLayers(_gl);
        _gl.DeleteProgram(_program);
        _gl = null;
    }

    protected override void OnOpenGlRender(GlInterface gl, int framebuffer)
    {
        if (_gl is null) return;
        try
        {
            Render(_gl);
        }
        catch (Exception ex)
        {
            Fail("Error al renderizar", ex);
        }
    }

    private void Fail(string message, Exception ex)
    {
        _gl = null;
        var text = $"{message} ({GlVersion.Type} {GlVersion.Major}.{GlVersion.Minor}): {ex.GetType().Name}: {ex.Message}";
        Console.Error.WriteLine(text);
        Dispatcher.UIThread.Post(() => RenderFailed?.Invoke(text));
    }

    private unsafe void Render(GL g)
    {
        UploadPending(g);

        var scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        var width = Math.Max(1, (int)(Bounds.Width * scale));
        var height = Math.Max(1, (int)(Bounds.Height * scale));
        g.Viewport(0, 0, (uint)width, (uint)height);
        g.ClearColor(0.11f, 0.12f, 0.14f, 1f);
        g.ClearDepth(1f);
        g.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        g.Enable(EnableCap.DepthTest);
        g.DepthFunc(DepthFunction.Lequal);
        g.Disable(EnableCap.CullFace);

        g.UseProgram(_program);
        var viewProjection = _camera.ViewMatrix * _camera.ProjectionMatrix((float)width / height);
        g.UniformMatrix4(g.GetUniformLocation(_program, "uViewProjection"), 1, false, (float*)&viewProjection);
        var light = Vector3.Normalize(_camera.Position - _camera.Target);
        g.Uniform3(g.GetUniformLocation(_program, "uLightDir"), light.X, light.Y, light.Z);
        var hasTexture = g.GetUniformLocation(_program, "uHasTexture");
        g.Uniform1(g.GetUniformLocation(_program, "uTexture"), 0);
        g.ActiveTexture(TextureUnit.Texture0);
        var flatColour = g.GetUniformLocation(_program, "uFlatColour");

        foreach (var (layer, mesh) in _layers.SelectMany(l => l.Meshes.Select(m => (l, m))))
        {
            g.Uniform1(hasTexture, layer.Texture == 0 ? 0 : 1);
            g.BindTexture(TextureTarget.Texture2D, layer.Texture);
            g.BindVertexArray(mesh.Vao);
            if (Shading != ShadingMode.Wireframe)
            {
                // Push triangles back so the wireframe overlay wins the depth test.
                g.Enable(EnableCap.PolygonOffsetFill);
                g.PolygonOffset(1f, 1f);
                g.Uniform4(flatColour, 0f, 0f, 0f, 0f);
                g.BindBuffer(BufferTargetARB.ElementArrayBuffer, mesh.TriangleIndices);
                g.DrawElements(PrimitiveType.Triangles, mesh.TriangleCount, DrawElementsType.UnsignedShort, null);
                g.Disable(EnableCap.PolygonOffsetFill);
            }
            if (Shading != ShadingMode.Solid && !layer.Context)
            {
                g.Uniform4(flatColour, 0.35f, 0.75f, 1f, 1f);
                g.BindBuffer(BufferTargetARB.ElementArrayBuffer, mesh.LineIndices);
                g.DrawElements(PrimitiveType.Lines, mesh.LineCount, DrawElementsType.UnsignedShort, null);
            }
        }
        g.BindVertexArray(0);

        if (SnapshotRequested is { } snapshot)
        {
            SnapshotRequested = null;
            var pixels = new byte[width * height * 4];
            fixed (byte* p = pixels)
                g.ReadPixels(0, 0, (uint)width, (uint)height, PixelFormat.Rgba, PixelType.UnsignedByte, p);
            var flipped = new byte[pixels.Length];
            for (var y = 0; y < height; y++)
                Array.Copy(pixels, (height - 1 - y) * width * 4, flipped, y * width * 4, width * 4);
            Dispatcher.UIThread.Post(() => snapshot(new RgbaImage(width, height, flipped)));
        }
    }

    // The GL surface draws nothing Avalonia can hit-test, so claim the whole area for input.
    public bool HitTest(Point point) => new Rect(Bounds.Size).Contains(point);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var properties = e.GetCurrentPoint(this).Properties;
        if (e.ClickCount == 2 && properties.IsLeftButtonPressed)
        {
            ResetCamera();
            return;
        }
        _panning = !properties.IsLeftButtonPressed || e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        _dragOrigin = e.GetPosition(this);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragOrigin is not { } origin) return;
        var position = e.GetPosition(this);
        var delta = position - origin;
        if (_panning)
        {
            var height = Math.Max(1, Bounds.Height);
            _camera.Pan((float)(-delta.X * 2 / height), (float)(delta.Y * 2 / height));
        }
        else
        {
            _camera.Rotate((float)delta.X * 0.01f, (float)delta.Y * 0.01f);
        }
        _dragOrigin = position;
        RequestNextFrameRendering();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragOrigin = null;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _dragOrigin = null;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        _camera.Zoom(MathF.Pow(0.9f, (float)e.Delta.Y));
        RequestNextFrameRendering();
        e.Handled = true;
    }

    private unsafe void UploadPending(GL g)
    {
        if (_pendingScene is { } scene)
        {
            _pendingScene = null;
            UploadScene(g, scene);
        }
        if (_pendingItemTexture is { } itemTexture && _layers.FindIndex(l => !l.Context) is var index and >= 0)
        {
            _pendingItemTexture = null;
            var layer = _layers[index];
            if (layer.Texture != 0) g.DeleteTexture(layer.Texture);
            _layers[index] = layer with { Texture = UploadTexture(g, itemTexture) };
        }
    }

    private void UploadScene(GL g, IReadOnlyList<(RenderItem Item, bool Context)> scene)
    {
        ClearLayers(g);
        foreach (var (item, context) in scene)
        {
            var texture = item.Texture is { } image ? UploadTexture(g, image) : 0u;
            _layers.Add(new GpuLayer(item.Meshes.Select(m => GpuMesh.Upload(g, m)).ToList(), texture, context));
        }
    }

    private static unsafe uint UploadTexture(GL g, RgbaImage image)
    {
        var texture = g.GenTexture();
        g.BindTexture(TextureTarget.Texture2D, texture);
        fixed (byte* p = image.Pixels)
            g.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba, (uint)image.Width, (uint)image.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, p);
        g.GenerateMipmap(TextureTarget.Texture2D);
        g.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
        g.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        return texture;
    }

    private void ClearLayers(GL g)
    {
        foreach (var layer in _layers)
        {
            foreach (var mesh in layer.Meshes) mesh.Dispose(g);
            if (layer.Texture != 0) g.DeleteTexture(layer.Texture);
        }
        _layers.Clear();
    }

    private static uint CompileProgram(GL g, string vertexSource, string fragmentSource)
    {
        var vs = CompileShader(g, ShaderType.VertexShader, vertexSource);
        var fs = CompileShader(g, ShaderType.FragmentShader, fragmentSource);
        var program = g.CreateProgram();
        g.AttachShader(program, vs);
        g.AttachShader(program, fs);
        g.LinkProgram(program);
        g.GetProgram(program, ProgramPropertyARB.LinkStatus, out var linked);
        g.DeleteShader(vs);
        g.DeleteShader(fs);
        if (linked == 0)
            throw new InvalidOperationException($"Shader link failed: {g.GetProgramInfoLog(program)}");
        return program;
    }

    private static uint CompileShader(GL g, ShaderType type, string source)
    {
        var shader = g.CreateShader(type);
        g.ShaderSource(shader, source);
        g.CompileShader(shader);
        g.GetShader(shader, ShaderParameterName.CompileStatus, out var compiled);
        if (compiled == 0)
            throw new InvalidOperationException($"{type} compile failed: {g.GetShaderInfoLog(shader)}");
        return shader;
    }

    private sealed record GpuLayer(List<GpuMesh> Meshes, uint Texture, bool Context);

    private sealed record GpuMesh(uint Vao, uint Vertices, uint TriangleIndices, uint TriangleCount, uint LineIndices, uint LineCount)
    {
        public static unsafe GpuMesh Upload(GL g, MeshData mesh)
        {
            var interleaved = new float[mesh.VertexCount * 8];
            for (var v = 0; v < mesh.VertexCount; v++)
            {
                Array.Copy(mesh.Positions, v * 3, interleaved, v * 8, 3);
                Array.Copy(mesh.Normals, v * 3, interleaved, v * 8 + 3, 3);
                Array.Copy(mesh.TexCoords, v * 2, interleaved, v * 8 + 6, 2);
            }
            var lines = mesh.WireframeIndices();

            var vao = g.GenVertexArray();
            g.BindVertexArray(vao);
            var vbo = g.GenBuffer();
            g.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
            g.BufferData<float>(BufferTargetARB.ArrayBuffer, interleaved, BufferUsageARB.StaticDraw);
            const uint stride = 8 * sizeof(float);
            g.EnableVertexAttribArray(0);
            g.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);
            g.EnableVertexAttribArray(1);
            g.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, (void*)(3 * sizeof(float)));
            g.EnableVertexAttribArray(2);
            g.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, stride, (void*)(6 * sizeof(float)));

            var tri = g.GenBuffer();
            g.BindBuffer(BufferTargetARB.ElementArrayBuffer, tri);
            g.BufferData<ushort>(BufferTargetARB.ElementArrayBuffer, mesh.Indices, BufferUsageARB.StaticDraw);
            var line = g.GenBuffer();
            g.BindBuffer(BufferTargetARB.ElementArrayBuffer, line);
            g.BufferData<ushort>(BufferTargetARB.ElementArrayBuffer, lines, BufferUsageARB.StaticDraw);
            g.BindVertexArray(0);

            return new GpuMesh(vao, vbo, tri, (uint)mesh.Indices.Length, line, (uint)lines.Length);
        }

        public void Dispose(GL g)
        {
            g.DeleteVertexArray(Vao);
            g.DeleteBuffer(Vertices);
            g.DeleteBuffer(TriangleIndices);
            g.DeleteBuffer(LineIndices);
        }
    }
}

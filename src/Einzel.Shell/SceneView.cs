using Avalonia;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Rendering;
using Avalonia.VisualTree;

using Einzel.Commands;

using Silk.NET.OpenGL;

namespace Einzel.Shell;

/// <summary>
/// The interactive viewport: conductors, the field, and the ions, drawn with OpenGL
/// through Avalonia's own GL control.
/// </summary>
/// <remarks>
/// <para>
/// <b>Avalonia is a 2-D toolkit, so the drawing is ours.</b> WPF had Helix Toolkit and a
/// scene graph; here there is a shader, a camera and a mesh upload, and nothing more -
/// the scene has no textures, no shadows, no animation and no picking. That is the price
/// of the shell running where the engine runs, and it is small because the hard part
/// stayed where UI-1 put it: <see cref="ViewportCommand"/> extracts the conductor
/// surfaces and flies the ions, and this control draws what it is handed.
/// </para>
/// <para>
/// <b>Three things a spike found the hard way, all of which look like a black window.</b>
/// A shader written <c>#version 330 core</c> fails on Windows, where Avalonia hands back
/// an OpenGL ES 3.0 context through ANGLE, and Avalonia catches the exception out of
/// <see cref="OnOpenGlInit"/> and quietly disables the control - the window opens, the
/// scene is black, and nothing says why. The control does not draw to the default
/// framebuffer, because Avalonia composites it, so every draw has to bind the one it is
/// handed. And the framebuffer is in physical pixels while <see cref="Visual.Bounds"/> is
/// in logical ones, so on a scaled display the scene lands in a corner of the surface.
/// </para>
/// </remarks>
public sealed class SceneView : OpenGlControlBase
{
    // One body, two dialects. GLSL 3.30 and GLSL ES 3.00 share layout qualifiers and
    // in/out, which is all this needs, so only the header differs.
    private const string VertexBody = """
    layout(location = 0) in vec3 aPosition;
    layout(location = 1) in vec3 aNormal;
    uniform mat4 uModelViewProjection;
    out vec3 vNormal;
    void main()
    {
        vNormal = aNormal;
        gl_Position = uModelViewProjection * vec4(aPosition, 1.0);
    }
    """;

    // Two-sided lambert with a little ambient. Surfaces are oriented from the signed
    // distance's own gradient, so the winding is right - but a viewport should not go
    // dark because one normal is backwards.
    private const string FragmentBody = """
    in vec3 vNormal;
    uniform vec3 uColor;
    uniform float uAlpha;
    out vec4 fragColor;
    void main()
    {
        vec3 light = normalize(vec3(0.4, 0.7, 1.0));
        float lambert = abs(dot(normalize(vNormal), light));
        fragColor = vec4(uColor * (0.25 + 0.75 * lambert), uAlpha);
    }
    """;

    private readonly List<Mesh> _conductors = [];
    private readonly List<Mesh> _density = [];
    private readonly List<Line> _paths = [];
    private readonly List<Line> _field = [];
    private readonly Lock _gate = new();

    private ViewportOutcome? _pending;

    private GL? _gl;
    private uint _program;
    private int _mvpLocation;
    private int _colorLocation;
    private int _alphaLocation;
    private Framing? _framing;

    /// <summary>What to draw, as the command layer measured it.</summary>
    /// <remarks>
    /// Set before the control is first realised. A viewport that changed scene mid-flight
    /// would have to tear down its buffers on the render thread, which is the next piece
    /// of work rather than this one.
    /// </remarks>
    public ViewportOutcome? Scene { get; init; }

    /// <summary>How opaque the conductors are, from zero to one.</summary>
    /// <remarks>
    /// <b>Opaque by default.</b> Transparency with no depth sort draws every buried
    /// interface where conductors touch or overlap - a segmented chain reads as a heap
    /// rather than as a rod - so it is asked for where it earns its keep, which is a lens
    /// or a guide whose ion flies down the bore.
    /// </remarks>
    public double ConductorOpacity
    {
        get;
        set
        {
            field = value;
            RequestNextFrameRendering();
        }
    } = 1.0;

    private (double Azimuth, double Elevation) _view = (-32.0, 24.0);

    /// <summary>Where the camera sits, in degrees of azimuth and elevation.</summary>
    /// <remarks>
    /// <para>
    /// <b>Iso by default, and a cross-section is why.</b> Both zero looks straight down the
    /// extrusion axis, which for a translational solve is the one direction that shows
    /// nothing: the conductors are deliberately <em>uncapped</em> prisms, because a
    /// cross-section says the geometry repeats along z and capping it would draw an end the
    /// model does not have. Looking down that axis there are no faces to light, so the
    /// conductors disappear and only the trajectories are left - measured, on the linear ion
    /// trap, where the section view drew 300 flights and none of the 11 electrodes.
    /// </para>
    /// <para>
    /// So the most informative direction for a cross-section is exactly the one an honest
    /// geometry cannot draw, and the answer is to come off the axis rather than to cap the
    /// prism. The named views make that a choice rather than a default nobody can change.
    /// </para>
    /// </remarks>
    public (double Azimuth, double Elevation) View
    {
        get => _view;
        set
        {
            _view = value;

            if (Scene is { } scene)
            {
                // Re-measuring is cheap and the upload is not: the meshes do not move when
                // the camera does, so only the framing is rebuilt.
                _framing = Framing.Measure(scene, value.Azimuth, value.Elevation);
                RequestNextFrameRendering();
            }
        }
    }

    /// <summary>Shows a newer frame of a run that is still going.</summary>
    /// <param name="frame">The run as it stands.</param>
    /// <remarks>
    /// <para>
    /// <b>Coalesced rather than queued, and that is the decision.</b> A viewport wants the
    /// newest packet, not every packet: a queue shows one from four frames ago with three
    /// behind it, falling further behind the longer it is watched. So a frame that arrives
    /// before the last one was drawn replaces it.
    /// </para>
    /// <para>
    /// The upload itself has to happen on the render thread, because that is where the GL
    /// context is - so this only hands the frame over and asks for a redraw.
    /// </para>
    /// </remarks>
    public void Show(ViewportOutcome frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        lock (_gate)
        {
            _pending = frame;
        }

        RequestNextFrameRendering();
    }

    /// <inheritdoc />
    protected override void OnOpenGlInit(GlInterface gl)
    {
        _gl = GL.GetApi(gl.GetProcAddress);

        _program = Link(_gl, Header(GlVersion));
        _mvpLocation = _gl.GetUniformLocation(_program, "uModelViewProjection");
        _colorLocation = _gl.GetUniformLocation(_program, "uColor");
        _alphaLocation = _gl.GetUniformLocation(_program, "uAlpha");

        if (Scene is not { } scene)
        {
            return;
        }

        UploadConductors(_gl, scene);
        UploadField(_gl, scene);
        UploadDensity(_gl, scene);
        UploadPaths(_gl, scene);
        _framing = Framing.Measure(scene, _view.Azimuth, _view.Elevation);

        _gl.Enable(EnableCap.DepthTest);
    }

    /// <inheritdoc />
    protected override unsafe void OnOpenGlRender(GlInterface gl, int fb)
    {
        if (_gl is not { } api)
        {
            return;
        }

        ViewportOutcome? arrived;

        lock (_gate)
        {
            // Cleared BEFORE the frame is read, so the race can post twice and can never
            // drop the last one.
            arrived = _pending;
            _pending = null;
        }

        if (arrived is { } frame)
        {
            // Only the density is rebuilt. The sequencer refuses a stage that moves an
            // electrode, so the conductors are identical at every instant by construction -
            // and re-uploading a trap's whole mesh every frame is what makes a watch stutter.
            foreach (var shell in _density)
            {
                api.DeleteVertexArray(shell.Vao);
                api.DeleteBuffer(shell.Vbo);
                api.DeleteBuffer(shell.Ebo);
            }

            _density.Clear();
            UploadDensity(api, frame);
        }

        var scaling = (this.GetVisualRoot() as IRenderRoot)?.RenderScaling ?? 1.0;
        var width = Math.Max(1, (int)Math.Round(Bounds.Width * scaling));
        var height = Math.Max(1, (int)Math.Round(Bounds.Height * scaling));

        // Avalonia composites this control, so it hands over a framebuffer object and
        // every draw has to name it. Drawing without binding it gives a frame that is
        // black even where the clear color should be, which reads as a renderer that
        // drew nothing at all.
        api.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)fb);
        api.Viewport(0, 0, (uint)width, (uint)height);

        // White, because a frame from here ends up in a report or a slide, and a plot on
        // near-black is usable only on the page it was made for. The cost is that the
        // shading carries the shape unaided by contrast against the ground.
        var (gr, gg, gb) = ColorRamp.Ground;
        api.ClearColor((float)gr, (float)gg, (float)gb, 1.0f);
        api.Clear((uint)(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit));
        api.Enable(EnableCap.DepthTest);
        api.UseProgram(_program);

        // Rebuilt per frame because it depends on the window's shape; the expensive half,
        // measuring the instrument, was done once.
        var mvp = _framing is { } framing
            ? framing.Project((double)width / height)
            : Framing.Identity();

        fixed (float* m = mvp)
        {
            api.UniformMatrix4(_mvpLocation, 1, false, m);
        }

        // THE PATHS FIRST, AND THE ORDER IS THE POINT. An ion flies down the bore, so it
        // is inside every electrode it passes; drawn after opaque metal it sits behind the
        // near wall at every pixel and the flight is simply not in the picture. Drawn
        // first, into depth, the conductors blend over it and the trajectory reads through.
        //
        // RND-8 is not this control's decision: a diffusive model has no paths in the
        // bundle at all, so an empty list is the transport mode's answer carried through
        // rather than something to re-derive from a pressure.
        api.Uniform1(_alphaLocation, 1.0f);

        // The field with the paths and before the metal, for the same reason: an
        // equipotential is drawn on the section plane, which runs through the bore, so
        // drawn after opaque conductors it is behind the near wall at every pixel.
        foreach (var contour in _field)
        {
            api.Uniform3(_colorLocation, contour.R, contour.G, contour.B);
            api.BindVertexArray(contour.Vao);
            api.DrawArrays(PrimitiveType.LineStrip, 0, (uint)contour.Count);
        }

        foreach (var path in _paths)
        {
            api.Uniform3(_colorLocation, path.R, path.G, path.B);
            api.BindVertexArray(path.Vao);
            api.DrawArrays(PrimitiveType.LineStrip, 0, (uint)path.Count);
        }

        var alpha = (float)Math.Clamp(ConductorOpacity, 0.0, 1.0);

        if (alpha < 1.0f)
        {
            api.Enable(EnableCap.Blend);
            api.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        }

        api.Uniform1(_alphaLocation, alpha);

        foreach (var mesh in _conductors)
        {
            api.Uniform3(_colorLocation, mesh.R, mesh.G, mesh.B);
            api.BindVertexArray(mesh.Vao);
            api.DrawElements(PrimitiveType.Triangles, (uint)mesh.Count, DrawElementsType.UnsignedInt, null);
        }

        if (alpha < 1.0f)
        {
            api.Disable(EnableCap.Blend);
        }

        // The density last and translucent, because it is what moves and it sits inside the
        // metal. Contours at decades below the peak rather than at even fractions: a density
        // spans orders of magnitude, so even spacing draws the top decade several times and
        // the extent not at all.
        if (_density.Count > 0)
        {
            api.Enable(EnableCap.Blend);
            api.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            api.Uniform1(_alphaLocation, 0.30f);

            foreach (var shell in _density)
            {
                api.Uniform3(_colorLocation, shell.R, shell.G, shell.B);
                api.BindVertexArray(shell.Vao);
                api.DrawElements(PrimitiveType.Triangles, (uint)shell.Count, DrawElementsType.UnsignedInt, null);
            }

            api.Disable(EnableCap.Blend);
        }
    }

    /// <inheritdoc />
    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        if (_gl is not { } api)
        {
            return;
        }

        foreach (var mesh in _conductors)
        {
            api.DeleteVertexArray(mesh.Vao);
            api.DeleteBuffer(mesh.Vbo);
            api.DeleteBuffer(mesh.Ebo);
        }

        foreach (var shell in _density)
        {
            api.DeleteVertexArray(shell.Vao);
            api.DeleteBuffer(shell.Vbo);
            api.DeleteBuffer(shell.Ebo);
        }

        foreach (var path in _paths.Concat(_field))
        {
            api.DeleteVertexArray(path.Vao);
            api.DeleteBuffer(path.Vbo);
        }

        _field.Clear();
        _density.Clear();
        _conductors.Clear();
        _paths.Clear();

        if (_program != 0)
        {
            api.DeleteProgram(_program);
            _program = 0;
        }

        _gl = null;
    }

    /// <summary>The <c>#version</c> line, and whatever the dialect makes mandatory.</summary>
    /// <remarks>
    /// ES additionally requires a declared default precision for floats, which desktop GL
    /// ignores rather than refuses - so it could be emitted unconditionally. It is emitted
    /// only for ES because a header that says what the context is is worth more than one
    /// that is merely accepted.
    /// </remarks>
    private static string Header(GlVersion version) =>
        version.Type == GlProfileType.OpenGLES
            ? "#version 300 es\nprecision highp float;\n\n"
            : "#version 330 core\n\n";

    private void UploadConductors(GL api, ViewportOutcome scene)
    {
        // A potential is signed, so the ramp is diverging and symmetric about earth:
        // stretching it across the observed range puts the neutral color at the
        // arithmetic middle, and an earthed tube gets painted the same as a genuinely
        // negative one. An electrode is colored by the peak its drive reaches rather
        // than by the DC it sits at - reading only the DC of a driven electrode is a
        // mistake this project has made six times.
        var span = scene.Conductors.Count == 0
            ? 1.0
            : scene.Conductors.Max(c => Math.Abs(c.PotentialVolts) + Math.Abs(c.DriveAmplitudeVolts));

        foreach (var conductor in scene.Conductors)
        {
            if (conductor.Triangles.Count == 0)
            {
                continue;
            }

            var peak = conductor.PotentialVolts
                + (Math.Sign(conductor.PotentialVolts is 0.0 ? 1.0 : conductor.PotentialVolts)
                   * Math.Abs(conductor.DriveAmplitudeVolts));

            var fraction = span > 0.0 ? 0.5 + (0.5 * Math.Clamp(peak / span, -1.0, 1.0)) : 0.5;
            var (r, g, b) = ColorRamp.Diverging(fraction);

            _conductors.Add(Mesh.Upload(api, conductor, (float)r, (float)g, (float)b));
        }
    }

    private void UploadField(GL api, ViewportOutcome scene)
    {
        // The same diverging ramp the conductors take, and symmetric about earth for the
        // same reason: stretching it across the observed range puts the neutral color at
        // the arithmetic middle, so an earthed contour would be painted like a negative one.
        var span = Math.Max(
            Math.Abs(scene.LowestPotentialVolts ?? 0.0),
            Math.Abs(scene.HighestPotentialVolts ?? 0.0));

        foreach (var level in scene.Equipotentials)
        {
            var fraction = span > 0.0
                ? 0.5 + (0.5 * Math.Clamp(level.PotentialVolts / span, -1.0, 1.0))
                : 0.5;

            var (r, g, b) = ColorRamp.Diverging(fraction);

            foreach (var polyline in level.PathsMm)
            {
                if (polyline.Count >= 6)
                {
                    _field.Add(Line.Upload(api, polyline, (float)r, (float)g, (float)b));
                }
            }
        }
    }

    private void UploadDensity(GL api, ViewportOutcome scene)
    {
        // Anchored on the decade rather than on this frame's own peak. A diffusing packet's
        // peak falls as it spreads, so levels taken per frame would fall with it, the
        // contours would stay the same size, and a film of a packet spreading would show a
        // packet doing nothing - which is not flicker, it is a lie.
        var deepest = scene.Density.Count == 0 ? 1 : scene.Density.Max(d => d.DecadesBelowPeak);

        foreach (var shell in scene.Density)
        {
            if (shell.Triangles.Count == 0)
            {
                continue;
            }

            var fraction = deepest > 0
                ? 1.0 - (Math.Clamp(shell.DecadesBelowPeak, 0, deepest) / (double)deepest)
                : 1.0;

            var (r, g, b) = ColorRamp.At(fraction);
            _density.Add(Mesh.Upload(api, shell, (float)r, (float)g, (float)b));
        }
    }

    private void UploadPaths(GL api, ViewportOutcome scene)
    {
        var low = scene.LowestEnergyEv ?? 0.0;
        var high = scene.HighestEnergyEv ?? low;

        // A degenerate range gives a half, not a division: a monoenergetic beam in a
        // field-free drift is the simplest model anyone writes, and dividing by a zero
        // width paints the bundle NaN.
        var width = high - low;

        foreach (var path in scene.Trajectories)
        {
            if (path.PointsMm.Count < 2)
            {
                continue;
            }

            var mean = path.EnergyEv.Count > 0 ? path.EnergyEv.Average() : low;
            var fraction = width > 0.0 ? Math.Clamp((mean - low) / width, 0.0, 1.0) : 0.5;
            var (r, g, b) = ColorRamp.At(fraction);

            _paths.Add(Line.Upload(api, path, (float)r, (float)g, (float)b));
        }
    }

    private static uint Link(GL api, string header)
    {
        var vertex = Compile(api, ShaderType.VertexShader, header + VertexBody);
        var fragment = Compile(api, ShaderType.FragmentShader, header + FragmentBody);

        var program = api.CreateProgram();
        api.AttachShader(program, vertex);
        api.AttachShader(program, fragment);
        api.LinkProgram(program);

        api.GetProgram(program, ProgramPropertyARB.LinkStatus, out var linked);

        if (linked == 0)
        {
            throw new InvalidOperationException(
                "the viewport's shader program did not link: " + api.GetProgramInfoLog(program));
        }

        api.DetachShader(program, vertex);
        api.DetachShader(program, fragment);
        api.DeleteShader(vertex);
        api.DeleteShader(fragment);

        return program;
    }

    private static uint Compile(GL api, ShaderType type, string source)
    {
        var shader = api.CreateShader(type);
        api.ShaderSource(shader, source);
        api.CompileShader(shader);

        api.GetShader(shader, ShaderParameterName.CompileStatus, out var compiled);

        if (compiled == 0)
        {
            // Named rather than swallowed. Avalonia catches what comes out of
            // OnOpenGlInit and disables the control, so a silent failure here is a black
            // window with no explanation anywhere - which is exactly how the dialect
            // mismatch presented.
            throw new InvalidOperationException(
                $"the viewport's {type} did not compile: {api.GetShaderInfoLog(shader)}");
        }

        return shader;
    }
}

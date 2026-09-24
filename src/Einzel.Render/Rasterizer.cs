namespace Einzel.Render;

/// <summary>A triangle mesh to rasterize, already colored.</summary>
/// <param name="VerticesMm">Positions as consecutive x, y, z triples, in millimetres.</param>
/// <param name="Normals">Normals, one triple per vertex; need not be unit length.</param>
/// <param name="Triangles">Vertex indices, three per triangle.</param>
/// <param name="R">Red, zero to one.</param>
/// <param name="G">Green, zero to one.</param>
/// <param name="B">Blue, zero to one.</param>
public sealed record RasterMesh(
    IReadOnlyList<double> VerticesMm,
    IReadOnlyList<double> Normals,
    IReadOnlyList<int> Triangles,
    double R,
    double G,
    double B);

/// <summary>A polyline to rasterize, already colored.</summary>
/// <param name="PointsMm">Consecutive x, y, z triples, in millimetres.</param>
/// <param name="R">Red, zero to one.</param>
/// <param name="G">Green, zero to one.</param>
/// <param name="B">Blue, zero to one.</param>
public sealed record RasterLine(IReadOnlyList<double> PointsMm, double R, double G, double B);

/// <summary>One layer, drawn whole before the next.</summary>
/// <param name="Meshes">Its surfaces.</param>
/// <param name="Lines">Its polylines.</param>
/// <param name="Alpha">How opaque it is, from zero to one.</param>
/// <param name="WritesDepth">Whether it hides what is drawn after it.</param>
public sealed record RasterLayer(
    IReadOnlyList<RasterMesh> Meshes, IReadOnlyList<RasterLine> Lines, double Alpha, bool WritesDepth);

/// <summary>How a surface is lit: a brightness for a normal, and one for a line.</summary>
/// <param name="Surface">Brightness from zero to one, given a normal's x, y and z.</param>
/// <param name="Line">Brightness of every line.</param>
public sealed record RasterLighting(Func<double, double, double, double> Surface, double Line);

/// <summary>
/// Draws triangles and lines into pixels, with a depth buffer and no GPU.
/// </summary>
/// <remarks>
/// <para>
/// <b>It decides nothing.</b> Colors, layer order, which layers are translucent, how a
/// surface is lit and where the camera is are all handed in - the caller that composes them
/// is the same one the interactive window draws from, so a still and the window show one
/// picture rather than two that agree today. This does what a GPU does with those inputs:
/// transform, depth-test, shade, blend.
/// </para>
/// <para>
/// <b>Managed, and on purpose.</b> RND-1 puts rendering in the engine and invariant 1 wants
/// it headless on a CI runner with no display, so an offscreen GL context would bring a
/// native dependency that one of the two platforms does not have by default. A software
/// rasterizer of a few hundred lines has no such cost and no license question (LIC-1).
/// </para>
/// <para>
/// <b>Orthographic only</b>, because the camera is: depth and every attribute then
/// interpolate linearly across a triangle in screen space, which is what makes plain
/// barycentric weights correct rather than an approximation needing a perspective divide.
/// </para>
/// <para>
/// <b>Supersampled</b>, then averaged, so edges and lines are antialiased without any
/// coverage arithmetic - the plainest correct method, and cheap at a still's size.
/// </para>
/// </remarks>
public static class Rasterizer
{
    /// <summary>Draws layers, in order, into an RGB image.</summary>
    /// <param name="layers">What to draw, in the order to draw it.</param>
    /// <param name="matrix">Column-major 4x4 from millimetres to clip space.</param>
    /// <param name="lighting">How surfaces and lines are lit.</param>
    /// <param name="ground">The background, as red, green and blue from zero to one.</param>
    /// <param name="width">Output width in pixels.</param>
    /// <param name="height">Output height in pixels.</param>
    /// <param name="supersample">Samples per output pixel along each axis.</param>
    /// <param name="lineWidth">Line width in output pixels.</param>
    /// <returns>Rows top to bottom, three bytes a pixel.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A size is not positive.</exception>
    /// <exception cref="ArgumentException">The matrix is not 4x4.</exception>
    public static byte[] Draw(
        IReadOnlyList<RasterLayer> layers,
        IReadOnlyList<double> matrix,
        RasterLighting lighting,
        (double R, double G, double B) ground,
        int width,
        int height,
        int supersample = 2,
        double lineWidth = 1.5)
    {
        ArgumentNullException.ThrowIfNull(layers);
        ArgumentNullException.ThrowIfNull(matrix);
        ArgumentNullException.ThrowIfNull(lighting);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(supersample);

        if (matrix.Count != 16)
        {
            throw new ArgumentException("a projection is a 4x4 matrix", nameof(matrix));
        }

        var canvas = new Canvas(width * supersample, height * supersample, ground, matrix);

        foreach (var layer in layers)
        {
            foreach (var line in layer.Lines)
            {
                canvas.Polyline(line, layer.Alpha, layer.WritesDepth, lighting.Line, lineWidth * supersample);
            }

            foreach (var mesh in layer.Meshes)
            {
                canvas.Mesh(mesh, layer.Alpha, layer.WritesDepth, lighting.Surface);
            }
        }

        return canvas.Downsample(supersample);
    }

    /// <summary>
    /// Crosses the bottom of an image with a hatched band, marking the figure as carrying a
    /// validity violation.
    /// </summary>
    /// <param name="rgb">The image, rows top to bottom, three bytes a pixel; changed in place.</param>
    /// <param name="width">Its width in pixels.</param>
    /// <param name="height">Its height in pixels.</param>
    /// <remarks>
    /// <b>RND-11 and GRD-5, in pixels.</b> A figure is the artifact most likely to be shown with
    /// none of the uncertainty apparatus attached, so a tainted one must look tainted without
    /// its metadata - the vector section draws the same band. It is kept to the bottom edge so
    /// it marks the figure without covering the instrument.
    /// </remarks>
    public static void Hatch(byte[] rgb, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(rgb);

        var band = Math.Max(6, height / 40);
        var stripe = Math.Max(3, band / 2);

        for (var y = height - band; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var on = ((x + y) / stripe) % 2 == 0;
                var i = 3 * ((y * width) + x);

                (rgb[i], rgb[i + 1], rgb[i + 2]) = on ? ((byte)165, (byte)19, (byte)28) : ((byte)255, (byte)255, (byte)255);
            }
        }
    }

    private sealed class Canvas
    {
        private readonly int _width;
        private readonly int _height;
        private readonly double[] _color;
        private readonly double[] _depth;
        private readonly IReadOnlyList<double> _m;

        public Canvas(int width, int height, (double R, double G, double B) ground, IReadOnlyList<double> matrix)
        {
            _width = width;
            _height = height;
            _m = matrix;
            _color = new double[3 * width * height];
            _depth = new double[width * height];

            Array.Fill(_depth, double.PositiveInfinity);

            for (var i = 0; i < width * height; i++)
            {
                _color[3 * i] = ground.R;
                _color[(3 * i) + 1] = ground.G;
                _color[(3 * i) + 2] = ground.B;
            }
        }

        /// <summary>Millimetres to pixel column, pixel row and depth.</summary>
        /// <remarks>
        /// Row zero is the top, as an image is stored, where clip space has +y up - the flip
        /// every framebuffer read-back needs. Smaller depth is nearer, which is the window's
        /// depth test.
        /// </remarks>
        private (double X, double Y, double Z) Project(double x, double y, double z)
        {
            var cx = (_m[0] * x) + (_m[4] * y) + (_m[8] * z) + _m[12];
            var cy = (_m[1] * x) + (_m[5] * y) + (_m[9] * z) + _m[13];
            var cz = (_m[2] * x) + (_m[6] * y) + (_m[10] * z) + _m[14];

            return ((cx + 1.0) * 0.5 * _width, (1.0 - cy) * 0.5 * _height, cz);
        }

        public void Mesh(
            RasterMesh mesh, double alpha, bool writesDepth, Func<double, double, double, double> brightness)
        {
            var v = mesh.VerticesMm;
            var n = mesh.Normals;
            var count = v.Count / 3;
            var screen = new (double X, double Y, double Z)[count];

            for (var i = 0; i < count; i++)
            {
                screen[i] = Project(v[3 * i], v[(3 * i) + 1], v[(3 * i) + 2]);
            }

            for (var t = 0; t + 2 < mesh.Triangles.Count; t += 3)
            {
                int a = mesh.Triangles[t], b = mesh.Triangles[t + 1], c = mesh.Triangles[t + 2];
                var (p0, p1, p2) = (screen[a], screen[b], screen[c]);

                var area = Edge(p0.X, p0.Y, p1.X, p1.Y, p2.X, p2.Y);

                // Zero area covers no pixel center; drawn edge-on it is a line and the
                // neighbouring faces carry it.
                if (area == 0.0 || !double.IsFinite(area))
                {
                    continue;
                }

                var minX = Math.Max(0, (int)Math.Floor(Math.Min(p0.X, Math.Min(p1.X, p2.X))));
                var maxX = Math.Min(_width - 1, (int)Math.Ceiling(Math.Max(p0.X, Math.Max(p1.X, p2.X))));
                var minY = Math.Max(0, (int)Math.Floor(Math.Min(p0.Y, Math.Min(p1.Y, p2.Y))));
                var maxY = Math.Min(_height - 1, (int)Math.Ceiling(Math.Max(p0.Y, Math.Max(p1.Y, p2.Y))));

                for (var py = minY; py <= maxY; py++)
                {
                    var sy = py + 0.5;

                    for (var px = minX; px <= maxX; px++)
                    {
                        var sx = px + 0.5;

                        var w0 = Edge(p1.X, p1.Y, p2.X, p2.Y, sx, sy) / area;
                        var w1 = Edge(p2.X, p2.Y, p0.X, p0.Y, sx, sy) / area;
                        var w2 = 1.0 - w0 - w1;

                        if (w0 < 0.0 || w1 < 0.0 || w2 < 0.0)
                        {
                            continue;
                        }

                        var depth = (w0 * p0.Z) + (w1 * p1.Z) + (w2 * p2.Z);
                        var index = (py * _width) + px;

                        if (!(depth < _depth[index]))
                        {
                            continue;
                        }

                        // Interpolated and then lit, per pixel, as the window's fragment
                        // shader does - not lit per vertex and interpolated.
                        var nx = (w0 * n[3 * a]) + (w1 * n[3 * b]) + (w2 * n[3 * c]);
                        var ny = (w0 * n[(3 * a) + 1]) + (w1 * n[(3 * b) + 1]) + (w2 * n[(3 * c) + 1]);
                        var nz = (w0 * n[(3 * a) + 2]) + (w1 * n[(3 * b) + 2]) + (w2 * n[(3 * c) + 2]);

                        var shade = brightness(nx, ny, nz);

                        Put(index, mesh.R * shade, mesh.G * shade, mesh.B * shade, alpha);

                        if (writesDepth)
                        {
                            _depth[index] = depth;
                        }
                    }
                }
            }
        }

        public void Polyline(RasterLine line, double alpha, bool writesDepth, double shade, double width)
        {
            var p = line.PointsMm;
            var half = 0.5 * width;

            for (var i = 0; i + 5 < p.Count; i += 3)
            {
                var a = Project(p[i], p[i + 1], p[i + 2]);
                var b = Project(p[i + 3], p[i + 4], p[i + 5]);

                var minX = Math.Max(0, (int)Math.Floor(Math.Min(a.X, b.X) - half));
                var maxX = Math.Min(_width - 1, (int)Math.Ceiling(Math.Max(a.X, b.X) + half));
                var minY = Math.Max(0, (int)Math.Floor(Math.Min(a.Y, b.Y) - half));
                var maxY = Math.Min(_height - 1, (int)Math.Ceiling(Math.Max(a.Y, b.Y) + half));

                var dx = b.X - a.X;
                var dy = b.Y - a.Y;
                var length2 = (dx * dx) + (dy * dy);

                for (var py = minY; py <= maxY; py++)
                {
                    for (var px = minX; px <= maxX; px++)
                    {
                        var sx = px + 0.5;
                        var sy = py + 0.5;

                        // Clamped to the segment, so a line ends where it ends rather than
                        // continuing along its own direction.
                        var t = length2 > 0.0
                            ? Math.Clamp((((sx - a.X) * dx) + ((sy - a.Y) * dy)) / length2, 0.0, 1.0)
                            : 0.0;

                        var ex = sx - (a.X + (t * dx));
                        var ey = sy - (a.Y + (t * dy));

                        if ((ex * ex) + (ey * ey) > half * half)
                        {
                            continue;
                        }

                        var depth = a.Z + (t * (b.Z - a.Z));
                        var index = (py * _width) + px;

                        if (!(depth < _depth[index]))
                        {
                            continue;
                        }

                        Put(index, line.R * shade, line.G * shade, line.B * shade, alpha);

                        if (writesDepth)
                        {
                            _depth[index] = depth;
                        }
                    }
                }
            }
        }

        public byte[] Downsample(int factor)
        {
            var width = _width / factor;
            var height = _height / factor;
            var rgb = new byte[3 * width * height];
            var samples = factor * factor;

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    double r = 0, g = 0, b = 0;

                    for (var j = 0; j < factor; j++)
                    {
                        for (var i = 0; i < factor; i++)
                        {
                            var s = 3 * ((((y * factor) + j) * _width) + (x * factor) + i);
                            r += _color[s];
                            g += _color[s + 1];
                            b += _color[s + 2];
                        }
                    }

                    var o = 3 * ((y * width) + x);
                    rgb[o] = Byte(r / samples);
                    rgb[o + 1] = Byte(g / samples);
                    rgb[o + 2] = Byte(b / samples);
                }
            }

            return rgb;
        }

        private void Put(int index, double r, double g, double b, double alpha)
        {
            var i = 3 * index;

            if (alpha >= 1.0)
            {
                _color[i] = r;
                _color[i + 1] = g;
                _color[i + 2] = b;
                return;
            }

            _color[i] = (r * alpha) + (_color[i] * (1.0 - alpha));
            _color[i + 1] = (g * alpha) + (_color[i + 1] * (1.0 - alpha));
            _color[i + 2] = (b * alpha) + (_color[i + 2] * (1.0 - alpha));
        }

        private static byte Byte(double value) =>
            (byte)Math.Clamp((int)Math.Round(value * 255.0), 0, 255);

        private static double Edge(double ax, double ay, double bx, double by, double px, double py) =>
            ((bx - ax) * (py - ay)) - ((by - ay) * (px - ax));
    }
}

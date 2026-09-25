using Einzel.Commands;

using Silk.NET.OpenGL;

namespace Einzel.Shell;

/// <summary>One conductor, uploaded and ready to draw.</summary>
/// <param name="Vao">The vertex array object.</param>
/// <param name="Vbo">Interleaved position and normal.</param>
/// <param name="Ebo">Triangle indices.</param>
/// <param name="Count">How many indices to draw.</param>
/// <param name="R">Red, zero to one.</param>
/// <param name="G">Green, zero to one.</param>
/// <param name="B">Blue, zero to one.</param>
public readonly record struct Mesh(uint Vao, uint Vbo, uint Ebo, int Count, float R, float G, float B)
{
    /// <summary>Uploads one mesh of a composed picture.</summary>
    /// <param name="api">The GL context.</param>
    /// <param name="mesh">A conductor or a density shell, already colored.</param>
    /// <returns>The uploaded mesh.</returns>
    /// <remarks>
    /// Position and normal are interleaved into one buffer because they are read together
    /// on every vertex, and because two buffers would double the bookkeeping for nothing. A
    /// conductor and a density shell differ in what they mean and not in how they are
    /// drawn, which is what lets one upload serve both.
    /// </remarks>
    public static Mesh Upload(GL api, PictureMesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);

        return Upload(api, mesh.VerticesMm, mesh.Normals, mesh.Triangles, (float)mesh.R, (float)mesh.G, (float)mesh.B);
    }

    private static unsafe Mesh Upload(
        GL api,
        IReadOnlyList<double> verticesMm,
        IReadOnlyList<double> normals,
        IReadOnlyList<int> triangles,
        float r,
        float g,
        float b)
    {
        ArgumentNullException.ThrowIfNull(api);

        var vertices = verticesMm.Count / 3;
        var interleaved = new float[vertices * 6];

        for (var v = 0; v < vertices; v++)
        {
            interleaved[(v * 6) + 0] = (float)verticesMm[(v * 3) + 0];
            interleaved[(v * 6) + 1] = (float)verticesMm[(v * 3) + 1];
            interleaved[(v * 6) + 2] = (float)verticesMm[(v * 3) + 2];
            interleaved[(v * 6) + 3] = (float)normals[(v * 3) + 0];
            interleaved[(v * 6) + 4] = (float)normals[(v * 3) + 1];
            interleaved[(v * 6) + 5] = (float)normals[(v * 3) + 2];
        }

        var indices = new uint[triangles.Count];

        for (var i = 0; i < indices.Length; i++)
        {
            indices[i] = (uint)triangles[i];
        }

        var vao = api.GenVertexArray();
        api.BindVertexArray(vao);

        var vbo = api.GenBuffer();
        api.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);

        fixed (float* data = interleaved)
        {
            api.BufferData(
                BufferTargetARB.ArrayBuffer,
                (nuint)(interleaved.Length * sizeof(float)),
                data,
                BufferUsageARB.StaticDraw);
        }

        var ebo = api.GenBuffer();
        api.BindBuffer(BufferTargetARB.ElementArrayBuffer, ebo);

        fixed (uint* data = indices)
        {
            api.BufferData(
                BufferTargetARB.ElementArrayBuffer,
                (nuint)(indices.Length * sizeof(uint)),
                data,
                BufferUsageARB.StaticDraw);
        }

        api.EnableVertexAttribArray(0);
        api.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), (void*)0);
        api.EnableVertexAttribArray(1);
        api.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), (void*)(3 * sizeof(float)));

        api.BindVertexArray(0);

        return new Mesh(vao, vbo, ebo, indices.Length, r, g, b);
    }
}

/// <summary>One trajectory, uploaded as a line strip.</summary>
/// <param name="Vao">The vertex array object.</param>
/// <param name="Vbo">Interleaved position and a placeholder normal.</param>
/// <param name="Count">How many points are in the strip.</param>
/// <param name="R">Red, zero to one.</param>
/// <param name="G">Green, zero to one.</param>
/// <param name="B">Blue, zero to one.</param>
public readonly record struct Line(uint Vao, uint Vbo, int Count, float R, float G, float B)
{
    /// <summary>Uploads one polyline of a composed picture.</summary>
    /// <param name="api">The GL context.</param>
    /// <param name="line">A trajectory or an equipotential, already colored.</param>
    /// <returns>The uploaded strip.</returns>
    /// <remarks>
    /// <para>
    /// Walked straight into the interleaved buffer. A first version re-wrapped every point
    /// into its own three-element array so it could share the trajectory's overload -
    /// thousands of short-lived allocations per equipotential level, to rebuild a layout the
    /// flat array already has.
    /// </para>
    /// <para>
    /// The normal attribute is filled with a constant rather than dropped, so one shader draws
    /// both meshes and lines - and the rasterizer lights a line with the same constant.
    /// </para>
    /// </remarks>
    public static Line Upload(GL api, PictureLine line)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(line);

        var pointsMm = line.PointsMm;
        var points = pointsMm.Count / 3;
        var interleaved = new float[points * 6];

        for (var p = 0; p < points; p++)
        {
            interleaved[(p * 6) + 0] = (float)pointsMm[(p * 3) + 0];
            interleaved[(p * 6) + 1] = (float)pointsMm[(p * 3) + 1];
            interleaved[(p * 6) + 2] = (float)pointsMm[(p * 3) + 2];
            interleaved[(p * 6) + 3] = 0f;
            interleaved[(p * 6) + 4] = 0f;
            interleaved[(p * 6) + 5] = 1f;
        }

        return Strip(api, interleaved, points, (float)line.R, (float)line.G, (float)line.B);
    }

    /// <summary>Hands an already-interleaved strip to OpenGL.</summary>
    private static unsafe Line Strip(GL api, float[] interleaved, int points, float r, float g, float b)
    {
        var vao = api.GenVertexArray();
        api.BindVertexArray(vao);

        var vbo = api.GenBuffer();
        api.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);

        fixed (float* data = interleaved)
        {
            api.BufferData(
                BufferTargetARB.ArrayBuffer,
                (nuint)(interleaved.Length * sizeof(float)),
                data,
                BufferUsageARB.StaticDraw);
        }

        api.EnableVertexAttribArray(0);
        api.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), (void*)0);
        api.EnableVertexAttribArray(1);
        api.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), (void*)(3 * sizeof(float)));

        api.BindVertexArray(0);

        return new Line(vao, vbo, points, r, g, b);
    }
}

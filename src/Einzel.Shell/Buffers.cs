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
    /// <summary>Uploads a conductor surface.</summary>
    /// <param name="api">The GL context.</param>
    /// <param name="conductor">The surface the command layer extracted.</param>
    /// <param name="r">Red, zero to one.</param>
    /// <param name="g">Green, zero to one.</param>
    /// <param name="b">Blue, zero to one.</param>
    /// <returns>The uploaded mesh.</returns>
    /// <remarks>
    /// Position and normal are interleaved into one buffer because they are read together
    /// on every vertex, and because two buffers would double the bookkeeping for nothing.
    /// </remarks>
    public static Mesh Upload(GL api, ConductorSurface conductor, float r, float g, float b)
    {
        ArgumentNullException.ThrowIfNull(conductor);

        return Upload(api, conductor.VerticesMm, conductor.Normals, conductor.Triangles, r, g, b);
    }

    /// <summary>Uploads a density shell.</summary>
    /// <param name="api">The GL context.</param>
    /// <param name="shell">One contour of the density, at a decade below the peak.</param>
    /// <param name="r">Red, zero to one.</param>
    /// <param name="g">Green, zero to one.</param>
    /// <param name="b">Blue, zero to one.</param>
    /// <returns>The uploaded mesh.</returns>
    /// <remarks>
    /// The same three arrays a conductor has, so the same upload. A density shell and a
    /// conductor differ in what they mean and not in how they are drawn, which is what lets
    /// one routine serve both - the same argument that has one marching-squares routine draw
    /// every conductor and every equipotential in the renderer.
    /// </remarks>
    public static Mesh Upload(GL api, DensityShell shell, float r, float g, float b)
    {
        ArgumentNullException.ThrowIfNull(shell);

        return Upload(api, shell.VerticesMm, shell.Normals, shell.Triangles, r, g, b);
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
    /// <summary>Uploads a trajectory.</summary>
    /// <param name="api">The GL context.</param>
    /// <param name="path">The flight the command layer produced.</param>
    /// <param name="r">Red, zero to one.</param>
    /// <param name="g">Green, zero to one.</param>
    /// <param name="b">Blue, zero to one.</param>
    /// <returns>The uploaded strip.</returns>
    /// <remarks>
    /// The normal attribute is filled with a constant rather than dropped, so one shader
    /// draws both meshes and paths. A second program for lines would be two shaders to keep
    /// in step for no gain: the fragment stage takes the absolute lambert, so a constant
    /// normal simply gives a flat colour.
    /// </remarks>
    public static unsafe Line Upload(GL api, TrajectoryPath path, float r, float g, float b)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(path);

        var points = path.PointsMm;
        var interleaved = new float[points.Count * 6];

        for (var p = 0; p < points.Count; p++)
        {
            interleaved[(p * 6) + 0] = (float)points[p][0];
            interleaved[(p * 6) + 1] = (float)points[p][1];
            interleaved[(p * 6) + 2] = (float)points[p][2];
            interleaved[(p * 6) + 3] = 0f;
            interleaved[(p * 6) + 4] = 0f;
            interleaved[(p * 6) + 5] = 1f;
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

        api.EnableVertexAttribArray(0);
        api.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), (void*)0);
        api.EnableVertexAttribArray(1);
        api.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), (void*)(3 * sizeof(float)));

        api.BindVertexArray(0);

        return new Line(vao, vbo, points.Count, r, g, b);
    }
}

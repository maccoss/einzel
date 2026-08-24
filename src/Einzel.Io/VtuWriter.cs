using System.Globalization;
using System.Text;
using Einzel.Transport.Integration;

namespace Einzel.Io;

/// <summary>
/// Writes trajectories as VTK unstructured grid (.vtu) files.
/// </summary>
/// <remarks>
/// <para>
/// RND-12 and the scope decision in spec section 17: fields, trajectories, and
/// density clouds export to VTU, and ParaView does the rest. "One writer buys an
/// entire visualization application" — and it lands in Phase 1, before any GUI
/// exists, which is what gives the CLI a complete visualisation story a year
/// before the shell.
/// </para>
/// <para>
/// The XML form is written directly rather than through VTK. Spec section 20
/// records the intent to own this: "VTK's XML formats are documented and simple",
/// and writing it removes a dependency for about a week of work. This is the
/// ASCII variant, which is larger than the appended-binary form but diffable and
/// openable in a text editor — the same argument RND-6 makes for keeping text as
/// text in vector figures.
/// </para>
/// <para>
/// GRD-12: a rendering never looks more precise than its data. The provenance
/// comment block at the head of every file records the engine version, the model
/// hash, and the sampling that produced it, so a file that outlives the
/// conversation still says where it came from.
/// </para>
/// </remarks>
public static class VtuWriter
{
    /// <summary>Writes a trajectory as a single polyline.</summary>
    /// <param name="samples">The sampled trajectory, in flight order.</param>
    /// <param name="provenance">Provenance lines recorded as an XML comment.</param>
    /// <returns>The .vtu document text.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="samples"/> is null.</exception>
    /// <exception cref="ArgumentException">The trajectory has fewer than two points.</exception>
    public static string WriteTrajectory(
        IReadOnlyList<TrajectorySample> samples,
        IReadOnlyList<string>? provenance = null)
    {
        ArgumentNullException.ThrowIfNull(samples);

        if (samples.Count < 2)
        {
            throw new ArgumentException(
                "a trajectory needs at least two points to form a polyline", nameof(samples));
        }

        var text = new StringBuilder(samples.Count * 96);
        var invariant = CultureInfo.InvariantCulture;

        text.Append("<?xml version=\"1.0\"?>\n");

        if (provenance is { Count: > 0 })
        {
            text.Append("<!--\n");

            foreach (var line in provenance)
            {
                // "--" would close the comment early; the only sanitising needed.
                text.Append("  ").Append(line.Replace("--", "- -", StringComparison.Ordinal)).Append('\n');
            }

            text.Append("-->\n");
        }

        text.Append("<VTKFile type=\"UnstructuredGrid\" version=\"1.0\" byte_order=\"LittleEndian\">\n");
        text.Append("  <UnstructuredGrid>\n");
        text.Append(invariant, $"    <Piece NumberOfPoints=\"{samples.Count}\" NumberOfCells=\"1\">\n");

        text.Append("      <Points>\n");
        text.Append("        <DataArray type=\"Float64\" NumberOfComponents=\"3\" format=\"ascii\">\n");

        foreach (var sample in samples)
        {
            text.Append(invariant, $"          {sample.Position.X:G17} {sample.Position.Y:G17} {sample.Position.Z:G17}\n");
        }

        text.Append("        </DataArray>\n");
        text.Append("      </Points>\n");

        text.Append("      <PointData Scalars=\"time\" Vectors=\"velocity\">\n");

        text.Append("        <DataArray type=\"Float64\" Name=\"time\" format=\"ascii\">\n");

        foreach (var sample in samples)
        {
            text.Append(invariant, $"          {sample.TimeSeconds:G17}\n");
        }

        text.Append("        </DataArray>\n");

        text.Append("        <DataArray type=\"Float64\" Name=\"speed\" format=\"ascii\">\n");

        foreach (var sample in samples)
        {
            text.Append(invariant, $"          {sample.Velocity.Length:G17}\n");
        }

        text.Append("        </DataArray>\n");

        text.Append("        <DataArray type=\"Float64\" Name=\"velocity\" NumberOfComponents=\"3\" format=\"ascii\">\n");

        foreach (var sample in samples)
        {
            text.Append(invariant, $"          {sample.Velocity.X:G17} {sample.Velocity.Y:G17} {sample.Velocity.Z:G17}\n");
        }

        text.Append("        </DataArray>\n");
        text.Append("      </PointData>\n");

        // One VTK_POLY_LINE cell (type 4) threading every point in flight order.
        text.Append("      <Cells>\n");
        text.Append("        <DataArray type=\"Int64\" Name=\"connectivity\" format=\"ascii\">\n          ");

        for (var i = 0; i < samples.Count; i++)
        {
            text.Append(invariant, $"{i} ");
        }

        text.Append("\n        </DataArray>\n");
        text.Append(invariant, $"        <DataArray type=\"Int64\" Name=\"offsets\" format=\"ascii\">\n          {samples.Count}\n        </DataArray>\n");
        text.Append("        <DataArray type=\"UInt8\" Name=\"types\" format=\"ascii\">\n          4\n        </DataArray>\n");
        text.Append("      </Cells>\n");

        text.Append("    </Piece>\n");
        text.Append("  </UnstructuredGrid>\n");
        text.Append("</VTKFile>\n");

        return text.ToString();
    }
}

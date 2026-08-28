using System.Globalization;
using System.Text.RegularExpressions;
using Einzel.Core.Errors;

namespace Einzel.Io;

/// <summary>
/// A scalar or vector array read from a VTK ImageData file.
/// </summary>
/// <param name="Name">The array's name in the file.</param>
/// <param name="Components">1 for a scalar, 3 for a vector.</param>
/// <param name="CountX">Nodes along x.</param>
/// <param name="CountY">Nodes along y.</param>
/// <param name="CountZ">Nodes along z.</param>
/// <param name="OriginSi">Position of node (0,0,0), in metres.</param>
/// <param name="SpacingSi">Node spacing along each axis, in metres.</param>
/// <param name="Values">
/// The samples, x fastest then y then z, with <paramref name="Components"/> numbers
/// per node - the order VTK reads an extent in.
/// </param>
public sealed record VtkImageArray(
    string Name,
    int Components,
    int CountX,
    int CountY,
    int CountZ,
    (double X, double Y, double Z) OriginSi,
    (double X, double Y, double Z) SpacingSi,
    double[] Values);

/// <summary>
/// Reads VTK ImageData, the format this engine already writes.
/// </summary>
/// <remarks>
/// <para>
/// GAS-1 asks a gas region to carry a bulk velocity <em>field</em>, and §21 lists
/// "gas velocity import" among Phase 3's deliverables. A field of that kind comes
/// out of a CFD code, and the interchange every CFD code can write is VTK - which
/// is also what <see cref="VtuWriter"/> already emits, so the format needs no
/// deciding and no new dependency. Reading a format carries no licence obligation;
/// linking a library would (RND-13).
/// </para>
/// <para>
/// <strong>ASCII only, and deliberately.</strong> Binary, appended and compressed
/// payloads are the majority of real VTK files and none of them is read here. That
/// is a stated subset in the same sense as EXT-7's JSON Schema subset: the
/// alternative is a base64 and zlib implementation inside a reader whose whole job
/// is to get a few thousand numbers into an array, and a file this cannot read is
/// refused by name rather than misread. ParaView will convert one in a single
/// "Save Data" with "Ascii" ticked.
/// </para>
/// </remarks>
public static class VtkImageData
{
    /// <summary>Reads one named array from an ImageData document.</summary>
    /// <param name="text">The file's text.</param>
    /// <param name="array">Which array to read, or null for the first one found.</param>
    /// <param name="path">The file's path, for the error object.</param>
    /// <returns>The array and the grid it lives on.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
    /// <exception cref="EinzelException">
    /// The document is not ImageData, the array is missing, or the payload is not
    /// ASCII.
    /// </exception>
    public static VtkImageArray Read(string text, string? array = null, string path = "/")
    {
        ArgumentNullException.ThrowIfNull(text);

        if (!text.Contains("type=\"ImageData\"", StringComparison.Ordinal))
        {
            throw Refuse(
                path,
                "this is not a VTK ImageData document",
                "a gas velocity field is read as ImageData (.vti), which is what a uniform grid "
                + "is. An unstructured grid (.vtu) carries every node coordinate explicitly and "
                + "is not read here");
        }

        var image = Match(text, "<ImageData([^>]*)>", path, "an <ImageData> element");

        var extent = Integers(Attribute(image, "WholeExtent", path), path, "WholeExtent", 6);
        var origin = Doubles(Attribute(image, "Origin", path), path, "Origin", 3);
        var spacing = Doubles(Attribute(image, "Spacing", path), path, "Spacing", 3);

        var countX = extent[1] - extent[0] + 1;
        var countY = extent[3] - extent[2] + 1;
        var countZ = extent[5] - extent[4] + 1;

        var pattern = array is null
            ? "<DataArray([^>]*)>(.*?)</DataArray>"
            : $"<DataArray([^>]*Name=\"{Regex.Escape(array)}\"[^>]*)>(.*?)</DataArray>";

        var found = Regex.Match(text, pattern, RegexOptions.Singleline, TimeSpan.FromSeconds(10));

        if (!found.Success)
        {
            var names = string.Join(
                ", ",
                Regex.Matches(text, "Name=\"([^\"]+)\"", RegexOptions.None, TimeSpan.FromSeconds(10))
                    .Select(m => m.Groups[1].Value)
                    .Distinct(StringComparer.Ordinal));

            throw Refuse(
                path,
                array is null ? "this document has no <DataArray>" : $"no array named '{array}'",
                names.Length > 0
                    ? $"arrays in this file: {names}"
                    : "the file appears to contain no data arrays at all");
        }

        var attributes = found.Groups[1].Value;
        var format = Attribute(attributes, "format", path);

        if (!string.Equals(format, "ascii", StringComparison.OrdinalIgnoreCase))
        {
            throw Refuse(
                path,
                $"the array's payload is '{format}', and only 'ascii' is read",
                "re-export with ASCII data. In ParaView that is Save Data with the Ascii box "
                + "ticked; binary, appended and compressed payloads are a stated gap rather than "
                + "an oversight, because decoding them here would mean base64 and zlib inside a "
                + "reader whose whole job is to get numbers into an array");
        }

        var components = attributes.Contains("NumberOfComponents", StringComparison.Ordinal)
            ? int.Parse(Attribute(attributes, "NumberOfComponents", path), CultureInfo.InvariantCulture)
            : 1;

        var name = attributes.Contains("Name=", StringComparison.Ordinal)
            ? Attribute(attributes, "Name", path)
            : "unnamed";

        var numbers = found.Groups[2].Value
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var expected = (long)countX * countY * countZ * components;

        if (numbers.Length != expected)
        {
            throw Refuse(
                path,
                $"array '{name}' holds {numbers.Length} numbers, and the extent "
                + $"{countX}x{countY}x{countZ} with {components} component(s) needs {expected}",
                "the extent and the payload disagree, so one of them is wrong. A truncated file "
                + "is the usual cause");
        }

        var values = new double[numbers.Length];

        for (var i = 0; i < numbers.Length; i++)
        {
            if (!double.TryParse(numbers[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]))
            {
                throw Refuse(
                    path,
                    $"'{numbers[i]}' in array '{name}' is not a number",
                    "the payload must be whitespace-separated decimal numbers");
            }
        }

        return new VtkImageArray(
            name,
            components,
            countX,
            countY,
            countZ,
            (origin[0], origin[1], origin[2]),
            (spacing[0], spacing[1], spacing[2]),
            values);
    }

    private static string Match(string text, string pattern, string path, string what)
    {
        var found = Regex.Match(text, pattern, RegexOptions.Singleline, TimeSpan.FromSeconds(10));

        return found.Success
            ? found.Groups[1].Value
            : throw Refuse(path, $"the document has no {what}", "check that the file is complete");
    }

    private static string Attribute(string attributes, string name, string path)
    {
        var found = Regex.Match(
            attributes, $"{Regex.Escape(name)}=\"([^\"]*)\"", RegexOptions.None, TimeSpan.FromSeconds(10));

        return found.Success
            ? found.Groups[1].Value
            : throw Refuse(
                path,
                $"a required attribute '{name}' is missing",
                "the element must carry it; a hand-edited file is the usual cause");
    }

    private static double[] Doubles(string text, string path, string what, int count)
    {
        var parts = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length != count)
        {
            throw Refuse(
                path, $"{what} has {parts.Length} values and needs {count}", $"supply {count}");
        }

        return [.. parts.Select(p => double.Parse(p, CultureInfo.InvariantCulture))];
    }

    private static int[] Integers(string text, string path, string what, int count) =>
        [.. Doubles(text, path, what, count).Select(v => (int)Math.Round(v))];

    private static EinzelException Refuse(string path, string constraint, string suggestion) =>
        new(new EinzelError
        {
            Code = ErrorCodes.SchemaInvalid,
            Path = path,
            Constraint = constraint,
            Suggestion = suggestion,
        });
}

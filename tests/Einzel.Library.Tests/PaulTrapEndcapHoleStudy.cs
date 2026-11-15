using System.Globalization;
using Einzel.Analysis;
using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Fields;
using Einzel.Fields.Solved;
using Einzel.Transport;
using Einzel.Transport.Integration;
using Xunit.Abstractions;

namespace Einzel.Library.Tests;

/// <summary>
/// Holes in the endcaps and a stretched endcap spacing: what a real quadrupole ion trap
/// has that the ideal hyperboloids do not, and what each does to the field.
/// </summary>
/// <remarks>
/// <para>
/// A real trap admits ions through a hole in its entrance endcap and ejects them through
/// a hole in its exit endcap to a detector behind it, and commercial instruments move the
/// endcaps outward - the stretched trap - to offset what the holes and the truncation do.
/// Both are declared parameters of the <c>paul-trap</c> template, defaulting to zero so the
/// template is unchanged to the bit, which the first test here asserts against a frozen
/// copy of the template as it was before they existed.
/// </para>
/// <para>
/// The multipoles are read from the on-axis potential. In the axisymmetric expansion
/// <c>phi = sum A_n rho^n P_n(cos theta)</c>, every Legendre polynomial is one on the axis,
/// so <c>phi(z, 0) = sum A_n z^n</c> and a polynomial fitted to the axial potential gives
/// each <c>A_n</c> directly. They are quoted as <c>A_n r0^(n-2) / A_2</c>, the
/// dimensionless weight relative to the quadrupole that the trap literature uses.
/// </para>
/// </remarks>
public sealed class PaulTrapEndcapHoleStudy(ITestOutputHelper output)
{
    /// <summary>The hole radius the measurements use, in mm: 4.8 radial cells as shipped.</summary>
    private const double HoleMm = 0.6;

    /// <summary>The stretch the measurements use: the recalled Finnigan figure, a guess.</summary>
    private const double Stretch = 0.107;

    private static ModelDocument Parse(string json, params (string Name, double Value)[] overrides)
    {
        var document = Io.ModelJson.Parse(json);

        var parameters = new Dictionary<string, ParameterDocument>(
            document.Parameters!, StringComparer.Ordinal);

        foreach (var (name, value) in overrides)
        {
            parameters[name] = parameters[name] with { Value = value };
        }

        return document with { Parameters = parameters };
    }

    private static ModelDocument Trap(params (string Name, double Value)[] overrides) =>
        Parse(DeviceTemplates.Read("paul-trap"), overrides);

    private static string Before() =>
        File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "paul-trap-before-endcap-holes.json"));

    private static CompiledModel Compile(ModelDocument document)
    {
        var validation = ModelValidator.Validate(document);

        Assert.True(
            validation.Model is not null,
            string.Join("; ", validation.Errors.Select(e => e.Constraint)));

        return validation.Model!;
    }

    /// <summary>The named geometries every measurement here is made on.</summary>
    private static readonly (string Name, (string, double)[] Overrides)[] Geometries =
    [
        ("ideal", []),
        ("both holes", [("endcapEntranceHoleRadius", HoleMm), ("endcapExitHoleRadius", HoleMm)]),
        ("exit hole only", [("endcapExitHoleRadius", HoleMm)]),
        ("stretch only", [("endcapStretchFraction", Stretch)]),
        ("holes + stretch",
            [("endcapEntranceHoleRadius", HoleMm), ("endcapExitHoleRadius", HoleMm),
             ("endcapStretchFraction", Stretch)]),
    ];

    /// <summary>
    /// With no holes and no stretch the template is the template before either existed,
    /// to the bit: every parameter, every outline vertex, every fixed node, every cut link,
    /// every solved potential.
    /// </summary>
    /// <remarks>
    /// The outline of a holed endcap runs from the hole's radius rather than from the axis,
    /// written so that a zero radius evaluates the same floating-point operations as before:
    /// <c>H + (R - H) k / 24</c> is <c>0 + (R k) / 24</c>, and adding zero and subtracting
    /// zero are exact. A stretch is added last to every axial coordinate it moves, for the
    /// same reason. Compared against a frozen copy of the template as it was, not against a
    /// tolerance: a field that is merely close is a different field, and every published
    /// number on this template would have to be re-measured.
    /// </remarks>
    [Fact]
    public void ZeroHolesAndZeroStretchAreTheTemplateBeforeThem()
    {
        var before = Compile(Parse(Before()));

        foreach (var after in new[]
        {
            Compile(Trap()),
            Compile(Trap(
                ("endcapEntranceHoleRadius", 0.0), ("endcapExitHoleRadius", 0.0),
                ("endcapStretchFraction", 0.0))),
        })
        {
            foreach (var (name, value) in before.Parameters.Values())
            {
                Assert.Equal(
                    BitConverter.DoubleToInt64Bits(value.SiValue),
                    BitConverter.DoubleToInt64Bits(after.Parameters[name].SiValue));
            }

            var a = before.Fields[0].Solve!;
            var b = after.Fields[0].Solve!;

            Assert.Equal(a.Electrodes.Count, b.Electrodes.Count);

            for (var e = 0; e < a.Electrodes.Count; e++)
            {
                Assert.Equal(a.Electrodes[e].Name, b.Electrodes[e].Name);
                Assert.Equal(a.Electrodes[e].Vertices.Count, b.Electrodes[e].Vertices.Count);

                for (var v = 0; v < a.Electrodes[e].Vertices.Count; v++)
                {
                    var (ax, ay) = a.Electrodes[e].Vertices[v];
                    var (bx, by) = b.Electrodes[e].Vertices[v];

                    Assert.Equal(BitConverter.DoubleToInt64Bits(ax), BitConverter.DoubleToInt64Bits(bx));
                    Assert.Equal(BitConverter.DoubleToInt64Bits(ay), BitConverter.DoubleToInt64Bits(by));
                }
            }

            var ca = GeometryBuilder.SolveChannels(a);
            var cb = GeometryBuilder.SolveChannels(b);

            Assert.Equal(ca.Count, cb.Count);

            for (var c = 0; c < ca.Count; c++)
            {
                Assert.Equal(ca[c].FixedNodes, cb[c].FixedNodes);
                Assert.Equal(ca[c].CutLinks, cb[c].CutLinks);
                Assert.Equal(ca[c].Report.Cycles, cb[c].Report.Cycles);

                var pa = ca[c].Potential.Values;
                var pb = cb[c].Potential.Values;

                Assert.Equal(pa.Length, pb.Length);

                var differing = 0;

                for (var k = 0; k < pa.Length; k++)
                {
                    if (BitConverter.DoubleToInt64Bits(pa[k]) != BitConverter.DoubleToInt64Bits(pb[k]))
                    {
                        differing++;
                    }
                }

                output.WriteLine($"channel {c}: {pa.Length} nodes, {differing} differ");
                Assert.Equal(0, differing);
            }
        }

        // The control, so the comparison above is known to see a change: a hole of one
        // radial cell is a different mask.
        var holed = GeometryBuilder.SolveChannels(
            Compile(Trap(("endcapExitHoleRadius", 0.13))).Fields[0].Solve!);
        var plain = GeometryBuilder.SolveChannels(before.Fields[0].Solve!);

        Assert.NotEqual(plain[0].FixedNodes, holed[0].FixedNodes);
    }

    /// <summary>The on-axis multipole weights, from a polynomial fit to the axial potential.</summary>
    /// <returns><c>A_n r0^(n-2) / A_2</c> for n = 0..8, and the fit's worst residual over A_2 r0^2.</returns>
    private static (double[] Weights, double Residual) AxialMultipoles(CompiledModel model, ITimeVaryingField field)
    {
        const int Degree = 8;
        const int Samples = 321;

        var r0 = model.Parameters["inscribedRadius"].SiValue;
        var half = 0.4 * r0;

        var gram = new double[Degree + 1, Degree + 1];
        var rhs = new double[Degree + 1];
        var us = new double[Samples];
        var phis = new double[Samples];

        for (var s = 0; s < Samples; s++)
        {
            var u = -1.0 + (2.0 * s / (Samples - 1));
            var phi = field.PotentialAt(new Vec3(u * half, 0.0, 0.0), 0.0);

            us[s] = u;
            phis[s] = phi;

            for (var i = 0; i <= Degree; i++)
            {
                rhs[i] += Math.Pow(u, i) * phi;

                for (var j = 0; j <= Degree; j++)
                {
                    gram[i, j] += Math.Pow(u, i + j);
                }
            }
        }

        var c = Solve(gram, rhs);

        var residual = 0.0;

        for (var s = 0; s < Samples; s++)
        {
            var fit = 0.0;

            for (var i = 0; i <= Degree; i++)
            {
                fit += c[i] * Math.Pow(us[s], i);
            }

            residual = Math.Max(residual, Math.Abs(fit - phis[s]));
        }

        // Back from the scaled variable u = z / half to z.
        var a = new double[Degree + 1];

        for (var i = 0; i <= Degree; i++)
        {
            a[i] = c[i] / Math.Pow(half, i);
        }

        var weights = new double[Degree + 1];

        for (var i = 0; i <= Degree; i++)
        {
            weights[i] = a[i] * Math.Pow(r0, i - 2) / a[2];
        }

        return (weights, residual / Math.Abs(a[2] * r0 * r0));
    }

    private static double[] Solve(double[,] matrix, double[] rhs)
    {
        var n = rhs.Length;
        var m = (double[,])matrix.Clone();
        var b = (double[])rhs.Clone();

        for (var col = 0; col < n; col++)
        {
            var pivot = col;

            for (var row = col + 1; row < n; row++)
            {
                if (Math.Abs(m[row, col]) > Math.Abs(m[pivot, col]))
                {
                    pivot = row;
                }
            }

            for (var k = 0; k < n; k++)
            {
                (m[col, k], m[pivot, k]) = (m[pivot, k], m[col, k]);
            }

            (b[col], b[pivot]) = (b[pivot], b[col]);

            for (var row = col + 1; row < n; row++)
            {
                var f = m[row, col] / m[col, col];

                for (var k = col; k < n; k++)
                {
                    m[row, k] -= f * m[col, k];
                }

                b[row] -= f * b[col];
            }
        }

        var x = new double[n];

        for (var row = n - 1; row >= 0; row--)
        {
            var sum = b[row];

            for (var k = row + 1; k < n; k++)
            {
                sum -= m[row, k] * x[k];
            }

            x[row] = sum / m[row, row];
        }

        return x;
    }

    /// <summary>Curvature at the center: dEz/dz, dEr/dr, and the effective radius.</summary>
    private static (double Axial, double Radial, double Radius) Curvature(
        CompiledModel model, ITimeVaryingField field, double delta)
    {
        var volts = model.Parameters["rfAmplitude"].SiValue;

        var axial =
            (field.ElectricFieldAt(new Vec3(delta, 0.0, 0.0), 0.0).X
                - field.ElectricFieldAt(new Vec3(-delta, 0.0, 0.0), 0.0).X)
            / (2.0 * delta);

        var radial =
            (field.ElectricFieldAt(new Vec3(0.0, delta, 0.0), 0.0).Y
                - field.ElectricFieldAt(new Vec3(0.0, -delta, 0.0), 0.0).Y)
            / (2.0 * delta);

        return (axial, radial, Math.Sqrt(2.0 * volts / axial));
    }

    private sealed record Multipoles(
        string Name, double Cells, double[] Weights, double Residual,
        double Ratio, double EffectiveMm, string SharedFaceWarnings);

    private static Multipoles Measure(string name, double cells, (string, double)[] overrides)
    {
        var all = overrides.Append(("cellsPerRadius", cells)).ToArray();
        var model = Compile(Trap(all));
        var solve = model.Fields[0].Solve!;
        var channels = GeometryBuilder.SolveChannels(solve);
        var field = (ITimeVaryingField)FieldAssembly.Build(model);

        var (weights, residual) = AxialMultipoles(model, field);
        var (axial, radial, radius) = Curvature(model, field, 0.4e-3);

        return new Multipoles(
            name, cells, weights, residual, axial / radial, radius * 1e3,
            string.Join(" | ", channels[0].Report.Warnings.Select(w => w.Code + ": " + w.Message)));
    }

    /// <summary>
    /// What the holes and the stretch do to the multipoles, at two meshes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Asserted are the properties fixed by symmetry and by the direction of each change,
    /// not the magnitudes, which are in <c>docs/device-templates.md</c> with this test as
    /// their recorded command: two equal holes keep the trap symmetric about its center
    /// plane, so every odd weight vanishes; one hole breaks that and the odd weights appear;
    /// and a mesh twice as fine changes the octupole weight by much less than any of the
    /// geometric changes it is quoted beside.
    /// </para>
    /// </remarks>
    [Fact]
    public void HolesAndStretchMoveTheMultipoles()
    {
        var rows = new List<Multipoles>();

        foreach (var cells in new[] { 20.0, 40.0 })
        {
            foreach (var (name, overrides) in Geometries)
            {
                rows.Add(Measure(name, cells, overrides));
            }
        }

        output.WriteLine(
            "geometry           cells    A1/A2 r0^-1   A3/A2     A4/A2      A6/A2      ratio      r0_eff(mm)  fit   warn");

        foreach (var row in rows)
        {
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{row.Name,-18} {row.Cells,4:F0}  {row.Weights[1],11:E3}  {row.Weights[3],9:E2}  "
                + $"{row.Weights[4],9:E3}  {row.Weights[6],9:E2}  {row.Ratio,9:F5}  {row.EffectiveMm,9:F5}  "
                + $"{row.Residual:E1}"));
        }

        output.WriteLine("mesh warnings, ideal at 20 cells: " + rows[0].SharedFaceWarnings);

        Multipoles At(string name, double cells) =>
            rows.Single(r => r.Name == name && r.Cells == cells);

        foreach (var cells in new[] { 20.0, 40.0 })
        {
            // Symmetric about the center plane: odd weights are round-off.
            foreach (var name in new[] { "ideal", "both holes", "stretch only", "holes + stretch" })
            {
                Assert.True(Math.Abs(At(name, cells).Weights[3]) < 1e-4, $"{name}: A3 {At(name, cells).Weights[3]:E2}");
                Assert.True(Math.Abs(At(name, cells).Weights[1]) < 1e-4, $"{name}: A1 {At(name, cells).Weights[1]:E2}");
            }

            // One hole: the symmetry is gone, and with it the odd weights' excuse.
            Assert.True(
                Math.Abs(At("exit hole only", cells).Weights[3]) > 10.0 * Math.Abs(At("ideal", cells).Weights[3]) + 1e-4,
                "one hole should add a hexapole");

            // No warning a hole adds. The ideal template already carries one - the ring's
            // outer face lying in the grounded domain edge, recorded and harmless in
            // docs/numerics.md - and its node count moves only with the domain's length,
            // which the stretch changes; a hole bore kept off the node rows adds nothing.
            Assert.Contains("'ring' meets the grounded top edge", At("ideal", cells).SharedFaceWarnings, StringComparison.Ordinal);
            Assert.Equal(At("ideal", cells).SharedFaceWarnings, At("both holes", cells).SharedFaceWarnings);
            Assert.Equal(At("ideal", cells).SharedFaceWarnings, At("exit hole only", cells).SharedFaceWarnings);
            Assert.Equal(At("stretch only", cells).SharedFaceWarnings, At("holes + stretch", cells).SharedFaceWarnings);
        }

        // The mesh: twice as fine moves the octupole weight by less than a tenth of what
        // the holes or the stretch move it by.
        foreach (var name in new[] { "both holes", "holes + stretch", "stretch only" })
        {
            var geometric = Math.Abs(At(name, 40.0).Weights[4] - At("ideal", 40.0).Weights[4]);
            var mesh = Math.Abs(At(name, 40.0).Weights[4] - At(name, 20.0).Weights[4]);

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{name}: octupole moved {geometric:E2} by geometry, {mesh:E2} by the mesh"));

            Assert.True(mesh < 0.1 * geometric, $"{name}: the mesh moves A4 by {mesh:E2} against {geometric:E2}");
        }
    }
}

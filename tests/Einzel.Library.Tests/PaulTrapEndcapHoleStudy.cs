using System.Globalization;
using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Fields;
using Einzel.Fields.Solved;
using Xunit.Abstractions;
using static Einzel.Library.Tests.PaulTrapHoles;

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
/// All are declared parameters of the <c>paul-trap</c> template, defaulting to zero, and at
/// zero the template is the ideal hyperboloid trap, which the first test checks against the
/// hyperboloids evaluated here rather than against a frozen copy of the file.
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
    /// <summary>
    /// The document with the hole and stretch machinery taken out: every bore radius and the
    /// stretch replaced by zero, and the entrance stop removed.
    /// </summary>
    private static ModelDocument Bypassed()
    {
        var document = Trap();

        var parameters = new Dictionary<string, ParameterDocument>(
            document.Parameters!, StringComparer.Ordinal);

        foreach (var name in new[] { "endcapEntranceBoreRadius", "endcapExitBoreRadius", "endcapStretch" })
        {
            parameters[name] = parameters[name] with { Expression = "0 * inscribedRadius" };
        }

        var field = document.Fields![0];
        var solve = field.Solve!;

        return document with
        {
            Parameters = parameters,
            Fields =
            [
                field with
                {
                    Solve = solve with
                    {
                        Electrodes = solve.Electrodes!.Where(e => e.Name != "entranceStop").ToList(),
                    },
                },
            ],
        };
    }

    /// <summary>
    /// With no holes and no stretch the endcaps and ring are the ideal hyperboloids, and the
    /// solved field is the field of the template with the hole and stretch machinery bypassed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The outlines are compared with the hyperboloids evaluated here, independently of the
    /// template's expressions: the ring <c>r = r0 sqrt(1 + (z/z0)^2)</c> and each endcap
    /// <c>z = z0 sqrt(1 + (r/r0)^2)</c>, sampled at the template's 25 points, to a part in
    /// 1e14 of r0 - so a later change to <c>paul-trap</c> that keeps those surfaces still
    /// passes, which a frozen copy of the whole file could not.
    /// </para>
    /// <para>
    /// The field is compared bit for bit with the same document whose bore radii and stretch
    /// are replaced by zero and whose entrance stop is removed: what the machinery adds when
    /// everything is closed is exactly nothing. The bores are written so that a zero hole
    /// evaluates the same floating-point operations as no hole - <c>H + (R - H) k / 24</c> is
    /// <c>0 + (R k) / 24</c>, and adding and subtracting zero are exact - and the stop has no
    /// width and lies a cell outside the domain.
    /// </para>
    /// </remarks>
    [Fact]
    public void ZeroHolesAndZeroStretchAreTheIdealHyperboloids()
    {
        var model = Compile(Trap());
        var solve = model.Fields[0].Solve!;

        var r0 = model.Parameters["inscribedRadius"].SiValue;
        var z0 = r0 / Math.Sqrt(2.0);
        var gap = model.Parameters["electrodeGap"].SiValue;
        var endcapRadius = r0 - gap;
        var ringHalf = z0 - gap;
        var tolerance = 1e-14 * r0;

        Assert.Equal(
            ["ring", "endcapEntrance", "endcapExit", "entranceStop"],
            solve.Electrodes.Select(e => e.Name).ToArray());

        var ring = solve.Electrodes[0].Vertices;
        var entrance = solve.Electrodes[1].Vertices;
        var exit = solve.Electrodes[2].Vertices;

        for (var k = 0; k <= 24; k++)
        {
            var z = -ringHalf + (2.0 * ringHalf * k / 24.0);
            Assert.Equal(z, ring[k].X, tolerance);
            Assert.Equal(r0 * Math.Sqrt(1.0 + ((z / z0) * (z / z0))), ring[k].Y, tolerance);

            var r = endcapRadius * k / 24.0;
            var face = z0 * Math.Sqrt(1.0 + ((r / r0) * (r / r0)));
            Assert.Equal(-face, entrance[k].X, tolerance);
            Assert.Equal(r, entrance[k].Y, tolerance);
            Assert.Equal(face, exit[k].X, tolerance);
            Assert.Equal(r, exit[k].Y, tolerance);
        }

        // The closed endcap's bore edge is on the axis, and the stop has no width and lies
        // outside the domain.
        Assert.Equal(0.0, entrance[^1].Y);
        Assert.Equal(0.0, exit[^1].Y);

        var stop = solve.Electrodes[3];
        var half = model.Parameters["domainHalfLength"].SiValue;

        Assert.Equal(stop.MinX, stop.MaxX);
        Assert.True(stop.MaxX < -half, "a closed entrance's stop should lie outside the domain");

        // The field, against the machinery bypassed.
        var bypassed = Compile(Bypassed());
        var a = GeometryBuilder.SolveChannels(bypassed.Fields[0].Solve!);
        var b = GeometryBuilder.SolveChannels(solve);

        Assert.Equal(a.Count, b.Count);

        for (var c = 0; c < a.Count; c++)
        {
            Assert.Equal(a[c].FixedNodes, b[c].FixedNodes);
            Assert.Equal(a[c].CutLinks, b[c].CutLinks);

            var pa = a[c].Potential.Values;
            var pb = b[c].Potential.Values;
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

        // The control, so the comparison above is known to see a change: the smallest hole a
        // fraction can say is a different mask.
        var holed = GeometryBuilder.SolveChannels(
            Compile(Trap(("endcapExitHoleFraction", 0.025))).Fields[0].Solve!);

        Assert.NotEqual(a[0].FixedNodes, holed[0].FixedNodes);
    }

    /// <summary>
    /// A hole is bounded by the endcap it is cut in: a fraction past 0.9 is refused at the
    /// parameter, so an endcap whose outline runs backwards cannot be declared.
    /// </summary>
    [Fact]
    public void AHoleCannotReachTheEdgeOfItsEndcap()
    {
        foreach (var name in new[] { "endcapEntranceHoleFraction", "endcapExitHoleFraction" })
        {
            Compile(Trap((name, 0.9)));

            var refused = ModelValidator.Validate(Trap((name, 1.0)));

            Assert.Null(refused.Model);
            Assert.Contains(refused.Errors, e => e.Path.Contains(name, StringComparison.Ordinal));
        }

        // At the largest fraction, at the coarsest mesh the parameters allow, the bore is
        // still inside the endcap: the offset cannot carry it to the edge.
        var model = Compile(Trap(("endcapExitHoleFraction", 0.9), ("cellsPerRadius", 4.0)));

        Assert.True(
            model.Parameters["endcapExitBoreRadius"].SiValue < model.Parameters["endcapOuterRadius"].SiValue);
    }

    /// <summary>
    /// The bore faces of the hole sizes a document would write are off the mesh's node rows,
    /// and the field is continuous as a declared hole crosses one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A conductor face lying exactly on a row of nodes is cut discontinuously
    /// (<c>docs/numerics.md</c>, "a single conductor's face on a node plane"), and the radial
    /// spacing is 0.125 mm as shipped, so a bore of 0.5, 1.0 or 1.5 mm would sit on one. The
    /// template moves every bore out by <c>endcapBoreOffset</c>, a sixteenth of a cell. Checked
    /// from the solved mesh itself: every multiple of 0.025 of the endcap radius, at 20 and 40
    /// cells per r0, lands at least 0.02 of a radial spacing off the nearest row.
    /// </para>
    /// <para>
    /// And the field quantity that would show a step - the axial potential near the exit
    /// endcap - is continuous as the declared hole is swept through 0.5 mm, exactly four
    /// radial spacings: the change across a step of 1e-9 of the hole is the slope times the
    /// step and no more.
    /// </para>
    /// </remarks>
    [Fact]
    public void BoreFacesAreOffTheNodeRows()
    {
        foreach (var cells in new[] { 20.0, 40.0 })
        {
            var reference = Compile(Trap(("cellsPerRadius", cells)));
            var grid = GeometryBuilder.SolveChannels(reference.Fields[0].Solve!)[0].Potential.Grid;
            var spacing = grid.SpacingY;
            var worst = double.MaxValue;

            for (var k = 1; k <= 36; k++)
            {
                var fraction = 0.025 * k;
                var model = Compile(Trap(("cellsPerRadius", cells), ("endcapExitHoleFraction", fraction)));
                var bore = model.Parameters["endcapExitBoreRadius"].SiValue;
                var rows = (bore - grid.OriginY) / spacing;
                var off = Math.Abs(rows - Math.Round(rows));

                worst = Math.Min(worst, off);
            }

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{cells,3:F0} cells/r0: radial spacing {spacing * 1e3:F5} mm; worst bore face {worst:F4} of a spacing off a row"));

            Assert.True(worst >= 0.02 - 1e-9, $"a bore face is {worst:F4} of a spacing off a row");
        }

        // Continuity through a declared hole of exactly four radial spacings.
        var endcapRadius = Compile(Trap()).Parameters["endcapOuterRadius"].SiValue;
        var crossing = 0.5e-3 / endcapRadius;
        var step = 1e-9 * crossing;

        double Probe(double fraction)
        {
            var model = Compile(Trap(("endcapExitHoleFraction", fraction)));
            var field = (ITimeVaryingField)FieldAssembly.Build(model);
            var z0 = model.Parameters["endcapHalfSpacing"].SiValue;

            return field.PotentialAt(new Vec3(0.8 * z0, 0.0, 0.0), 0.0);
        }

        var below = Probe(crossing - step);
        var at = Probe(crossing);
        var above = Probe(crossing + step);
        var slope = Math.Abs(Probe(crossing + (1e-3 * crossing)) - at) / (1e-3 * crossing);

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"axial potential at 0.8 z0 across a declared 0.5 mm hole: {below:R} / {at:R} / {above:R} V; slope {slope:E3} V per unit fraction"));

        Assert.True(Math.Abs(above - below) <= (10.0 * slope * 2.0 * step) + 1e-12);
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

    private sealed record Multipoles(
        string Name, double Cells, double[] Weights, double Residual,
        double Ratio, double EffectiveMm, string SharedFaceWarnings);

    private static Multipoles Measure(string name, double cells, (string, double)[] overrides)
    {
        var model = Compile(Trap(overrides.Append(("cellsPerRadius", cells)).ToArray()));
        var channels = GeometryBuilder.SolveChannels(model.Fields[0].Solve!);
        var field = (ITimeVaryingField)FieldAssembly.Build(model);
        var volts = model.Parameters["rfAmplitude"].SiValue;

        var (weights, residual) = AxialMultipoles(model, field);
        var ratio = AxialCurvature(field, 0.4e-3) / RadialCurvature(field, 0.4e-3);

        return new Multipoles(
            name, cells, weights, residual, ratio, EffectiveRadius(field, volts) * 1e3,
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
            "geometry           cells    A1/A2 r0^-1   A3/A2     A4/A2      A6/A2      ratio      r0_eff(mm)  fit");

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

            // No warning a hole or its stop adds. The ideal template already carries one - the
            // ring's outer face lying in the grounded domain edge, recorded and harmless in
            // docs/numerics.md - and its node count moves only with the domain's length, which
            // the stretch changes; a bore kept off the node rows, and a grounded stop flush with
            // the grounded edge, add nothing.
            Assert.Contains("'ring' meets the grounded top edge", At("ideal", cells).SharedFaceWarnings, StringComparison.Ordinal);
            Assert.Equal(At("ideal", cells).SharedFaceWarnings, At("both holes", cells).SharedFaceWarnings);
            Assert.Equal(At("ideal", cells).SharedFaceWarnings, At("exit hole only", cells).SharedFaceWarnings);
            Assert.Equal(At("stretch only", cells).SharedFaceWarnings, At("holes + stretch", cells).SharedFaceWarnings);
        }

        // The mesh: twice as fine moves the octupole weight by much less than the holes or the
        // stretch move it - measured at 0.02 of the change for both holes and 0.10 for holes
        // plus stretch, whose change is the smallest, so asserted under a fifth. A bound at
        // exactly the measured ratio would be asserting which side of it the arithmetic lands.
        foreach (var name in new[] { "both holes", "holes + stretch", "stretch only" })
        {
            var geometric = Math.Abs(At(name, 40.0).Weights[4] - At("ideal", 40.0).Weights[4]);
            var mesh = Math.Abs(At(name, 40.0).Weights[4] - At(name, 20.0).Weights[4]);

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{name}: octupole moved {geometric:E2} by geometry, {mesh:E2} by the mesh"));

            Assert.True(mesh < 0.2 * geometric, $"{name}: the mesh moves A4 by {mesh:E2} against {geometric:E2}");
        }
    }
}

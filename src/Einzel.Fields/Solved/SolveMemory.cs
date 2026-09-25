namespace Einzel.Fields.Solved;

/// <summary>What building a solved field holds in memory, known before anything is built.</summary>
/// <param name="Nodes">Nodes on the finest grid.</param>
/// <param name="Channels">Basis solves the geometry decomposes into; one for a static geometry.</param>
/// <param name="Galerkin">
/// Whether the coarse levels are formed by Galerkin coarsening. Always false for a plane solve,
/// which has only the rediscretized hierarchy.
/// </param>
/// <param name="PeakBytes">The most the build holds live at once, in bytes.</param>
/// <param name="RetainedBytes">What the finished field keeps once the build returns, in bytes.</param>
/// <remarks>
/// <para>
/// <b>The live set, not the process's working set.</b> What is counted is what the solve
/// holds <em>reachable</em> at its peak - the number that has to fit. The collector does not
/// free a large array the moment it becomes garbage, so on a machine with memory to spare the
/// process runs higher: 390 bytes a node on the Astral at a 4 mm cell against a live 242.
/// Under a hard heap limit the same solve completed at 279 MiB and ran out at 275, against a
/// live peak of 259, so the collector gives the garbage back when it has to, and the live set
/// plus a few per cent is the floor.
/// </para>
/// <para>
/// <b>Stated beside the allocations it describes</b>, in <see cref="SolveMemoryModel"/>, so a
/// new per-node array is added to the sum in the same file as the reason for it, and
/// <c>SolveMemoryTests</c> measures a real build against this number.
/// </para>
/// </remarks>
public sealed record SolveMemory(
    long Nodes, int Channels, bool Galerkin, double PeakBytes, double RetainedBytes);

/// <summary>
/// Bytes per finest-level node that each part of a solve holds, and what they sum to at the peak.
/// </summary>
/// <remarks>
/// <para>
/// <b>Measured, then written as arithmetic over the arrays.</b> The estimate this replaces
/// quoted 48 bytes a node for every solve - potential, right-hand side, residual and "the
/// correction and restriction buffers" - and a volume solve holds five times that. What it
/// left out is the geometry: a conductor mask, and six cut-link arms of two doubles a node,
/// which is 96 bytes before a single field is allocated. The Galerkin hierarchy then
/// assembles the fine operator as a stored stencil of eight doubles a node, and forms the
/// first coarse level from it with three scratch arrays over the fine grid.
/// </para>
/// <para>
/// Peak live set measured by sampling a forced full collection every few milliseconds
/// through a build, Release, on the shipped templates:
/// </para>
/// <list type="table">
/// <item><description>
/// volume, Galerkin: astral-3d at 1.12 M nodes, <b>241.8</b> bytes a node
/// </description></item>
/// <item><description>
/// volume, rediscretized: segmented-quadrupole at 1.09 M and 4.28 M nodes, <b>141.9</b> and
/// <b>141.8</b> - flat, so no fixed cost is hiding in the per-node figure
/// </description></item>
/// <item><description>
/// plane: quadrupole at 0.26 M and 1.05 M nodes, <b>143.4</b> and <b>149.7</b>; einzel-lens
/// at 1.05 M, <b>143.4</b>; tims-analyzer at 0.13 M, <b>143.6</b>
/// </description></item>
/// </list>
/// <para>
/// The arithmetic below reproduces the volume figures to 1 per cent and the plane ones to
/// 7, conservatively. It is the arithmetic that is used, rather than the measurements, so
/// that each term names the array responsible for it.
/// </para>
/// </remarks>
public static class SolveMemoryModel
{
    /// <summary>A volume conductor mask: a fixed flag and a held potential per node.</summary>
    public const double VolumeMask = sizeof(bool) + sizeof(double);

    /// <summary>Volume cut links: six arms, each a fraction and a surface potential.</summary>
    /// <remarks>
    /// The largest single term, and allocated in full whatever fraction of arms is cut - on
    /// the Astral at 4 mm, 244,664 cut arms among 6.7 million.
    /// </remarks>
    public const double VolumeCutLinks = 6 * 2 * sizeof(double);

    /// <summary>The potential, the right-hand side and the residual.</summary>
    public const double Fields = 3 * sizeof(double);

    /// <summary>The fine operator as a stored stencil: diagonal, six arms, and the known term.</summary>
    public const double VolumeStencil = (1 + 6 + 1) * sizeof(double);

    /// <summary>
    /// Forming a Galerkin level: a column and its image over the fine grid, and a stamp.
    /// </summary>
    public const double GalerkinForming = (2 * sizeof(double)) + sizeof(int);

    /// <summary>The first coarse Galerkin level: twenty-seven coefficients on an eighth of the nodes.</summary>
    public const double GalerkinFirstLevel = 27.0 * sizeof(double) / 8.0;

    /// <summary>
    /// A volume solve on the Galerkin hierarchy, at its peak: while the first coarse level is
    /// being formed from the fine stencil.
    /// </summary>
    /// <remarks>
    /// 240 bytes a node, against a measured 241.8. After the hierarchy is built the stencil and
    /// the scratch are garbage and the solve holds about 170.
    /// </remarks>
    public const double VolumeGalerkinPeak =
        VolumeMask + VolumeCutLinks + Fields + VolumeStencil + GalerkinForming + GalerkinFirstLevel;

    /// <summary>
    /// A volume solve on the rediscretized hierarchy, at its peak: the deepest point of a V-cycle.
    /// </summary>
    /// <remarks>
    /// The fine level's mask, cut links and fields, a fine residual, and on each coarse level a
    /// mask, a restricted residual, a correction and a residual of its own - 33 bytes a coarse
    /// node, summed over levels an eighth the size of the one above. 141.7 bytes a node, against
    /// a measured 141.9.
    /// </remarks>
    public const double VolumeRediscretizedPeak =
        VolumeMask + VolumeCutLinks + Fields + sizeof(double)
        + ((VolumeMask + (3 * sizeof(double))) / 7.0);

    /// <summary>A plane conductor mask.</summary>
    public const double PlaneMask = sizeof(bool) + sizeof(double);

    /// <summary>Plane cut links: four arms, each a fraction and a surface potential.</summary>
    public const double PlaneCutLinks = 4 * 2 * sizeof(double);

    /// <summary>
    /// A plane solve at its peak: the fine level, and a V-cycle's coarse levels while the first
    /// of them re-zeroes its cut links.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The fine level's mask, cut links, three fields and a residual; each coarse level's mask,
    /// cut links and three working fields, summed over levels a quarter the size of the one
    /// above; and one transient copy of the first coarse level's cut links, which a coarse
    /// level replaces because it solves for the error and the error is zero on a conductor.
    /// </para>
    /// <para>
    /// 153 bytes a node, against 143 to 150 measured, so it errs toward over.
    /// </para>
    /// </remarks>
    public const double PlanePeak =
        PlaneMask + PlaneCutLinks + Fields + sizeof(double)
        + ((PlaneMask + PlaneCutLinks + (3 * sizeof(double))) / 3.0)
        + (PlaneCutLinks / 4.0);

    /// <summary>What a finished field keeps per channel: its solved potential.</summary>
    /// <remarks>
    /// The interpolants reference the potential rather than tabulating coefficients, so a
    /// channel costs one double a node once it is built. Measured at 8.0 on every template.
    /// </remarks>
    public const double RetainedPerChannel = sizeof(double);

    /// <summary>What a build holds at its peak, given one channel's working set.</summary>
    /// <param name="nodes">Nodes on the finest grid.</param>
    /// <param name="channels">How many basis solves the build makes.</param>
    /// <param name="workingPerNode">One channel's working set, in bytes a node.</param>
    /// <param name="galerkin">Whether that working set is the Galerkin one.</param>
    /// <returns>The statement.</returns>
    /// <remarks>
    /// Channels are solved one at a time and each keeps only its potential, so the peak is one
    /// working set plus the potentials of every channel solved before it.
    /// </remarks>
    public static SolveMemory For(long nodes, int channels, double workingPerNode, bool galerkin)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(nodes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);

        return new SolveMemory(
            nodes,
            channels,
            galerkin,
            nodes * (workingPerNode + (RetainedPerChannel * (channels - 1))),
            nodes * RetainedPerChannel * channels);
    }
}

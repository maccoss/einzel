using System.Globalization;
using System.Text;

using Einzel.Core.Results;

namespace Einzel.Commands;

/// <summary>
/// A report as one self-contained page.
/// </summary>
/// <remarks>
/// <para>
/// <b>Self-contained because the two design documents already are.</b> Both are
/// hand-authored HTML with an inline <c>&lt;style&gt;</c> block over an IBM Plex palette,
/// and a report of the work belongs in the same family as the documents describing the
/// intent - a reader moving between them should not be moving between visual systems. The
/// palette and the type pairing here are lifted from
/// <c>einzel-software-spec-r06.html</c> rather than chosen afresh.
/// </para>
/// <para>
/// <b>One file, no assets.</b> A report is the artifact most likely to be sent to somebody
/// or opened months later, and one that needed a stylesheet beside it is one that arrives
/// broken. The fonts are asked for by link and every face has a real fallback stack, so the
/// page reads correctly with no network at all - which matters here because
/// <c>AGT-8</c> keeps the CLI itself off the network and a report that only rendered online
/// would be a network dependency by the back door.
/// </para>
/// <para>
/// <b>State is in the form as well as in the words</b>, because this page is scanned rather
/// than read: a run still standing, one the model has moved out from under, and one that
/// stored no answer are three different things and a reader should not have to parse a
/// sentence to tell them apart. A validity violation gets the hatched band
/// <c>Einzel.Render</c> stamps on a tainted figure, deliberately - that is already this
/// project's visual vocabulary for a result that may not be read without its caveat, and
/// GRD-3 makes it the one class that must never be skimmed.
/// </para>
/// <para>
/// <b>Nothing is computed here.</b> Every number arrives formatted, from
/// <see cref="ReportCommand"/>, so a second consumer of that command renders the same
/// figure the same way. A page that formatted its own would be a second opinion about how
/// many digits a quantity deserves.
/// </para>
/// </remarks>
public static class ReportPage
{
    /// <summary>Renders a report as a complete HTML document.</summary>
    /// <param name="report">What to render.</param>
    /// <param name="title">The heading, usually the project's own name.</param>
    /// <returns>The page.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="report"/> is null.</exception>
    public static string Write(ReportOutcome report, string title)
    {
        ArgumentNullException.ThrowIfNull(report);

        var page = new StringBuilder(16 * 1024);

        page.Append("<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n");
        page.Append("<meta charset=\"utf-8\">\n");
        page.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        page.Append(CultureInfo.InvariantCulture, $"<title>{Escape(title)} — run report</title>\n");
        page.Append("<link rel=\"preconnect\" href=\"https://fonts.googleapis.com\">\n");
        page.Append("<link rel=\"preconnect\" href=\"https://fonts.gstatic.com\" crossorigin>\n");
        page.Append("<link href=\"https://fonts.googleapis.com/css2?family=IBM+Plex+Mono:wght@400;500"
            + "&family=IBM+Plex+Sans:wght@400;500;600&family=IBM+Plex+Serif:wght@400&display=swap\" "
            + "rel=\"stylesheet\">\n");
        page.Append("<style>\n").Append(Style).Append("</style>\n</head>\n<body>\n");

        page.Append("<div class=\"sheet\">\n");

        Masthead(page, report, title);

        if (report.Warnings.Count > 0)
        {
            page.Append("<section>\n<h2>About this report</h2>\n");

            foreach (var warning in report.Warnings)
            {
                Warning(page, warning);
            }

            page.Append("</section>\n");
        }

        page.Append("<section>\n<h2>Runs <em>newest first</em></h2>\n");

        if (report.Runs.Count == 0)
        {
            page.Append("<p class=\"empty\">Nothing has been run in this project, or its "
                + "results have been discarded. Results are regenerable by design (PRJ-4), so "
                + "an empty <code>results/</code> is an ordinary state rather than a loss.</p>\n");
        }

        foreach (var run in report.Runs)
        {
            Run(page, run);
        }

        page.Append("</section>\n");

        if (report.Figures.Count > 0)
        {
            page.Append("<section>\n<h2>Figures <em>already rendered here</em></h2>\n");
            page.Append("<p class=\"note\">Listed rather than redrawn. A figure carries its own "
                + "provenance stamped on it (GRD-12), so re-rendering one here would be a second "
                + "caller of that pipeline with its own idea of what the figure should say.</p>\n");
            page.Append("<ul class=\"files\">\n");

            foreach (var figure in report.Figures)
            {
                page.Append(CultureInfo.InvariantCulture,
                    $"<li><a href=\"{Escape(figure)}\">{Escape(figure)}</a></li>\n");
            }

            page.Append("</ul>\n</section>\n");
        }

        Colophon(page, report);

        page.Append("</div>\n</body>\n</html>\n");

        return page.ToString();
    }

    private static void Masthead(StringBuilder page, ReportOutcome report, string title)
    {
        page.Append("<header class=\"masthead\">\n<div class=\"stamp\">");
        page.Append("<span>Einzel run report</span>");
        page.Append(CultureInfo.InvariantCulture,
            $"<span><b>{report.Runs.Count}</b> run(s)</span>");
        page.Append(CultureInfo.InvariantCulture,
            $"<span><b>{report.Current}</b> still current</span>");

        if (report.OutsideValidity > 0)
        {
            page.Append(CultureInfo.InvariantCulture,
                $"<span class=\"warn\"><b>{report.OutsideValidity}</b> outside validity</span>");
        }

        page.Append("</div>\n");
        page.Append(CultureInfo.InvariantCulture, $"<h1>{Escape(title)}</h1>\n");

        page.Append("<p class=\"lede\">What has been run in this project, what came out of it, "
            + "and which caveats came with it. This is a view over the results and manifests "
            + "already in <code>results/</code> — it records nothing of its own, so it "
            + "cannot disagree with what actually ran.</p>\n");

        page.Append("</header>\n");
    }

    private static void Run(StringBuilder page, ReportedRun run)
    {
        // The state a reader wants first, and there are six because they call for six
        // different things: nothing to do, re-run it, run it again to store an answer,
        // give it longer or a coarser mesh, read it another way, and report a defect.
        // Collapsing any pair would tell a reader to do the wrong one - a study whose
        // answer this page does not draw is not broken, a document this build cannot load
        // is not a project that has moved on, and a run that was interrupted after six
        // hours is not one that was never started.
        var (state, label) = run.Unreadable is not null ? ("broken", "unreadable")
            : run.NotRendered is not null ? ("study", "a study's answer")
            : run.Unfinished is not null ? ("unfinished", "interrupted")
            : run.Result is null ? ("noanswer", "no result stored")
            : !run.Current ? ("drifted", "superseded")
            : ("current", "current");

        page.Append(CultureInfo.InvariantCulture, $"<article class=\"run {state}\">\n");

        page.Append("<div class=\"runhead\">\n");
        page.Append(CultureInfo.InvariantCulture,
            $"<h3>{Escape(Slashes(run.Model ?? run.RecordedModel ?? run.Manifest))}</h3>\n");
        page.Append(CultureInfo.InvariantCulture,
            $"<span class=\"chip {state}\">{Escape(label)}</span>\n");
        page.Append("</div>\n");

        // The engineering title block: the provenance PRJ-3 says determines the run, laid
        // out as a drawing's is rather than as a paragraph, because it is read by looking
        // for one field rather than by reading across.
        page.Append("<div class=\"titleblock\">\n");
        Field(page, "when", When(run.CreatedUtc));
        Field(page, "transport", run.TransportMode);
        Field(page, "engine", run.EngineVersion);
        Field(page, "solver behaviour", run.SolverBehaviourVersion.ToString(CultureInfo.InvariantCulture));
        Field(page, "machine", run.Machine);
        Field(page, "model hash", Short(run.ModelHash));
        page.Append("</div>\n");

        if (run.Unreadable is { } unreadable)
        {
            page.Append(CultureInfo.InvariantCulture,
                $"<p class=\"broken\">{Escape(unreadable)}</p>\n</article>\n");
            return;
        }

        if (run.NotRendered is { } notRendered)
        {
            // A gap, said as one. The provenance above is still worth having: it names the
            // model and the engine that produced the answer sitting next to it.
            page.Append(CultureInfo.InvariantCulture,
                $"<p class=\"study\">{Escape(notRendered)}</p>\n</article>\n");
            return;
        }

        if (run.Outcome is { } outcome)
        {
            page.Append(CultureInfo.InvariantCulture,
                $"<p class=\"outcome\">Ended <code>{Escape(outcome)}</code>"
                + $"{(run.Completed == true ? ", and the engine finished what it was asked to do"
                    : ", and the engine did not finish")}.</p>\n");
        }

        if (run.Result is null && run.Unfinished is { } unfinished)
        {
            // A RUN THAT DID NOT FINISH IS A DIFFERENT STATEMENT from one that has no
            // answer stored, and only one of them is worth acting on the same way. Both
            // leave results/ without a result; this one ran for hours and was interrupted,
            // and the phases it got through are real measurements.
            page.Append(CultureInfo.InvariantCulture,
                $"<p class=\"unfinished\">{Escape(unfinished)}.</p>\n");
        }
        else if (run.Result is null)
        {
            page.Append("<p class=\"noanswer\">This run stored a manifest and no result "
                + "document, so its provenance is complete and its answer is nowhere. Nothing "
                + "here can say what came out of it; re-running the model stores one.</p>\n");
        }
        else if (run.Numbers.Count == 0)
        {
            page.Append("<p class=\"note\">This run produced no reportable quantity. For a "
                + "density that is ordinary — a diffusive result has no flight time and "
                + "says so rather than filling one in.</p>\n");
        }
        else
        {
            page.Append("<div class=\"scroll\">\n<table class=\"numbers\">\n");
            page.Append("<thead><tr><th>quantity</th><th class=\"num\">value</th>"
                + "<th>unit</th><th>interval</th><th>evidence</th></tr></thead>\n<tbody>\n");

            foreach (var number in run.Numbers)
            {
                page.Append("<tr>");
                page.Append(CultureInfo.InvariantCulture, $"<td>{Escape(number.Name)}</td>");
                page.Append(CultureInfo.InvariantCulture,
                    $"<td class=\"num\">{Escape(number.Value)}</td>");
                page.Append(CultureInfo.InvariantCulture,
                    $"<td class=\"unit\">{Escape(number.Unit)}</td>");

                // An absent interval is an em dash rather than a zero. A residual of zero
                // is not "no uncertainty", it is one smaller than a comparison of two
                // doubles can see, and the two must not print alike.
                page.Append(CultureInfo.InvariantCulture,
                    $"<td class=\"iv\">{(number.Interval is null ? "—" : Escape(number.Interval))}</td>");

                page.Append(CultureInfo.InvariantCulture,
                    $"<td class=\"ev\">{(number.Evidence is null ? "—" : Escape(number.Evidence))}</td>");
                page.Append("</tr>\n");
            }

            page.Append("</tbody>\n</table>\n</div>\n");
        }

        // THE TIMELINE, WHERE THERE IS ONE. A sequenced run's answer is how the packet
        // changed through the phases, and the width per phase is the whole subject of a
        // mobility-analyzer study: split a hold into phases and this table is a
        // relaxation curve. Below the scalars, because those describe the run and this
        // describes the course it took.
        if (run.Phases.Count > 0)
        {
            page.Append("<h4>The timeline it walked</h4>\n");
            page.Append("<div class=\"scroll\">\n<table class=\"numbers phases\">\n");
            page.Append("<thead><tr><th>phase</th><th>mode</th><th class=\"num\">ends at</th>"
                + "<th class=\"num\">population</th><th class=\"num\">trajectories</th>"
                + "<th class=\"num\">center</th><th class=\"num\">axial width</th>"
                + "<th class=\"num\">radial width</th></tr></thead>\n<tbody>\n");

            foreach (var phase in run.Phases)
            {
                page.Append("<tr>");

                // THE CONVERSION IS MARKED ON THE PHASE IT HAPPENED AT. SEQ-1's own
                // subject is that position is the one thing both descriptions carry, so
                // the widths either side of that mark are two measurements of one packet
                // by two machineries - and a reader comparing them has to know which
                // boundary they straddle.
                page.Append(CultureInfo.InvariantCulture,
                    $"<td>{Escape(phase.Name)}{(phase.Converted ? Converted : "")}</td>");

                page.Append(CultureInfo.InvariantCulture,
                    $"<td class=\"unit\">{Escape(phase.Mode)}</td>");

                page.Append(CultureInfo.InvariantCulture,
                    $"<td class=\"num\">{Escape(phase.EndsAtUs)}<span class=\"unit\"> us</span></td>");

                page.Append(CultureInfo.InvariantCulture,
                    $"<td class=\"num\">{Escape(phase.Population)}</td>");

                page.Append(CultureInfo.InvariantCulture,
                    $"<td class=\"num\">{(phase.Trajectories is { } carried
                        ? Escape(carried.ToString("N0", CultureInfo.InvariantCulture))
                        : "—")}</td>");

                Millimeters(page, phase.CentroidMm);
                Millimeters(page, phase.AxialSpreadMm);
                Millimeters(page, phase.RadialSpreadMm);

                page.Append("</tr>\n");
            }

            page.Append("</tbody>\n</table>\n</div>\n");
        }

        if (run.Drift.Count > 0)
        {
            page.Append("<h4>Why this is no longer the answer</h4>\n<ul class=\"drift\">\n");

            foreach (var reason in run.Drift)
            {
                page.Append(CultureInfo.InvariantCulture, $"<li>{Escape(reason)}</li>\n");
            }

            page.Append("</ul>\n");
        }

        if (run.Notes.Count > 0)
        {
            page.Append("<h4>True of it, and not invalidating</h4>\n<ul class=\"notes\">\n");

            foreach (var note in run.Notes)
            {
                page.Append(CultureInfo.InvariantCulture, $"<li>{Escape(note)}</li>\n");
            }

            page.Append("</ul>\n");
        }

        if (run.Warnings.Count > 0)
        {
            page.Append("<h4>What rode along</h4>\n");

            foreach (var warning in run.Warnings)
            {
                Warning(page, warning);
            }
        }

        if (run.Artifacts.Count > 0)
        {
            page.Append("<h4>What it wrote</h4>\n<ul class=\"files\">\n");

            foreach (var artifact in run.Artifacts)
            {
                var href = Slashes(artifact);

                page.Append(CultureInfo.InvariantCulture,
                    $"<li><a href=\"{Escape(href)}\">{Escape(href)}</a></li>\n");
            }

            page.Append("</ul>\n");
        }

        page.Append("</article>\n");
    }

    /// <summary>One warning, marked by which of the four severities it carries.</summary>
    /// <remarks>
    /// <para>
    /// <b>Four levels, because the enum has four and they mean different things.</b> The
    /// first version of this page keyed the hatched band on <c>IsSuppressible</c>, which
    /// is false for everything above advisory - so the band that exists to mark the one
    /// class GRD-3 says must never be skimmed appeared on a housekeeping note about a
    /// convergence floor, and on almost every warning there is. A mark on everything
    /// marks nothing, which is GRD-3's own argument met from the other direction.
    /// </para>
    /// <para>
    /// So the hatch - the same device <c>Einzel.Render</c> puts across a tainted figure -
    /// is for <see cref="WarningSeverity.ValidityViolation"/> alone: the result was
    /// computed outside the validity of the model used. Provenance takes the palette's own
    /// provenance colour, since GRD-5's point is that it travels with the artifact rather
    /// than that something went wrong; qualified takes amber; and advisory, the only
    /// silenceable severity, is left plain.
    /// </para>
    /// </remarks>
    /// <summary>The badge that marks the phase a packet changed description at.</summary>
    private const string Converted =
        " <span class=\"conv\" title=\"the packet was converted between transport "
        + "descriptions here\">converted</span>";

    /// <summary>
    /// One length, in millimeters, or an em dash where there was none to measure.
    /// </summary>
    /// <param name="page">The page being built.</param>
    /// <param name="value">The length, already formatted; null where there is none.</param>
    /// <remarks>
    /// <b>An absent width is an em dash rather than a zero</b>, for the same reason an
    /// absent interval is: a packet one cell across reports a width of nearly zero and
    /// that is a measurement, while a phase that ended with nothing left has no width at
    /// all. Printing both as "0.0000" would make the second look like the first.
    /// </remarks>
    private static void Millimeters(StringBuilder page, string? value)
        => page.Append(CultureInfo.InvariantCulture,
            $"<td class=\"num\">{(value is null
                ? "—"
                : Escape(value) + "<span class=\"unit\"> mm</span>")}</td>");

    private static void Warning(StringBuilder page, ValidityWarning warning)
    {
        var kind = warning.Severity switch
        {
            WarningSeverity.ValidityViolation => "violation",
            WarningSeverity.Provenance => "provenance",
            WarningSeverity.Qualified => "qualified",
            _ => "advisory",
        };

        page.Append(CultureInfo.InvariantCulture, $"<div class=\"warning {kind}\">\n");

        if (warning.Severity == WarningSeverity.ValidityViolation)
        {
            page.Append("<div class=\"hatch\" aria-hidden=\"true\"></div>\n");
        }

        page.Append(CultureInfo.InvariantCulture,
            $"<div class=\"wcode\">{Escape(Words(warning.Severity))}"
            + $"<code>{Escape(warning.Code)}</code></div>\n");

        page.Append(CultureInfo.InvariantCulture,
            $"<p>{Escape(warning.Message)}</p>\n</div>\n");
    }

    /// <summary>A severity said in words rather than in the enum's spelling.</summary>
    /// <remarks>
    /// <c>ValidityViolation</c> names the rule; "outside validity" names what happened to
    /// the run, which is what a reader is looking for. The code beside it stays exact.
    /// </remarks>
    private static string Words(WarningSeverity severity) => severity switch
    {
        WarningSeverity.ValidityViolation => "outside validity",
        WarningSeverity.Provenance => "provenance",
        WarningSeverity.Qualified => "qualified",
        WarningSeverity.Advisory => "advisory",

        // Named cases and a throw for the rest, so a fifth severity fails to render
        // rather than falling through to the mildest label there is.
        _ => throw new ArgumentOutOfRangeException(nameof(severity), severity, null),
    };

    private static void Colophon(StringBuilder page, ReportOutcome report)
    {
        page.Append("<footer>\n<div class=\"stamp\">");
        page.Append(CultureInfo.InvariantCulture,
            $"<span>project <b>{Escape(report.Root)}</b></span>");
        page.Append(CultureInfo.InvariantCulture,
            $"<span>engine <b>{Escape(Project.EngineBuild.Version)}</b></span>");
        page.Append(CultureInfo.InvariantCulture,
            $"<span>rendered <b>{DateTime.UtcNow.ToString("u", CultureInfo.InvariantCulture)}</b></span>");
        page.Append("</div>\n");

        // Said plainly, because a page listing runs invites being read as the record of
        // them. PRJ-4 puts the durable record in the model document and its history.
        page.Append("<p class=\"note\">Regenerate with <code>einzel report</code>. This page is "
            + "a view: the record of a design is the model document and its history, and "
            + "everything above was read from the manifests and results in "
            + "<code>results/</code> at the moment shown.</p>\n</footer>\n");
    }

    private static void Field(StringBuilder page, string name, string value)
        => page.Append(CultureInfo.InvariantCulture,
            $"<div><span>{Escape(name)}</span>{Escape(value)}</div>\n");

    /// <summary>The manifest's timestamp, to the minute, or as recorded if it will not parse.</summary>
    private static string When(string createdUtc)
        => DateTimeOffset.TryParse(
            createdUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var when)
            ? when.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + "Z"
            : createdUtc;

    /// <summary>
    /// A hash's first bytes, which is what a reader compares.
    /// </summary>
    /// <remarks>
    /// Shortened for reading and never for comparing - the whole hash is in the manifest,
    /// which is the file a check reads. A page is not the place a hash is verified from.
    /// </remarks>
    private static string Short(string hash)
    {
        var digits = hash.StartsWith("sha256:", StringComparison.Ordinal) ? hash[7..] : hash;

        return digits.Length <= 12 ? digits : digits[..12];
    }

    private static string Escape(string text) => System.Net.WebUtility.HtmlEncode(text);

    /// <summary>A path with forward slashes, whichever platform wrote it.</summary>
    /// <remarks>
    /// A page is read and linked from rather than walked, so a backslash here is a link a
    /// browser will not follow and a path that reads as one platform's. The stored
    /// documents keep whatever separator they were written with; this is presentation.
    /// </remarks>
    private static string Slashes(string path) => path.Replace('\\', '/');

    private const string Style = """
        :root{
          --paper:#F7F7F4; --ink:#12161A; --ink2:#525C64; --ink3:#7E888F;
          --rule:#C9CFD4; --hair:#DDE2E5; --panel:#FFFFFF;
          --blue:#1D4E89; --verd:#0B7A6B; --amber:#9C6A0B; --plum:#5B4B8A; --rust:#A0522D;
          --tint:#E7EEF3; --tint2:#E4F0ED; --tint3:#F6EEDD;
        }
        @media (prefers-color-scheme: dark){
          :root:not([data-theme="light"]){
            --paper:#101315; --ink:#E7EAEC; --ink2:#A3ADB4; --ink3:#7E888F;
            --rule:#333B41; --hair:#252C31; --panel:#171B1E;
            --blue:#7EA8D8; --verd:#5FBFAE; --amber:#D7A44A; --plum:#A798D0; --rust:#D08A5A;
            --tint:#1B2530; --tint2:#172624; --tint3:#2A2318;
          }
        }
        :root[data-theme="dark"]{
          --paper:#101315; --ink:#E7EAEC; --ink2:#A3ADB4; --ink3:#7E888F;
          --rule:#333B41; --hair:#252C31; --panel:#171B1E;
          --blue:#7EA8D8; --verd:#5FBFAE; --amber:#D7A44A; --plum:#A798D0; --rust:#D08A5A;
          --tint:#1B2530; --tint2:#172624; --tint3:#2A2318;
        }
        *{box-sizing:border-box}
        html{-webkit-text-size-adjust:100%}
        body{
          margin:0; background:var(--paper); color:var(--ink);
          font-family:"IBM Plex Sans",-apple-system,BlinkMacSystemFont,"Segoe UI",sans-serif;
          font-size:16px; line-height:1.6; letter-spacing:.002em;
        }
        .sheet{max-width:1080px;margin:0 auto;padding:0 28px 96px}
        .masthead{border-bottom:2px solid var(--ink);padding:44px 0 26px}
        .stamp{
          font-family:"IBM Plex Mono",ui-monospace,monospace;font-size:11px;
          letter-spacing:.14em;text-transform:uppercase;color:var(--ink3);
          display:flex;flex-wrap:wrap;gap:22px;margin-bottom:24px;
        }
        .stamp b{color:var(--ink2);font-weight:500}
        .stamp .warn b{color:var(--amber)}
        h1{
          font-weight:600;font-size:clamp(27px,4.4vw,40px);line-height:1.1;
          letter-spacing:-.022em;margin:0 0 14px;max-width:26ch;text-wrap:balance;
        }
        .lede{
          font-family:"IBM Plex Serif",Georgia,serif;font-size:18px;line-height:1.55;
          color:var(--ink2);margin:0;max-width:64ch;
        }
        section{margin-top:52px}
        h2{
          font-size:12.5px;font-family:"IBM Plex Mono",ui-monospace,monospace;font-weight:500;
          letter-spacing:.15em;text-transform:uppercase;color:var(--blue);
          margin:0 0 20px;padding-bottom:8px;border-bottom:1px solid var(--rule);
          display:flex;justify-content:space-between;align-items:baseline;gap:16px;
        }
        h2 em{font-style:normal;color:var(--ink3);font-size:11px;letter-spacing:.1em}
        h4{
          font-size:11px;font-family:"IBM Plex Mono",ui-monospace,monospace;font-weight:500;
          letter-spacing:.13em;text-transform:uppercase;color:var(--ink3);
          margin:26px 0 10px;
        }
        p{margin:0 0 14px;max-width:70ch}
        code{
          font-family:"IBM Plex Mono",ui-monospace,monospace;font-size:.88em;
          background:var(--hair);padding:1px 5px;border-radius:2px;
        }
        a{color:var(--blue)}
        .note,.empty{color:var(--ink2);font-size:14.5px}

        /* A run. The stripe is the state, so it reads before any word does. */
        .run{
          margin:0 0 34px;padding:20px 22px 22px;background:var(--panel);
          border:1px solid var(--hair);border-left:3px solid var(--rule);
        }
        .run.current{border-left-color:var(--verd)}
        .run.drifted{border-left-color:var(--amber)}
        .run.noanswer{border-left-color:var(--plum)}
        .run.unfinished{border-left-color:var(--rust)}
        .run.study{border-left-color:var(--blue)}
        .run.broken{border-left-color:#8A2B2B}
        .runhead{
          display:flex;justify-content:space-between;align-items:baseline;
          gap:16px;flex-wrap:wrap;margin-bottom:14px;
        }
        .runhead h3{
          font-size:18px;font-weight:600;letter-spacing:-.012em;margin:0;
          font-family:"IBM Plex Mono",ui-monospace,monospace;
        }
        .chip{
          font-family:"IBM Plex Mono",ui-monospace,monospace;font-size:10px;
          letter-spacing:.13em;text-transform:uppercase;padding:3px 9px;
          border:1px solid currentColor;border-radius:2px;white-space:nowrap;
        }
        .chip.current{color:var(--verd)}
        .chip.drifted{color:var(--amber)}
        .chip.noanswer{color:var(--plum)}
        .chip.unfinished{color:var(--rust)}
        .chip.study{color:var(--blue)}
        .chip.broken{color:#8A2B2B}

        .titleblock{
          display:grid;grid-template-columns:repeat(auto-fit,minmax(148px,1fr));
          border-top:1px solid var(--rule);border-bottom:1px solid var(--rule);
          font-family:"IBM Plex Mono",ui-monospace,monospace;font-size:11.5px;
          color:var(--ink2);
        }
        .titleblock div{padding:9px 12px 10px;border-right:1px solid var(--hair)}
        .titleblock div:last-child{border-right:none}
        .titleblock span{
          display:block;color:var(--ink3);font-size:9.5px;letter-spacing:.13em;
          text-transform:uppercase;margin-bottom:3px;
        }
        .outcome{margin:16px 0 0;font-size:14.5px;color:var(--ink2)}
        p.noanswer{margin:16px 0 0;font-size:14.5px;color:var(--plum);max-width:70ch}
        p.study{margin:16px 0 0;font-size:14.5px;color:var(--ink2);max-width:70ch}
        p.broken{margin:16px 0 0;font-size:14.5px;color:#8A2B2B;max-width:70ch}

        /* Wide content scrolls in its own box; the page body never does. */
        .scroll{overflow-x:auto;margin:18px 0 0}
        table.numbers{
          border-collapse:collapse;width:100%;font-size:14px;
          font-variant-numeric:tabular-nums;
        }
        table.numbers th{
          text-align:left;font-family:"IBM Plex Mono",ui-monospace,monospace;
          font-size:9.5px;font-weight:500;letter-spacing:.13em;text-transform:uppercase;
          color:var(--ink3);padding:0 14px 7px 0;border-bottom:1px solid var(--rule);
          white-space:nowrap;
        }
        table.numbers td{
          padding:7px 14px 7px 0;border-bottom:1px solid var(--hair);vertical-align:baseline;
        }
        table.numbers tr:last-child td{border-bottom:none}
        table.numbers .num{
          font-family:"IBM Plex Mono",ui-monospace,monospace;text-align:right;
          white-space:nowrap;font-weight:500;
        }
        table.numbers .unit,table.numbers .iv,table.numbers .ev{
          font-family:"IBM Plex Mono",ui-monospace,monospace;font-size:12px;color:var(--ink2);
        }
        table.numbers .ev{color:var(--ink3)}
        p.unfinished{
          margin:16px 0 0;font-size:14.5px;color:var(--rust);max-width:70ch}
        table.phases td:first-child{white-space:nowrap}
        table.phases .conv{
          font-family:"IBM Plex Mono",ui-monospace,monospace;font-size:9.5px;
          font-weight:500;letter-spacing:.1em;text-transform:uppercase;
          color:var(--ink3);border:1px solid var(--rule);border-radius:2px;
          padding:2px 4px;margin-left:7px}

        ul.drift,ul.notes,ul.files{margin:0;padding-left:20px;font-size:14.5px;color:var(--ink2)}
        ul.drift li,ul.notes li{margin-bottom:6px;max-width:74ch}
        ul.files{list-style:none;padding-left:0;font-family:"IBM Plex Mono",ui-monospace,monospace;font-size:12.5px}
        ul.files li{margin-bottom:4px}

        .warning{
          margin:0 0 12px;padding:11px 14px;background:var(--tint);
          border-left:2px solid var(--blue);
        }
        .warning p{margin:0;font-size:14px;color:var(--ink2);max-width:76ch}
        .warning.qualified{background:var(--tint3);border-left-color:var(--amber)}
        .warning.provenance{background:var(--tint2);border-left-color:var(--plum)}
        .warning.violation{background:var(--tint3);border-left-color:var(--amber)}
        .wcode{
          font-family:"IBM Plex Mono",ui-monospace,monospace;font-size:9.5px;
          letter-spacing:.13em;text-transform:uppercase;color:var(--ink3);
          margin-bottom:5px;display:flex;gap:10px;align-items:baseline;flex-wrap:wrap;
        }
        .wcode code{
          letter-spacing:0;text-transform:none;font-size:11.5px;color:var(--ink2);
          background:transparent;padding:0;
        }
        .warning.qualified .wcode{color:var(--amber)}
        .warning.provenance .wcode{color:var(--plum)}
        .warning.violation .wcode{color:var(--amber)}
        /* The same hatched rule Einzel.Render puts across a tainted figure. */
        .hatch{
          height:5px;margin:-11px -14px 9px;
          background:repeating-linear-gradient(
            135deg,var(--amber) 0 2px,transparent 2px 6px);
        }

        footer{margin-top:64px;padding-top:22px;border-top:1px solid var(--rule)}
        footer .stamp{margin-bottom:14px}

        @media (max-width:640px){
          .sheet{padding:0 18px 64px}
          .titleblock{grid-template-columns:1fr 1fr}
          .runhead{align-items:flex-start}
        }

        """;
}

using System.Text.RegularExpressions;

using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// The summary table at the top of <c>SPEC.md</c> counts the register below it, and it is
/// counted by hand.
/// </summary>
/// <remarks>
/// <para>
/// It had drifted to 64 / 14 / 37 / 3 against an actual 78 / 14 / 22 / 4, and described the
/// shell and MCP as "whole assemblies that do not exist" long after both were built. Six
/// individual rows had drifted with it, one of them registered wrong in both its requirement
/// and its evidence column - so it read Not built while the thing it asks for had shipped.
/// </para>
/// <para>
/// <c>CLAUDE.md</c>'s rule is that a status page which has drifted is worse than none,
/// because it is trusted, and the same argument makes the platform layer of
/// <c>AGENTS.md</c> generated rather than hand-written. This table is not generated, so the
/// next best thing is that it cannot disagree with what it counts.
/// </para>
/// </remarks>
public sealed partial class SpecRegisterTests(ITestOutputHelper output)
{
    /// <summary>Every requirement row: the tag and the status column, in file order.</summary>
    private static List<(string Tag, string Status)> Register(string spec)
    {
        var rows = new List<(string Tag, string Status)>();

        foreach (var line in File.ReadLines(spec))
        {
            if (!line.StartsWith("| `", StringComparison.Ordinal))
            {
                continue;
            }

            var cells = line.Trim().Trim('|').Split('|');
            if (cells.Length < 3)
            {
                continue;
            }

            var tag = cells[0].Trim().Trim('`');
            if (!TagPattern().IsMatch(tag))
            {
                continue;
            }

            rows.Add((tag, cells[2].Replace("*", string.Empty, StringComparison.Ordinal).Trim()));
        }

        return rows;
    }

    /// <summary>Which of the four buckets a status column falls into.</summary>
    /// <remarks>
    /// The register uses qualified statuses - "Met, with a stated floor", "Met, for the
    /// artifact that exists", "Partly met", "Not built, and deferred indefinitely" - because
    /// a bare word would overstate several of them. They still have to be counted as one of
    /// the four the table has columns for, and where they are counted is a judgement the
    /// register makes: "Met, ..." counts as met, "Partly met" as partial.
    /// </remarks>
    private static string Bucket(string status)
    {
        if (status.StartsWith("Partly", StringComparison.Ordinal)
            || status.StartsWith("Partial", StringComparison.Ordinal))
        {
            return "Partial";
        }

        if (status.StartsWith("Met", StringComparison.Ordinal))
        {
            return "Met";
        }

        if (status.StartsWith("Unverified", StringComparison.Ordinal))
        {
            return "Unverified";
        }

        return status.StartsWith("Not built", StringComparison.Ordinal) ? "Not built" : status;
    }

    /// <summary>The summary table agrees with the register it summarises.</summary>
    [Fact]
    public void TheSummaryTableCountsTheRegister()
    {
        var spec = SpecPath();
        var rows = Register(spec);

        var counted = rows.GroupBy(r => Bucket(r.Status))
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        var text = File.ReadAllText(spec);
        var stated = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var (label, key) in new[]
        {
            ("**Met**, with evidence", "Met"),
            ("**Partial**, with a stated gap", "Partial"),
            ("**Not built**", "Not built"),
            ("**Unverified**", "Unverified"),
        })
        {
            var match = Regex.Match(
                text,
                @"^\| " + Regex.Escape(label) + @"[^|]*\| (\d+) \|\s*$",
                RegexOptions.Multiline);

            Assert.True(match.Success, $"the summary table has no row for {label}");
            stated[key] = int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        }

        foreach (var key in stated.Keys.Order(StringComparer.Ordinal))
        {
            counted.TryGetValue(key, out var actual);
            output.WriteLine($"{key,-12} table {stated[key],4}   register {actual,4}");
        }

        foreach (var key in stated.Keys.Order(StringComparer.Ordinal))
        {
            counted.TryGetValue(key, out var actual);
            Assert.True(
                stated[key] == actual,
                $"SPEC.md's summary table says {stated[key]} {key}, and the register below it "
                + $"has {actual}. Recount the table, or fix the row that changed.");
        }

        // The four buckets must also account for every row, or a status spelled some fifth
        // way would be silently uncounted by both.
        var total = counted.Values.Sum();
        Assert.True(
            total == rows.Count,
            $"{rows.Count - total} rows fall outside the four buckets: "
            + string.Join(", ", rows.Select(r => Bucket(r.Status)).Distinct(StringComparer.Ordinal)
                .Where(b => b is not ("Met" or "Partial" or "Not built" or "Unverified"))));
    }

    /// <summary>
    /// The stated total is the number of tags, and every tag appears exactly once.
    /// </summary>
    /// <remarks>
    /// A duplicated tag would be counted twice and read as two requirements, and a register
    /// that had quietly lost one would still add up.
    /// </remarks>
    [Fact]
    public void EveryTagAppearsOnceAndTheTotalIsTheirCount()
    {
        var spec = SpecPath();
        var rows = Register(spec);

        Assert.NotEmpty(rows);

        var duplicated = rows.GroupBy(r => r.Tag, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.True(duplicated.Count == 0, "duplicated tags: " + string.Join(", ", duplicated));

        var match = Regex.Match(
            File.ReadAllText(spec), @"^\| Total tagged in r06 \| (\d+) \|\s*$",
            RegexOptions.Multiline);

        Assert.True(match.Success, "the summary table has no total row");

        var stated = int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        output.WriteLine($"total: table {stated}, register {rows.Count}");

        Assert.Equal(stated, rows.Count);
    }

    private static string SpecPath()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "SPEC.md");

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        // Not Assert.Fail: a test that silently passes when it cannot find the file it
        // checks is the vacuous truth this repository has been caught by four times.
        throw new FileNotFoundException("SPEC.md is not above the test binary");
    }

    [GeneratedRegex("^[A-Z]+-[0-9]+$")]
    private static partial Regex TagPattern();
}

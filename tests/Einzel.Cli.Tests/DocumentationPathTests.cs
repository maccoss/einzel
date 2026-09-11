using System.Text.RegularExpressions;

using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// A path this repository's documentation names has to resolve from the repository root.
/// </summary>
/// <remarks>
/// <para>
/// <b>Raised by review on `docs/extending.md`</b>, which named six source files without the
/// <c>src/</c> prefix - so a reader following them from the repository root got dead ends, on
/// the one page whose entire job is telling somebody where to put a change. Every other
/// prefixed path in the documentation resolved, which is what made this worth a test rather
/// than a correction: the convention was already right everywhere else and the new page was
/// the outlier.
/// </para>
/// <para>
/// <b>Deliberately narrow.</b> It matches only tokens naming an assembly directory -
/// <c>Einzel.Something/...</c> - because the documentation is full of paths that are correctly
/// relative to a *project* rather than to this repository (<c>models/reflectron.json</c>,
/// <c>results/</c>, <c>.einzel/</c>), and <c>papers/</c> is gitignored so it is absent in CI. A
/// matcher wide enough to catch those would fail for reasons that are not defects, which is
/// worse than not checking.
/// </para>
/// <para>
/// It asserts the prefix as well as the existence, because an unprefixed path that happens to
/// resolve under <c>src/</c> is exactly the defect: it reads as a repository path and is not
/// one.
/// </para>
/// </remarks>
public sealed partial class DocumentationPathTests(ITestOutputHelper output)
{
    [Fact]
    public void EveryAssemblyPathTheDocumentationNamesResolves()
    {
        var root = RepositoryRoot();

        var pages = new List<string> { "CLAUDE.md", "SPEC.md" };

        pages.AddRange(
            Directory.EnumerateFiles(Path.Combine(root, "docs"), "*.md")
                .Select(p => Path.Combine("docs", Path.GetFileName(p)))
                .OrderBy(p => p, StringComparer.Ordinal));

        var checked_ = 0;
        var wrong = new List<string>();

        foreach (var page in pages)
        {
            var text = File.ReadAllText(Path.Combine(root, page));
            var found = 0;

            foreach (var match in AssemblyPathPattern().Matches(text).Cast<Match>())
            {
                var named = match.Groups[1].Value;

                found++;
                checked_++;

                if (!named.StartsWith("src/", StringComparison.Ordinal))
                {
                    wrong.Add($"{page}: '{named}' has no src/ prefix, so it does not resolve "
                        + "from the repository root");
                    continue;
                }

                if (!File.Exists(Path.Combine(root, named.Replace('/', Path.DirectorySeparatorChar))))
                {
                    wrong.Add($"{page}: '{named}' does not exist");
                }
            }

            if (found > 0)
            {
                output.WriteLine($"{page,-32} {found} assembly path(s)");
            }
        }

        // The control. A matcher that had stopped matching would report no failures over no
        // paths, which is the vacuous truth this repository has been caught by four times.
        Assert.True(
            checked_ >= 10,
            $"only {checked_} assembly paths were found across {pages.Count} pages, so this "
            + "test is passing over almost nothing - the matcher is the thing to look at");

        output.WriteLine($"{checked_} paths checked, {wrong.Count} wrong");

        Assert.Empty(wrong);
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SPEC.md")))
            {
                return directory.FullName;
            }
        }

        // Not Assert.Fail, for the reason SpecRegisterTests gives: a test that silently passes
        // when it cannot find what it checks is worse than one that is absent.
        throw new FileNotFoundException("SPEC.md is not above the test binary");
    }

    /// <summary>A backticked path naming a file inside an assembly directory.</summary>
    /// <remarks>
    /// The <c>src/</c> is inside the capture rather than required by the pattern, so an
    /// unprefixed path is matched and then reported rather than being quietly skipped.
    /// </remarks>
    [GeneratedRegex(@"`((?:src/)?Einzel\.[A-Za-z0-9.]+/[A-Za-z0-9._/-]+\.(?:cs|json|py|html))`")]
    private static partial Regex AssemblyPathPattern();
}

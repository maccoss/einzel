using System.Reflection;

using Einzel.Commands;

using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// The two rules that keep the shell cheap to have (invariant 1, UI-1).
/// </summary>
/// <remarks>
/// <para>
/// <b>These run on the Linux CI runner</b>, which is the point: an invariant only ever
/// checked on a developer's Windows box is one that has already been broken by the time
/// anyone notices. This test project does not reference <c>Einzel.Wpf</c> and must never
/// do so — what it checks is that nothing else does either.
/// </para>
/// <para>
/// Windows-only applies to the shell and to nothing else, and that is the misreading to
/// guard against: "the GUI is Windows-only" and "the project is Windows-only" are one
/// word apart, and the second would undo the Linux CI that keeps the first one cheap.
/// </para>
/// </remarks>
public sealed class ShellBoundaryTests(ITestOutputHelper output)
{
    /// <summary>Assemblies that must be present, so the check cannot pass vacuously.</summary>
    /// <remarks>
    /// The scan below is over whatever is actually beside the test assembly, which is the
    /// honest thing to check - a transitive reference through a third assembly would not
    /// appear in any csproj and is exactly as much of a violation. But a scan that found
    /// nothing would pass, which is the vacuous truth this project has found four times
    /// (`einzel test` passing with no tests, `einzel solve` converging over no elements),
    /// so these must be among what it found.
    /// </remarks>
    private static readonly string[] MustBePresent =
    [
        "Einzel.Core",
        "Einzel.Fields",
        "Einzel.Transport",
        "Einzel.Render",
        "Einzel.Commands",
        "Einzel.Io",
    ];

    /// <summary>Nothing below the shell may reference it (invariant 1).</summary>
    /// <remarks>
    /// <para>
    /// The invariant that pays for itself. Every assembly above <c>Einzel.Wpf</c> builds
    /// and runs on Linux, which is what makes a later cross-platform shell a replacement
    /// of a presentation layer rather than a rewrite - and what lets <c>Einzel.Render</c>
    /// produce a publication figure headlessly in CI with no display attached.
    /// </para>
    /// <para>
    /// Checked against what each assembly actually references rather than against project
    /// files, because a transitive reference is exactly as much of a violation and does
    /// not appear in a csproj.
    /// </para>
    /// </remarks>
    [Fact]
    public void NothingBelowTheShellReferencesIt()
    {
        var scanned = new List<string>();

        foreach (var path in Directory.EnumerateFiles(AppContext.BaseDirectory, "Einzel.*.dll")
            .Concat(Directory.EnumerateFiles(AppContext.BaseDirectory, "einzel.dll")))
        {
            var name = Path.GetFileNameWithoutExtension(path);

            if (name.EndsWith(".Tests", StringComparison.Ordinal))
            {
                continue;
            }

            AssemblyName[] references;

            try
            {
                references = Assembly.LoadFrom(path).GetReferencedAssemblies();
            }
            catch (BadImageFormatException)
            {
                continue;
            }

            scanned.Add(name);

            var offending = references
                .Where(r => r.Name is not null
                    && r.Name.StartsWith("Einzel.Wpf", StringComparison.Ordinal))
                .Select(r => r.Name!)
                .ToArray();

            Assert.True(
                offending.Length == 0,
                $"{name} references {string.Join(", ", offending)}, which breaks invariant 1: "
                + "nothing below the shell may reference it, and every assembly above it "
                + "must build and run on Linux");
        }

        output.WriteLine($"scanned {scanned.Count}: {string.Join(", ", scanned.Order(StringComparer.Ordinal))}");

        // A scan that found nothing would have passed every assertion above.
        foreach (var required in MustBePresent)
        {
            Assert.Contains(required, scanned);
        }
    }

    /// <summary>
    /// The shell drives command objects, and holds no physics of its own (UI-1).
    /// </summary>
    /// <remarks>
    /// <para>
    /// UI-1 says the shell owns layout, input, the interactive viewport and the update
    /// check, and owns no physics, no validation rules, no file format knowledge and no
    /// render output. A window that referenced the engine directly could grow its own
    /// idea of what a model means, and the two would part company.
    /// </para>
    /// <para>
    /// So what it may reference is the command layer, which is the seam figure 3 draws:
    /// the CLI, the MCP server and the shell are peers driving the same command objects.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheShellReachesThePlatformThroughTheCommandLayer()
    {
        // Found by path rather than by name: this test project deliberately does not
        // reference the shell - that is the invariant above - so there is no assembly
        // identity to resolve and nothing copies it here.
        var path = Shell();

        if (!OperatingSystem.IsWindows())
        {
            // The shell is Windows-only, so on the Linux runner there is nothing to
            // check and saying so is the honest outcome. Invariant 1's check above is
            // the one that matters there, and it does not need this.
            output.WriteLine("not Windows; the shell cannot be built here");

            return;
        }

        // On Windows it must be there. A skip because the file was not found would be a
        // test that passes by not looking - the same vacuous truth the check above
        // guards against, and it would hide exactly the case where the shell had grown
        // a reference it should not have.
        Assert.True(
            path is not null,
            "the shell was not found beside the tests or in its own build output, so "
            + "UI-1 went unchecked on the one platform that can check it");

        var shell = Assembly.LoadFrom(path!);

        var used = shell.GetReferencedAssemblies()
            .Select(r => r.Name)
            .Where(n => n is not null && n.StartsWith("Einzel", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        output.WriteLine($"uses:     {string.Join(", ", used)}");

        Assert.Contains("Einzel.Commands", used);

        // What the project *declares*, which is the check that matters and is not the
        // same one. `GetReferencedAssemblies` reports what the compiler emitted - what
        // the code actually uses - so a ProjectReference to the whole transport engine
        // that nothing has called yet leaves no trace in the metadata at all. It passed
        // this test when it was written against emitted references only.
        //
        // Declaration is the right thing to check for UI-1: once the reference is there,
        // using it is one keystroke away, and the rule is about what the shell may reach
        // for rather than what it happens to have reached for so far.
        var declared = Declared(path!);

        output.WriteLine($"declares: {string.Join(", ", declared)}");

        Assert.Contains("Einzel.Commands", declared);

        // Not the solvers, not the integrator, not the renderer. Those are reached
        // through the command layer or not at all.
        foreach (var forbidden in new[]
        {
            "Einzel.Fields", "Einzel.Transport", "Einzel.Render", "Einzel.Analysis",
            "Einzel.Sweeps", "Einzel.Library", "Einzel.Io",
        })
        {
            Assert.DoesNotContain(forbidden, declared);
            Assert.DoesNotContain(forbidden, used);
        }
    }

    /// <summary>What the shell's project declares a reference to.</summary>
    /// <remarks>
    /// Read from the csproj beside the built assembly rather than from metadata, because
    /// a declared reference that nothing has used yet is invisible to reflection and is
    /// exactly the state UI-1 needs to catch: the rule is about what the shell may reach
    /// for, not what it has reached for so far.
    /// </remarks>
    private static string[] Declared(string assemblyPath)
    {
        for (var directory = new DirectoryInfo(Path.GetDirectoryName(assemblyPath)!);
            directory is not null;
            directory = directory.Parent)
        {
            var project = Path.Combine(directory.FullName, "Einzel.Wpf.csproj");

            if (!File.Exists(project))
            {
                continue;
            }

            return [.. System.Text.RegularExpressions.Regex
                .Matches(File.ReadAllText(project), @"ProjectReference\s+Include=""[^""]*?([A-Za-z.]+)\.csproj""")
                .Select(m => m.Groups[1].Value)
                .Order(StringComparer.Ordinal)];
        }

        return [];
    }

    /// <summary>Where the shell's assembly is, beside the tests or in its own output.</summary>
    /// <remarks>
    /// Nothing copies it here, because nothing may reference it. Walking to its build
    /// output is the price of the invariant being real.
    /// </remarks>
    private static string? Shell()
    {
        var beside = Path.Combine(AppContext.BaseDirectory, "einzel-shell.dll");

        if (File.Exists(beside))
        {
            return beside;
        }

        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent)
        {
            var candidate = Path.Combine(
                directory.FullName, "src", "Einzel.Wpf", "bin");

            if (Directory.Exists(candidate))
            {
                return Directory
                    .EnumerateFiles(candidate, "einzel-shell.dll", SearchOption.AllDirectories)
                    .FirstOrDefault();
            }
        }

        return null;
    }
}

# Release notes

One file per release, plus the rolling draft for the next one.

## Versioning

Einzel uses a `YY.feature.patch` convention:

| | |
| --- | --- |
| **YY** | two-digit year — `26` for 2026 |
| **feature** | incremented for each release carrying new capability |
| **patch** | incremented for a fix-only release within the same feature version |

`26.1.0` is the first feature release of 2026, `26.1.1` a fix on top of it, `26.2.0` the
second feature release. **The version is bumped at release time, not during development**,
so the working tree carries the version last released and the commit hash distinguishes
builds within it.

**Why a year rather than a semantic major.** A semantic major number promises something
about compatibility, and the thing this project must not silently break is the *model
format*, which carries its own `schemaVersion` and its own compatibility rule: every
version `SupportedVersions` claims to read is asserted to read, by a test. Overloading a
second compatibility promise onto the package version would put the same claim in two
places, where the two would eventually disagree. The year says when, the format says what
it can read.

**What the version is not.** `solverBehaviourVersion` is separate and deliberately so
(PRJ-3): it changes when the numbers a run produces would change, which is a different
event from a release. `einzel verify` distinguishes them — an edited model or a changed
solver-behaviour version invalidates a stored result, while a different engine build with
identical numerics does not.

## Where the version lives

1. **`Directory.Build.props`** — `<VersionPrefix>`, the single source. Every assembly
   takes it, and `EngineBuild.Version` reads it back through
   `AssemblyInformationalVersionAttribute`, so the CLI, the manifests and
   `einzel doctor` cannot disagree with the build.
2. **The git tag** — `v26.1.0`. The release workflow takes the version from the tag with
   the leading `v` stripped, and refuses a tag that is not `vYY.feature.patch`.
3. **This directory** — one notes file per released version.

Prose that quotes a version — sample CLI output in `README.md` and `docs/cli.md`, the
`engineMinimum` example in `docs/extensions.md` — needs it too. Those are examples rather
than sources, and a mechanical bump leaves them contradicting the line above if they are
missed.

## Files

```text
release-notes/
  README.md                       this file
  RELEASE_NOTES_next.md           the working draft, renamed at release time
  RELEASE_NOTES_v26.1.0.md        one per released version
```

During development, append to `RELEASE_NOTES_next.md`. It stays unversioned until the
release is cut, so a planned patch can become a feature release when new capability lands
without a file having to be renamed twice.

## What the assets are, and what they are not

The builds are **self-contained**: they carry their own .NET runtime, so nothing has to be
installed before they run. That costs about 75 MB of download and takes cold start from
73-147 ms to 220-250 ms, against PERF-8's 500 ms budget.

The reason is that somebody who wants to model an ion optic should not first have to install
a .NET SDK. r06's DST-1 gives a different reason - a per-user installer for a locked-down
instrument PC - and that use case is speculative; the one above is not, and it is the one
this decision rests on. The **installer and the updater are consequently deferred**: the
portable path needs neither, and fourteen of the specification's twenty-two unbuilt
requirements are the two of them.

## Cutting a release

1. Fold `RELEASE_NOTES_next.md` into `RELEASE_NOTES_v<version>.md` and start a fresh draft.
2. Set `<VersionPrefix>` in `Directory.Build.props` to the same version.
3. Update the sample output listed above if the version appears in it.
4. Commit, then tag `v<version>` and push the tag.

The workflow builds from the tag on a clean checkout (DST-5), runs the suite on both
platforms, smoke-tests the published binary by running it, checksums the assets in one
place (DST-4), and creates the release **as a draft**. Publishing it is a separate act by
somebody who has looked at what came out.

// These tests drive Program.Main, which writes to Console - a process-global
// resource. Capturing it means redirecting Console.Out and Console.Error for the
// duration of a call, and two test classes doing that at once interleave: one
// test's assertions run against output another test produced, which shows up as
// a JSON parse failure in whichever lost the race.
//
// It presented exactly that way: every test passing alone and one failing in the
// full run. Serialising the assembly is the honest fix, because the contention is
// real rather than an artefact of the tests. The whole file runs in about a
// second, so there is nothing to buy back.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

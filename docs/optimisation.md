# Sweeps and optimisation

`Einzel.Sweeps` holds four things that all take the same shape: a model, a set
of named parameters to vary, and a function from a validated model to a number.
Nothing in it knows what a mirror or a quadrupole is.

- **Tolerance Monte Carlo** (`ToleranceStudy`) — stochastic draws for the
  distribution of achieved performance, and one-at-a-time attribution for which
  tolerance binds first.
- **Parameter scan** (`ParameterScan`) — one parameter across a declared range on a
  grid, one row per point. What a curve is made of.
- **Sensitivity fields** (`SensitivityFields`) — FLD-1's cached derivative of
  potential with respect to each channel, so a perturbed geometry is a weighted
  sum rather than a solve.
- **Optimisation** (`Optimiser`) — Nelder–Mead and CMA-ES over the declared
  parameter surface.

This page is about the last, with the scan below it.

## From the command line

All three drivers are reachable as `einzel sweep`, `einzel scan` and
`einzel optimise`, over a study file that names a figure of merit rather than
carrying a function. See [CLI](cli.md#studies).

## What an optimiser is given

A **design variable** names a declared parameter and the interval to search it
over. The interval is not optional and it usually comes from the model, which is
what schema 0.2's `minimum` and `maximum` are for:

```csharp
var result = Optimiser.Run(
    document,
    [new DesignVariable("rodRatio")],           // bounds from the template
    model => Math.Abs(TwelvePoleFraction(model)),
    ObjectiveSense.Minimise,
    OptimisationAlgorithm.NelderMead);
```

Both algorithms are derivative-free box methods: Nelder–Mead needs an initial
scale, CMA-ES needs an initial step size, and both need somewhere to stop. A
search with no box would have to invent one from the nominal value, which is a
guess about a physical dimension that would not appear anywhere in the answer.
So an unbounded parameter is refused, with the AGT-3 error saying to add a bound
to the model or supply one on the variable.

A **derived** parameter is refused too. Varying a consequence varies nothing:
whatever it is derived from overwrites it on the next evaluation.

The **sense** is stated rather than left to the caller to negate. A sign error in
an objective does not throw — it returns the worst design in the box and looks
like a result.

## The normalised box

Every variable is mapped affinely onto the unit interval, so a search takes steps
of comparable size in a length, a voltage, and a dimensionless ratio without
anyone tuning per-variable scales.

A candidate outside the box is **repaired** to its face and charged a penalty
proportional to the square of how far it was moved. That keeps the objective
defined everywhere rather than putting a hard wall for a simplex to slide along,
and it is the standard boundary handling for CMA-ES.

A **failed** evaluation — a geometry that does not work, an objective that returns
null, a solve that throws — becomes a large finite number, not an infinity. A
simplex whose reflection lands on infinity learns nothing about which way to go
and can spend its whole budget contracting against a wall of equal values; a
large finite number leaves the penalty term visible underneath, so the search is
still pushed back toward where the model works. Failures are counted and warned
about, never fatal.

## What comes back

GRD-1 applies: every number is a `Measured`. The optimum is one envelope per
variable, and the interval on each is the spread of the final simplex or
population in that direction — how far apart the candidates the search was still
considering were.

That is a **convergence measure, not a confidence interval**, and the distinction
is worth keeping: it says how sharply the optimum is defined, which is often the
more useful fact. A first-order focus is a broad optimum in separation, and an
envelope that says so is telling the designer something real about the tolerance
they can afford.

Evidence is `Evidence.Search(Evaluations, Converged, SpreadSi)` — added for this,
because "an optimiser spent 45 evaluations and met its tolerance" is a different
kind of support from an ensemble or a grid refinement.

### Three warnings, all non-suppressible

| Code | When | Why it matters |
| --- | --- | --- |
| `optimiser.optimum-at-bound` | A variable ends on a face of its box | What is reported is where the search was stopped by the box, not a stationary point. The number looks identical either way |
| `optimiser.budget-exhausted` | The evaluation budget ran out first | It is a best-so-far, not an optimum |
| `optimiser.failed-evaluations` | Some designs produced no figure of merit | Advisory below a quarter of evaluations, qualified above |

The first is the most useful thing an optimiser can say and the easiest thing to
miss.

### A tight interval is not always the better answer

The natural reading of the spread is backwards as often as it is right, and the
first agent to use it in earnest read it backwards.

Two searches over the same reflectron: `resolvingPower` came back at
4028.58 ± 0.003 V, and `arrivalSpread` at a broad optimum ± 100 V. The instinct
is that ±0.003 V is the better-determined answer. It is the opposite. A simplex
contracts until its vertices stop disagreeing, and it will contract hardest where
the objective is *steepest* — so a spread of three millivolts on an 800 V box
means the search was crawling up a ramp into a discontinuity, and ±100 V means
the minimum is genuinely broad and flat.

Which is the one you can build. The tight optimum there sat on the apex of a
sawtooth: the resolving power climbed about 4.5 per volt below it and fell about
200 per volt above it, and under a ±10 V supply it *lost* to the nominal design
on average, 8521 ± 1190 against 8870 ± 55.

So the spread answers "how sharply is this optimum defined", and sharply defined
is a warning as often as it is a result. Read it beside
`optimiser.optimum-at-bound`, which is the other way a number can look like an
optimum without being one.

### The first evaluation is the model, not the box centre

Both searches start from the parameter values the model declares, clamped into
the box — not from the middle of the box. This matters if you are driving the
optimiser as a point probe by moving the box around: the box moves and the first
evaluation does not, so eleven probes come back with eleven different labels and
the same number. The tell is that evaluation 1 disagrees with the box centre.

There is no verb yet for "evaluate this figure of merit at this stated point",
and there should be; until then, probe by copying the model and changing the
declared value.

## Choosing an algorithm

**Nelder–Mead** for a handful of variables. Cheap per iteration, no derivatives,
copes with an objective that is only piecewise smooth — minimising the *magnitude*
of an aberration coefficient puts a kink at the optimum, and a simplex does not
care. Its weakness is well known: on a curved valley the simplex flattens along
the ridge and converges to a point that is not a minimum. Restarting from the
best vertex with a fresh full-size simplex is the standard remedy and is on by
default, because the failure is silent otherwise.

**CMA-ES** for more variables or a rougher objective. It learns the local
covariance, so it follows a curved valley a simplex crawls along, and it is far
less troubled by numerical noise — and every objective here is noisy, since each
evaluation ends in a field solve at a finite tolerance. Hansen's default
parameters, unmodified: they are not tuning knobs, and the whole claim of the
method is that they work across problems untouched.

Measured on the standard functions, from the same starting points:

| Problem | Nelder–Mead | CMA-ES |
| --- | --- | --- |
| Rosenbrock, 2 variables | 405 evaluations, f = 3.1e-11 | 715 evaluations, f = 3.0e-13 |
| Sphere, 6 variables, offset optimum | 1108 evaluations, worst coordinate 3.3e-7 | 1396 evaluations, worst coordinate 3.1e-7 |

Neither dominates at this size, which is the expected result and the reason both
are here.

## Setting the tolerances

The parameter tolerance is a fraction of the box. The objective tolerance is
relative to the objective's own magnitude, **with an absolute floor at the same
number** — because an objective being driven to zero, which is what cancelling an
aberration coefficient is, can never meet a purely relative criterion.

That floor is the one setting worth thinking about, and the rule is to put it at
the objective's own noise level and not below. Each evaluation ends in a
multigrid solve at a finite tolerance; what comes out has grit on it. A tolerance
under that asks the search to resolve noise, and it will spend its whole budget
doing so and then report, correctly, that it never converged.

There is also a floor no setting can move. Near a smooth optimum the objective is
quadratic, so a parameter offset δ costs only δ² in objective: at an objective
tolerance of 1e-8 the parameter is indistinguishable within about 1e-4 either
way, and no amount of searching recovers a digit the objective does not carry.

**Convergence is two tests and both must hold.** The simplex must be smaller than
`parameterTolerance` *and* its objective values must agree to
`objectiveTolerance`. In practice the second binds almost always, which makes the
first look broken: tighten `parameterTolerance` from 1e-3 to 1e-4 and nothing
changes at all, because it was already met four orders over. The warning used to
say "without meeting its tolerance", which is exactly the wrong number of
tolerances to name; it now reports the observed spread, which test it met, and
which one is still open.

## The worked example: the quadrupole rod ratio

A quadrupole made from round rods is not a hyperbolic field. With the four-fold
symmetry and the x–y antisymmetry the rods impose, the potential expands in
multipoles of order 2, 6, 10, 14 — the wanted quadrupole, then the 12-pole, then
the 20-pole. The classical design question is what rod radius makes the 12-pole
vanish, and the published answer is **r/r₀ = 1.1468** (Denison 1971), with 1.1487
also in circulation from a different criterion.

Minimising |A₆/A₂| sampled on a circle at 0.6 r₀, from the template's nominal:

```
eval   1: rodRatio  1.14680 -> 4.545E-005
eval  13: rodRatio  1.14055 -> 8.010E-006
eval  24: rodRatio  1.14133 -> 1.308E-006
eval  37: rodRatio  1.14148 -> 5.154E-008
```

45 evaluations, converged, **rodRatio = 1.14148 ± 3.05e-6**, with the 12-pole
cancelled by a factor of 880 from its value at the nominal ratio.

Two things make this worth having as a test rather than a demonstration.

It is a **literature number**, not a self-consistency check — and with the
cross-code tier unavailable, published results carry weight that internal
agreement cannot.

And it is **only measurable because the rod surfaces are cut cells**. A rasterised
circle is a staircase, and a staircase radiates harmonics of its own into exactly
the multipoles being measured. The quantity here is four parts in ten thousand of
the main term at nominal, and a few parts in a hundred million at the optimum.

Two further tests say what kind of number this is.

The answer is a **property of the field**, not of the circle it was measured on:
the optimum moves by 0.0016 across sampling radii from 0.45 to 0.75 r₀, where a
measurement artefact would move with the radius.

And the 0.46% gap from the published value is **discretisation, not a modelling
error**. Refining the mesh moves the answer toward 1.1468 and slows down doing it:

| Cells per r₀ | Optimum | Change |
| --- | --- | --- |
| 16 | 1.14148 | |
| 32 | 1.14426 | +0.00278 |
| 64 | 1.14487 | +0.00061 |

A ratio of 4.6 between successive changes is second order within the noise, and
Richardson extrapolation puts the grid-converged optimum at about **1.1451**. The
grounded housing accounts for roughly the remaining 0.002: widening the clearance
from 1.6 to 3.0 rod radii moves the 16-cell answer from 1.14148 to 1.14326, and
the classical result assumes no housing at all.

That is worth spelling out because the first guess was wrong. The housing looked
like the obvious culprit and it is the smaller of the two; only refining the mesh
showed which. The 64-cell case is a 513 by 513 solve per evaluation and takes
minutes, so the suite ships the 16 and 32 comparison and this page records the
third point.

---

# Scans

## What a scan is for, and why it is not a sweep with one channel

A sweep asks what a *distribution* of manufacturing error does to a design, and
reports a spread. An optimiser asks where the figure is best, and reports where it
stopped. Neither answers what section 12's whole Class B asks — stability and
cut-off boundaries, mass filter peak shape against a scan line, low-mass cut-off
for funnels and RF guides — because every one of those is a question about a
**curve**, and averaging a curve into an interval answers a different question.

This is the operation every curve in this engine had so far been produced by a
hand-written loop in a test file: the low-mass cut-off scans, the extraction-slot
scan at 0.5 to 3.0 mm, the drift-length scan at 20.7 ns/mm. None of them wrote a
manifest, none could be re-run from the project, and none was reachable by an agent
at all.

```json
{
  "name": "low-mass-cutoff",
  "model": "../models/quadrupole-rf.json",
  "figureOfMerit": "transmission",
  "scan": {
    "parameter": "q",
    "from": 0.80, "to": 0.95, "unit": "1",
    "points": 31,
    "spacing": "linear"
  }
}
```

```
einzel scan studies/low-mass-cutoff.json
```

`spacing` may be `logarithmic`, which is what a range spanning decades needs: a
pressure scan from 1e-4 to 10 mbar taken linearly puts every point but one above a
millibar and says nothing about the thin end — and the thin end is where the
transport mode changes.

## Four decisions worth keeping

**Both ends are included, and returned exactly.** Half of the interval (0.1, 0.2)
is 0.15000000000000002 in binary, so an end reached by interpolation lands an ulp
outside a bound — and a scan written the obvious way, from a parameter's declared
minimum to its declared maximum, then has its last point refused by validation with
nothing on the page to say why the row is blank. The ends are the declared
quantities themselves; interpolation is for the interior, where an ulp means
nothing.

**A failed point is a row, not the end of the scan**, and the reason matters more
here than in a sweep. On a stability scan "the ion was lost on `rodYPlus`" is the
*answer*: a cut-off is precisely the value at which the figure stops existing, so a
driver that stopped at the first failure would stop exactly where the interesting
thing is.

**A range past the declared bounds is warned about once, up front.** Walking past
what the template says is buildable is a legitimate thing to ask for — it is how
you find where a design stops working — but half a table of blanks with no
explanation reads as the solver failing rather than as the model refusing.
`scan.outside-declared-bounds` says which end and what the bound was.

**The steepest interval is reported, and is deliberately not called a boundary.**
What comes back is where on the grid *actually computed* the figure moves fastest,
and how wide that interval is as a fraction of the whole scan — which is the
currency ACC-6 is written in, one part in five hundred. A reader can then tell a
resolved transition from a scan too coarse to have found one:

```
steepest between 0.905 and 0.910 1: the figure stops existing
  that interval is 1 part in 30 of the scan; ACC-6 asks for 1 in 500
```

An interval where the figure *vanishes* outranks one where it merely moves a long
way, and it is flagged as `figureVanishes` rather than an infinite change, because
JSON has no infinity and a null would be indistinguishable from a value that was
never computed. On a mass filter that vanishing is the cut-off, and scoring it as
"no change" would rank the one interesting interval last.

## What a scan is not, yet

Class B proper. ACC-6 wants a boundary resolved to one part in five hundred of the
scan variable, which needs a bisection onto the transition rather than a grid
across it; the steepest interval is the honest precursor and says how coarse it is.
The mass-filter peak shape against a scan line, and the secular frequency spectrum
against notch width, need the same machinery plus figures of merit that do not
exist yet.

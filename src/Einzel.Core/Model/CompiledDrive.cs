namespace Einzel.Core.Model;

/// <summary>The shape of an RF drive over one cycle, as a model declares it.</summary>
/// <remarks>
/// Named here rather than reusing the field layer's waveform type, because the
/// model layer sits below the field layer and a document has no business knowing
/// how a waveform is evaluated. The mapping happens once, where the field is built.
/// </remarks>
public enum DriveWaveform
{
    /// <summary>What a resonant circuit produces. Gives the Mathieu equation.</summary>
    Sinusoid,

    /// <summary>What a switching supply produces. Gives the Meissner equation.</summary>
    Rectangular,
}

/// <summary>
/// The drive a solved geometry is operated with, validated and reduced to SI.
/// </summary>
/// <param name="FrequencyHz">Drive frequency, in hertz.</param>
/// <param name="Waveform">The shape of one cycle.</param>
/// <param name="DutyCycle">
/// Fraction of the cycle at the positive level, for a rectangular wave. One half is
/// a balanced square wave; anything else carries a mean, which acts as a DC offset
/// and is the whole trick of a digital mass filter.
/// </param>
/// <param name="Name">
/// What the generator is called, so an electrode can say which one it taps. Empty
/// where a geometry has only one and nothing needs naming.
/// </param>
/// <remarks>
/// <para>
/// A generator, not an electrode setting. A real instrument has a supply and
/// electrodes tapped off it at various amplitudes and phases, and what each
/// electrode does with it is on the electrode: an amplitude, and a phase as a
/// fraction of a cycle. A quadrupole is one pair at phase zero and the other at a
/// half; a travelling-wave guide is a ramp of phases along its length.
/// </para>
/// <para>
/// <strong>A geometry may have more than one, and the first version of this said it
/// may not.</strong> The original note here read "one drive per solve ... modelling
/// it the other way round would let a document declare two frequencies on one
/// structure - which is a different instrument and almost always a mistake." Two
/// devices refuted it. A real travelling-wave guide superposes a fast confining RF
/// on a slow travelling wave, which is why the shipped template confines to about
/// 0.1 mm on a 2 mm bore; and a trap performing a stored-waveform isolation runs a
/// low-frequency notched comb across its endcaps while the ring carries the main
/// drive. Two frequencies on one structure is not a mistake, it is what a trap is.
/// </para>
/// <para>
/// It costs nothing in the solver. Basis superposition is indifferent to what the
/// weights are functions of, so two generators reaching the same electrodes in the
/// same proportions are one solved pattern carrying two weights on two clocks -
/// exactly as a DC supply and an RF supply already were. What multiplies the solve
/// count is a different <em>spatial pattern</em>, never a different frequency.
/// </para>
/// </remarks>
public sealed record CompiledDrive(
    double FrequencyHz, DriveWaveform Waveform, double DutyCycle, string Name = "");

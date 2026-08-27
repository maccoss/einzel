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
/// <remarks>
/// <para>
/// One drive per solve, not one per electrode. A real instrument has a generator
/// and electrodes tapped off it at various amplitudes and phases, and modelling it
/// the other way round would let a document declare two frequencies on one
/// structure - which is a different instrument and almost always a mistake.
/// </para>
/// <para>
/// What each electrode does with the drive is on the electrode: an amplitude, and a
/// phase as a fraction of a cycle. A quadrupole is one pair at phase zero and the
/// other at a half; a travelling-wave guide is a ramp of phases along its length.
/// </para>
/// </remarks>
public sealed record CompiledDrive(double FrequencyHz, DriveWaveform Waveform, double DutyCycle);

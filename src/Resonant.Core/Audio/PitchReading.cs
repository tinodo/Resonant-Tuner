using Resonant.Core.Tuning;

namespace Resonant.Core.Audio;

public enum PitchRejection
{
    None,
    Silence,
    InvalidSamples,
    OutOfRange,
    Uncertain,
    InsufficientData,
}

/// <summary>Search band for the periodicity estimator, in Hz.</summary>
public sealed record PitchSearchRange(double MinHz, double MaxHz)
{
    public static PitchSearchRange Full { get; } = new(NoteMath.MinSupportedHz, NoteMath.MaxSupportedHz);
}

/// <summary>Result of analysing one window. Hz is only meaningful when <see cref="IsValid"/> is true.</summary>
public readonly record struct PitchEstimate(
    double Hz,
    double Confidence,
    double Rms,
    bool IsClipping,
    PitchRejection Rejection)
{
    public bool IsValid => Rejection == PitchRejection.None;
}

/// <summary>An immutable measurement published to the UI. A reading is created for every analysis hop, valid or not.</summary>
public readonly record struct PitchReading(
    long Timestamp,
    double Hz,
    double Confidence,
    double Rms,
    bool IsClipping,
    bool IsValid,
    PitchRejection Rejection)
{
    public static PitchReading Valid(long timestamp, PitchEstimate estimate) =>
        new(timestamp, estimate.Hz, estimate.Confidence, estimate.Rms, estimate.IsClipping, true, PitchRejection.None);

    public static PitchReading Invalid(long timestamp, PitchRejection rejection, double rms = 0, bool clipping = false, double confidence = 0) =>
        new(timestamp, 0, confidence, rms, clipping, false, rejection);
}

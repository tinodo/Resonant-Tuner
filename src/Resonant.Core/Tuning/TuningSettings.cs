namespace Resonant.Core.Tuning;

/// <summary>Session-only tuner settings. Nothing here is persisted.</summary>
public sealed record TuningSettings
{
    public const double MinToleranceCents = 1.0;
    public const double MaxToleranceCents = 10.0;
    public const int MaxCapoSemitones = 12;

    public static TuningSettings Default { get; } = new();

    public double A4Hz { get; init; } = NoteMath.DefaultA4Hz;

    public double ToleranceCents { get; init; } = 2.0;

    public int CapoSemitones { get; init; }

    public NoteSpelling Spelling { get; init; } = NoteSpelling.Sharps;

    public bool IsValid =>
        double.IsFinite(A4Hz) && A4Hz is >= NoteMath.MinA4Hz and <= NoteMath.MaxA4Hz &&
        double.IsFinite(ToleranceCents) && ToleranceCents is >= MinToleranceCents and <= MaxToleranceCents &&
        CapoSemitones is >= 0 and <= MaxCapoSemitones;

    /// <summary>Returns a copy with every value forced into its supported range.</summary>
    public TuningSettings Clamped() => this with
    {
        A4Hz = double.IsFinite(A4Hz) ? Math.Clamp(A4Hz, NoteMath.MinA4Hz, NoteMath.MaxA4Hz) : NoteMath.DefaultA4Hz,
        ToleranceCents = double.IsFinite(ToleranceCents)
            ? Math.Clamp(ToleranceCents, MinToleranceCents, MaxToleranceCents)
            : 2.0,
        CapoSemitones = Math.Clamp(CapoSemitones, 0, MaxCapoSemitones),
    };
}

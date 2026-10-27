using Resonant.Core.Audio;

namespace Resonant.Core.Tuning;

public enum Correction
{
    None,
    Hold,
    TuneUp,
    TuneDown,
    InTune,
}

/// <summary>Unrounded tuning guidance for the latest measurement. Negative cents means flat, so the correction is Tune up.</summary>
public sealed record TuningFeedback
{
    public static TuningFeedback None { get; } = new();

    public bool HasReading { get; init; }

    /// <summary>True when the last valid reading is being held briefly after input became invalid.</summary>
    public bool IsStale { get; init; }

    /// <summary>The selected string target, or null in chromatic mode.</summary>
    public TuningTarget? Target { get; init; }

    /// <summary>The target note, or the nearest note in chromatic mode.</summary>
    public Note? Note { get; init; }

    public double MeasuredHz { get; init; }

    public double TargetHz { get; init; }

    public double Cents { get; init; }

    public double Confidence { get; init; }

    public Correction Correction { get; init; }

    public bool IsLocked { get; init; }

    /// <summary>True when the error exceeds the 50-cent meter scale; the signed cents value is still exact.</summary>
    public bool IsOffScale { get; init; }
}

public interface ITuningSession
{
    TuningFeedback Current { get; }

    IReadOnlyList<TuningTarget> Targets { get; }

    /// <summary>Index into <see cref="Targets"/> of the manually locked string, or null for automatic selection.</summary>
    int? ManualTargetIndex { get; }

    /// <summary>The band the estimator should search for the current configuration.</summary>
    PitchSearchRange SearchRange { get; }

    void Configure(TuningPreset? preset, TuningSettings settings);

    void SelectTarget(int? targetIndex);

    TuningFeedback Update(PitchReading reading);

    /// <summary>Expires stale feedback when no reading has arrived.</summary>
    TuningFeedback Tick();

    void Reset();
}

namespace Resonant.Core.Tuning;

/// <summary>In-memory state shared by the pages. Nothing is persisted; it resets when the app closes.</summary>
public sealed class SessionState
{
    private readonly List<TuningPreset> _customTunings = [];

    public SessionState()
    {
        Preset = TuningPresets.GuitarStandard;
    }

    public event EventHandler? Changed;

    public TuningSettings Settings { get; private set; } = TuningSettings.Default;

    /// <summary>The selected preset, or <see langword="null"/> for chromatic mode.</summary>
    public TuningPreset? Preset { get; private set; }

    public IReadOnlyList<TuningPreset> CustomTunings => _customTunings;

    public IEnumerable<TuningPreset> AllPresets => TuningPresets.All.Concat(_customTunings);

    public void UpdateSettings(TuningSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Settings = settings.Clamped();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SelectPreset(TuningPreset? preset)
    {
        Preset = preset;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void AddCustomTuning(TuningPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        _customTunings.Add(preset);
        Preset = preset;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void RemoveCustomTuning(TuningPreset preset)
    {
        if (!_customTunings.Remove(preset))
        {
            return;
        }

        if (ReferenceEquals(Preset, preset))
        {
            Preset = TuningPresets.GuitarStandard;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}

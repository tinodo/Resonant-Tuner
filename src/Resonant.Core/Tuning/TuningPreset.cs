using Resonant.Core.Localization;

namespace Resonant.Core.Tuning;

public enum InstrumentKind
{
    Guitar,
    Ukulele,
    Bass,
    Violin,
    Mandolin,
}

/// <summary>A string target at the current calibration and capo. <see cref="StringNumber"/> 1 is the highest-numbered physical string slot.</summary>
public sealed record TuningTarget(int StringNumber, Note Note, double Hz)
{
    public string Label(NoteSpelling spelling) => Note.ToString(spelling);
}

/// <summary>An ordered set of string targets in physical string order (string N first, string 1 last).</summary>
public sealed record TuningPreset(
    string Id,
    string Name,
    InstrumentKind Instrument,
    IReadOnlyList<Note> Strings,
    string Description,
    bool IsCustom = false)
{
    public int StringCount => Strings.Count;

    /// <summary>Only guitar presets honour the capo setting.</summary>
    public bool SupportsCapo => Instrument == InstrumentKind.Guitar;

    public string LocalizedName => IsCustom ? Name : Text.Get(Name);

    public string LocalizedDescription => Text.Get(Description);

    public string DisplayName => $"{InstrumentName} \u00B7 {LocalizedName}";

    public string InstrumentName => Instrument switch
    {
        InstrumentKind.Guitar => Text.Get("Guitar"),
        InstrumentKind.Ukulele => Text.Get("Ukulele"),
        InstrumentKind.Bass => Text.Get("Bass"),
        InstrumentKind.Violin => Text.Get("Violin"),
        InstrumentKind.Mandolin => Text.Get("Mandolin"),
        _ => Text.Get("Instrument"),
    };

    public string StringSummary(NoteSpelling spelling) =>
        string.Join("  ", Strings.Select(n => n.ToString(spelling)));

    /// <summary>Targets for the given calibration. A capo raises guitar targets; A4 scales every target.</summary>
    public IReadOnlyList<TuningTarget> GetTargets(TuningSettings settings)
    {
        var capo = SupportsCapo ? settings.CapoSemitones : 0;
        var targets = new TuningTarget[Strings.Count];
        for (var i = 0; i < targets.Length; i++)
        {
            var sounding = Strings[i].Transpose(capo);
            targets[i] = new TuningTarget(
                Strings.Count - i,
                sounding,
                NoteMath.FrequencyFromMidi(sounding.Midi, settings.A4Hz));
        }

        return targets;
    }
}

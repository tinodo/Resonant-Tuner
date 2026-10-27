using Resonant.Core.Localization;

namespace Resonant.Core.Tuning;

public sealed record CustomTuningResult(TuningPreset? Preset, IReadOnlyList<string> Errors)
{
    public bool IsValid => Preset is not null && Errors.Count == 0;
}

/// <summary>Validates a session-only custom tuning entered as free text.</summary>
public static class CustomTuningValidator
{
    public const int MinStrings = 1;
    public const int MaxStrings = 12;
    public const int MaxNameLength = 40;

    private static readonly char[] Separators = [' ', ',', ';', '\t', '\r', '\n'];

    public static CustomTuningResult Validate(string? name, InstrumentKind instrument, int expectedStringCount, string? targetsText)
    {
        var errors = new List<string>();
        var trimmedName = name?.Trim() ?? string.Empty;

        if (trimmedName.Length == 0)
        {
            errors.Add(Text.Get("Enter a name for the tuning."));
        }
        else if (trimmedName.Length > MaxNameLength)
        {
            errors.Add(Text.Format("Use {0} characters or fewer for the name.", MaxNameLength));
        }

        if (expectedStringCount is < MinStrings or > MaxStrings)
        {
            errors.Add(Text.Format("Choose between {0} and {1} strings.", MinStrings, MaxStrings));
        }

        var tokens = (targetsText ?? string.Empty)
            .Split(Separators, StringSplitOptions.RemoveEmptyEntries);

        var notes = new List<Note>(tokens.Length);
        foreach (var token in tokens)
        {
            if (!Note.TryParse(token, out var note))
            {
                errors.Add(Text.Format("'{0}' is not a note with an octave, for example E2 or F#3.", token));
                continue;
            }

            var hz = NoteMath.FrequencyFromMidi(note.Midi);
            if (hz is < NoteMath.MinSupportedHz or > NoteMath.MaxSupportedHz)
            {
                errors.Add(Text.Format("{0} is outside the {1:0}\u2013{2:0} Hz range this tuner can measure.", token, NoteMath.MinSupportedHz, NoteMath.MaxSupportedHz));
                continue;
            }

            notes.Add(note);
        }

        if (tokens.Length == 0)
        {
            errors.Add(Text.Get("Enter at least one string target such as E2."));
        }
        else if (tokens.Length != expectedStringCount && expectedStringCount is >= MinStrings and <= MaxStrings)
        {
            errors.Add(Text.Format("Expected {0} targets but found {1}.", expectedStringCount, tokens.Length));
        }

        if (errors.Count > 0 || notes.Count == 0)
        {
            return new CustomTuningResult(null, errors);
        }

        var preset = new TuningPreset(
            "custom-" + Guid.NewGuid().ToString("N"),
            trimmedName,
            instrument,
            notes,
            "Custom tuning \u00B7 this session only",
            IsCustom: true);
        return new CustomTuningResult(preset, errors);
    }
}

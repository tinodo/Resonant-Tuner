namespace Resonant.Core.Tuning;

/// <summary>Bundled, read-only tuning definitions.</summary>
public static class TuningPresets
{
    public static TuningPreset GuitarStandard { get; } = Make("guitar-standard", "Standard", InstrumentKind.Guitar, "Lowest string to highest", "E2 A2 D3 G3 B3 E4");

    public static IReadOnlyList<TuningPreset> All { get; } =
    [
        GuitarStandard,
        Make("guitar-drop-d", "Drop D", InstrumentKind.Guitar, "Low string one whole step down", "D2 A2 D3 G3 B3 E4"),
        Make("guitar-dadgad", "DADGAD", InstrumentKind.Guitar, "Open suspended tuning", "D2 A2 D3 G3 A3 D4"),
        Make("guitar-open-g", "Open G", InstrumentKind.Guitar, "Open G major", "D2 G2 D3 G3 B3 D4"),
        Make("guitar-open-d", "Open D", InstrumentKind.Guitar, "Open D major", "D2 A2 D3 F#3 A3 D4"),
        Make("guitar-half-step-down", "Half-step down", InstrumentKind.Guitar, "Every string one semitone lower", "Eb2 Ab2 Db3 Gb3 Bb3 Eb4"),
        Make("ukulele-high-g", "High G", InstrumentKind.Ukulele, "Re-entrant physical string order", "G4 C4 E4 A4"),
        Make("ukulele-low-g", "Low G", InstrumentKind.Ukulele, "Linear physical string order", "G3 C4 E4 A4"),
        Make("ukulele-baritone", "Baritone", InstrumentKind.Ukulele, "Tuned like the top four guitar strings", "D3 G3 B3 E4"),
        Make("bass-4-standard", "Standard four-string", InstrumentKind.Bass, "Bass, 4 strings", "E1 A1 D2 G2"),
        Make("bass-5-standard", "Standard five-string", InstrumentKind.Bass, "Bass, low B at 30.8677 Hz", "B0 E1 A1 D2 G2"),
        Make("violin-standard", "Standard", InstrumentKind.Violin, "Tune one string at a time", "G3 D4 A4 E5"),
        Make("mandolin-standard", "Standard", InstrumentKind.Mandolin, "Tune each course member separately", "G3 D4 A4 E5"),
    ];

    public static TuningPreset? FindById(string id) =>
        All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.Ordinal));

    private static TuningPreset Make(string id, string name, InstrumentKind kind, string description, string notes) =>
        new(
            id,
            name,
            kind,
            notes.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Note.Parse).ToArray(),
            description);
}

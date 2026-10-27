using System.Globalization;

namespace Resonant.Core.Tuning;

public enum NoteSpelling
{
    Sharps,
    Flats,
}

/// <summary>A scientific-pitch-notation note (C4 = MIDI 60, A4 = MIDI 69).</summary>
public readonly record struct Note
{
    public const int MinMidi = 0;
    public const int MaxMidi = 127;

    private static readonly string[] SharpNames = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];
    private static readonly string[] FlatNames = ["C", "Db", "D", "Eb", "E", "F", "Gb", "G", "Ab", "A", "Bb", "B"];

    public Note(int midi)
    {
        if (midi is < MinMidi or > MaxMidi)
        {
            throw new ArgumentOutOfRangeException(nameof(midi), midi, "MIDI note must be between 0 and 127.");
        }

        Midi = midi;
    }

    public int Midi { get; }

    public int PitchClass => Midi % 12;

    public int Octave => (Midi / 12) - 1;

    public string Name(NoteSpelling spelling) =>
        (spelling == NoteSpelling.Sharps ? SharpNames : FlatNames)[PitchClass];

    public string ToString(NoteSpelling spelling) =>
        Name(spelling) + Octave.ToString(CultureInfo.InvariantCulture);

    public override string ToString() => ToString(NoteSpelling.Sharps);

    public Note Transpose(int semitones) => new(Midi + semitones);

    public static Note Parse(string text) =>
        TryParse(text, out var note)
            ? note
            : throw new FormatException($"'{text}' is not a valid note such as E2, F#3 or Bb4.");

    public static bool TryParse(string? text, out Note note)
    {
        note = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var s = text.Trim();
        var index = 0;
        var letter = char.ToUpperInvariant(s[index++]);
        var pitchClass = letter switch
        {
            'C' => 0,
            'D' => 2,
            'E' => 4,
            'F' => 5,
            'G' => 7,
            'A' => 9,
            'B' => 11,
            _ => -1,
        };

        if (pitchClass < 0)
        {
            return false;
        }

        var accidental = 0;
        while (index < s.Length)
        {
            var c = s[index];
            if (c is '#' or '\u266F')
            {
                accidental++;
            }
            else if (c is 'b' or '\u266D')
            {
                accidental--;
            }
            else
            {
                break;
            }

            index++;
        }

        if (index >= s.Length ||
            !int.TryParse(s.AsSpan(index), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var octave))
        {
            return false;
        }

        var midi = ((octave + 1) * 12) + pitchClass + accidental;
        if (midi is < MinMidi or > MaxMidi)
        {
            return false;
        }

        note = new Note(midi);
        return true;
    }
}

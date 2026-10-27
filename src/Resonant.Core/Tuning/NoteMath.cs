namespace Resonant.Core.Tuning;

/// <summary>Twelve-tone equal temperament helpers.</summary>
public static class NoteMath
{
    public const double MinA4Hz = 415.0;
    public const double MaxA4Hz = 466.0;
    public const double DefaultA4Hz = 440.0;

    /// <summary>Lowest and highest fundamental the tuner claims to measure.</summary>
    public const double MinSupportedHz = 29.0;
    public const double MaxSupportedHz = 1500.0;

    /// <summary>Frequency of a MIDI note: A4 * 2^((midi - 69) / 12).</summary>
    public static double FrequencyFromMidi(double midi, double a4Hz = DefaultA4Hz) =>
        a4Hz * Math.Pow(2.0, (midi - 69.0) / 12.0);

    public static double MidiFromFrequency(double hz, double a4Hz = DefaultA4Hz) =>
        69.0 + (12.0 * Math.Log2(hz / a4Hz));

    /// <summary>Signed error in cents; negative means the measured pitch is flat of the target.</summary>
    public static double Cents(double measuredHz, double targetHz) =>
        1200.0 * Math.Log2(measuredHz / targetHz);

    public static Note NearestNote(double hz, double a4Hz, out double cents)
    {
        var midi = (int)Math.Round(MidiFromFrequency(hz, a4Hz), MidpointRounding.AwayFromZero);
        midi = Math.Clamp(midi, Note.MinMidi, Note.MaxMidi);
        cents = Cents(hz, FrequencyFromMidi(midi, a4Hz));
        return new Note(midi);
    }
}

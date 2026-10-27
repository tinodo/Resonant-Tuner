using Resonant.Core.Tuning;

namespace Resonant.Core.Tests;

[TestClass]
public sealed class NoteMathTests
{
    [TestMethod]
    public void A4AtDefaultCalibrationIs440Hz()
    {
        Assert.AreEqual(440.0, NoteMath.FrequencyFromMidi(69), 1e-9);
    }

    [TestMethod]
    [DataRow("E2", 82.4069)]
    [DataRow("A2", 110.0)]
    [DataRow("D3", 146.8324)]
    [DataRow("G3", 195.9977)]
    [DataRow("B3", 246.9417)]
    [DataRow("E4", 329.6276)]
    [DataRow("B0", 30.8677)]
    public void StandardFrequenciesMatchEqualTemperament(string note, double expectedHz)
    {
        var hz = NoteMath.FrequencyFromMidi(Note.Parse(note).Midi);
        Assert.AreEqual(expectedHz, hz, 0.0005);
    }

    [TestMethod]
    public void CentsSignIsNegativeWhenFlat()
    {
        var flat = 110.0 * Math.Pow(2.0, -6.0 / 1200.0);
        Assert.AreEqual(-6.0, NoteMath.Cents(flat, 110.0), 1e-9);
        Assert.AreEqual(1200.0, NoteMath.Cents(220.0, 110.0), 1e-9);
    }

    [TestMethod]
    [DataRow(415.0)]
    [DataRow(440.0)]
    [DataRow(466.0)]
    public void CalibrationScalesEveryPresetFrequencyNotJustA4(double a4)
    {
        var settings = new TuningSettings { A4Hz = a4 };
        var targets = TuningPresets.GuitarStandard.GetTargets(settings);
        var a2 = targets[1];
        Assert.AreEqual(a4 / 4.0, a2.Hz, 1e-9);

        var e2 = targets[0];
        Assert.AreEqual(a4 * Math.Pow(2.0, (40 - 69) / 12.0), e2.Hz, 1e-9);
    }

    [TestMethod]
    public void NearestNoteReportsSignedCents()
    {
        var hz = 440.0 * Math.Pow(2.0, 12.0 / 1200.0);
        var note = NoteMath.NearestNote(hz, 440.0, out var cents);
        Assert.AreEqual(69, note.Midi);
        Assert.AreEqual(12.0, cents, 1e-9);
    }
}

[TestClass]
public sealed class NoteTests
{
    [TestMethod]
    [DataRow("C4", 60)]
    [DataRow("A4", 69)]
    [DataRow("B0", 23)]
    [DataRow("F#3", 54)]
    [DataRow("Gb3", 54)]
    [DataRow("Eb2", 39)]
    [DataRow("c-1", 0)]
    public void ParsesScientificPitchNotation(string text, int midi)
    {
        Assert.IsTrue(Note.TryParse(text, out var note));
        Assert.AreEqual(midi, note.Midi);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("H2")]
    [DataRow("E")]
    [DataRow("E#")]
    [DataRow("E99")]
    public void RejectsInvalidNotes(string text)
    {
        Assert.IsFalse(Note.TryParse(text, out _));
    }

    [TestMethod]
    public void SpellingSelectsSharpsOrFlats()
    {
        var note = Note.Parse("A#3");
        Assert.AreEqual("A#3", note.ToString(NoteSpelling.Sharps));
        Assert.AreEqual("Bb3", note.ToString(NoteSpelling.Flats));
    }
}

using Resonant.Core.Tuning;

namespace Resonant.Core.Tests;

[TestClass]
public sealed class TuningPresetTests
{
    [TestMethod]
    public void AllRequiredPresetsExistWithExpectedStrings()
    {
        var expected = new Dictionary<string, string>
        {
            ["guitar-standard"] = "E2 A2 D3 G3 B3 E4",
            ["guitar-drop-d"] = "D2 A2 D3 G3 B3 E4",
            ["guitar-dadgad"] = "D2 A2 D3 G3 A3 D4",
            ["guitar-open-g"] = "D2 G2 D3 G3 B3 D4",
            ["guitar-open-d"] = "D2 A2 D3 F#3 A3 D4",
            ["guitar-half-step-down"] = "D#2 G#2 C#3 F#3 A#3 D#4",
            ["ukulele-high-g"] = "G4 C4 E4 A4",
            ["ukulele-low-g"] = "G3 C4 E4 A4",
            ["ukulele-baritone"] = "D3 G3 B3 E4",
            ["bass-4-standard"] = "E1 A1 D2 G2",
            ["bass-5-standard"] = "B0 E1 A1 D2 G2",
            ["violin-standard"] = "G3 D4 A4 E5",
            ["mandolin-standard"] = "G3 D4 A4 E5",
        };

        foreach (var (id, notes) in expected)
        {
            var preset = TuningPresets.FindById(id);
            Assert.IsNotNull(preset, id);
            Assert.AreEqual(notes, string.Join(' ', preset.Strings.Select(n => n.ToString(NoteSpelling.Sharps))), id);
        }

        Assert.AreEqual(expected.Count, TuningPresets.All.Count);
    }

    [TestMethod]
    public void EveryPresetTargetIsInsideTheMeasurableRangeAtEveryCalibrationEndpoint()
    {
        foreach (var a4 in new[] { 415.0, 440.0, 466.0 })
        {
            foreach (var preset in TuningPresets.All)
            {
                foreach (var target in preset.GetTargets(new TuningSettings { A4Hz = a4 }))
                {
                    Assert.IsTrue(target.Hz is >= NoteMath.MinSupportedHz and <= NoteMath.MaxSupportedHz, $"{preset.Id} {target.Note} {a4}");
                }
            }
        }
    }

    [TestMethod]
    public void UkuleleHighGKeepsReEntrantPhysicalOrder()
    {
        var targets = TuningPresets.FindById("ukulele-high-g")!.GetTargets(TuningSettings.Default);
        Assert.AreEqual(4, targets[0].StringNumber);
        Assert.AreEqual("G4", targets[0].Note.ToString());
        Assert.IsTrue(targets[0].Hz > targets[1].Hz);
        Assert.AreEqual(1, targets[3].StringNumber);
    }

    [TestMethod]
    public void GuitarStringsAreNumberedSixToOne()
    {
        var targets = TuningPresets.GuitarStandard.GetTargets(TuningSettings.Default);
        Assert.AreEqual(6, targets[0].StringNumber);
        Assert.AreEqual(1, targets[5].StringNumber);
    }

    [TestMethod]
    public void CapoRaisesGuitarTargetsOnly()
    {
        var settings = new TuningSettings { CapoSemitones = 2 };
        var guitar = TuningPresets.GuitarStandard.GetTargets(settings);
        Assert.AreEqual("F#2", guitar[0].Note.ToString());
        Assert.AreEqual(NoteMath.FrequencyFromMidi(42), guitar[0].Hz, 1e-9);

        var ukulele = TuningPresets.FindById("ukulele-high-g")!.GetTargets(settings);
        Assert.AreEqual("G4", ukulele[0].Note.ToString());
    }

    [TestMethod]
    public void SettingsClampToDocumentedRanges()
    {
        var clamped = new TuningSettings { A4Hz = 500, ToleranceCents = 0.1, CapoSemitones = 20 }.Clamped();
        Assert.AreEqual(466.0, clamped.A4Hz);
        Assert.AreEqual(1.0, clamped.ToleranceCents);
        Assert.AreEqual(12, clamped.CapoSemitones);
        Assert.IsTrue(clamped.IsValid);
        Assert.IsFalse(new TuningSettings { A4Hz = 400 }.IsValid);
    }
}

[TestClass]
public sealed class CustomTuningValidatorTests
{
    [TestMethod]
    public void AcceptsDefaultEditorSpecimen()
    {
        var result = CustomTuningValidator.Validate("My open tuning", InstrumentKind.Guitar, 6, "E2 A2 D3 G3 B3 E4");
        Assert.IsTrue(result.IsValid);
        Assert.AreEqual(6, result.Preset!.StringCount);
        Assert.IsTrue(result.Preset.IsCustom);
    }

    [TestMethod]
    public void AllowsDuplicateTargets()
    {
        var result = CustomTuningValidator.Validate("Unison", InstrumentKind.Mandolin, 3, "A4 A4 A4");
        Assert.IsTrue(result.IsValid);
    }

    [TestMethod]
    public void SupportsOneToTwelveStrings()
    {
        Assert.IsTrue(CustomTuningValidator.Validate("One", InstrumentKind.Guitar, 1, "E2").IsValid);
        Assert.IsTrue(CustomTuningValidator.Validate("Twelve", InstrumentKind.Guitar, 12, string.Join(' ', Enumerable.Repeat("E3", 12))).IsValid);
        Assert.IsFalse(CustomTuningValidator.Validate("Thirteen", InstrumentKind.Guitar, 13, string.Join(' ', Enumerable.Repeat("E3", 13))).IsValid);
        Assert.IsFalse(CustomTuningValidator.Validate("Zero", InstrumentKind.Guitar, 0, string.Empty).IsValid);
    }

    [TestMethod]
    public void ReportsEveryProblem()
    {
        var result = CustomTuningValidator.Validate(" ", InstrumentKind.Guitar, 6, "E2 X9 C0");
        Assert.IsFalse(result.IsValid);
        Assert.IsNull(result.Preset);
        Assert.IsTrue(result.Errors.Count >= 3, string.Join("|", result.Errors));
    }
}

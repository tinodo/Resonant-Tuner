using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Resonant.Core.Localization;
using Resonant.Core.Tuning;

namespace Resonant.Core.Tests;

[TestClass]
public sealed class LocalizationTests
{
    [TestMethod]
    [DataRow("en-US", "Tuner")]
    [DataRow("nl-NL", "Stemmer")]
    [DataRow("de-DE", "Stimmger\u00E4t")]
    [DataRow("fr-FR", "Accordeur")]
    [DataRow("es-ES", "Afinador")]
    public void RegionalLanguagesResolveTheirResource(string culture, string expected)
    {
        Assert.AreEqual(expected, Text.Get("Tuner", CultureInfo.GetCultureInfo(culture)));
    }

    [TestMethod]
    public void UnsupportedLanguageFallsBackToEnglish()
    {
        Assert.AreEqual("Sound & display", Text.Get("Sound & display", CultureInfo.GetCultureInfo("ja-JP")));
    }

    [TestMethod]
    public void UnknownExternalDiagnosticIsPreserved()
    {
        Assert.AreEqual("Device-specific diagnostic", Text.Get("Device-specific diagnostic", CultureInfo.GetCultureInfo("fr-FR")));
    }

    [TestMethod]
    [DataRow("nl")]
    [DataRow("de")]
    [DataRow("fr")]
    [DataRow("es")]
    public void CatalogsHaveMatchingKeysAndFormatArguments(string language)
    {
        var manager = new ResourceManager("Resonant.Core.Localization.Strings", typeof(Text).Assembly);
        var english = ReadCatalog(manager, CultureInfo.InvariantCulture);
        var translated = ReadCatalog(manager, CultureInfo.GetCultureInfo(language));
        CollectionAssert.AreEquivalent(english.Keys.ToArray(), translated.Keys.ToArray());
        Assert.AreEqual(translated.Count, translated.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        foreach (var (key, value) in translated)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(value), $"{language}: {key}");
            var expectedArguments = Regex.Matches(english[key], @"\{\d+(?:,[^}:]+)?(?::[^}]+)?\}")
                .Select(match => match.Value).Order(StringComparer.Ordinal).ToArray();
            var actualArguments = Regex.Matches(value, @"\{\d+(?:,[^}:]+)?(?::[^}]+)?\}")
                .Select(match => match.Value).Order(StringComparer.Ordinal).ToArray();
            CollectionAssert.AreEqual(expectedArguments, actualArguments, $"{language}: {key}");
            Assert.AreEqual(CompositeFormat.Parse(english[key]).MinimumArgumentCount,
                CompositeFormat.Parse(value).MinimumArgumentCount, $"{language}: {key}");
        }
    }

    [TestMethod]
    [DataRow("en-US", "Reference  A4 = 442.5 Hz")]
    [DataRow("nl-NL", "Referentie  A4 = 442,5 Hz")]
    [DataRow("de-DE", "Referenz  A4 = 442,5 Hz")]
    [DataRow("fr-FR", "R\u00e9f\u00e9rence  A4 = 442,5 Hz")]
    [DataRow("es-ES", "Referencia  A4 = 442,5 Hz")]
    public void FormattingUsesTheSelectedLanguageAndDecimalSeparator(string language, string expected)
    {
        WithCulture(language, () => Assert.AreEqual(expected,
            Text.Format("Reference  A4 = {0:0.#} Hz", 442.5)));
    }

    [TestMethod]
    [DataRow("nl-NL", "Gitaar", "Standaard", "Van laagste naar hoogste snaar")]
    [DataRow("de-DE", "Gitarre", "Standard", "Von der tiefsten zur h\u00f6chsten Saite")]
    [DataRow("fr-FR", "Guitare", "Standard", "De la corde la plus grave \u00e0 la plus aigu\u00eb")]
    [DataRow("es-ES", "Guitarra", "Est\u00e1ndar", "De la cuerda m\u00e1s grave a la m\u00e1s aguda")]
    public void BuiltInPresetDisplayIsLocalizedWithoutChangingItsIdentity(string language, string instrument, string name, string description)
    {
        WithCulture(language, () =>
        {
            var preset = TuningPresets.GuitarStandard;
            Assert.AreEqual(instrument, preset.InstrumentName);
            Assert.AreEqual($"{instrument} \u00b7 {name}", preset.DisplayName);
            Assert.AreEqual(name, preset.LocalizedName);
            Assert.AreEqual(description, preset.LocalizedDescription);
            Assert.AreEqual("guitar-standard", preset.Id);
            Assert.AreEqual("Standard", preset.Name);
            Assert.AreEqual("Lowest string to highest", preset.Description);
            Assert.AreEqual("E2  A2  D3  G3  B3  E4", preset.StringSummary(NoteSpelling.Sharps));
        });
    }

    [TestMethod]
    [DataRow("nl-NL")]
    [DataRow("de-DE")]
    [DataRow("fr-FR")]
    [DataRow("es-ES")]
    public void CustomNamesAndNoteParsingRemainUntranslated(string language)
    {
        WithCulture(language, () =>
        {
            var result = CustomTuningValidator.Validate("Standard", InstrumentKind.Violin, 2, "E2 F#3");
            Assert.IsTrue(result.IsValid);
            Assert.IsNotNull(result.Preset);
            Assert.AreEqual("Standard", result.Preset.LocalizedName);
            Assert.AreEqual(InstrumentKind.Violin, result.Preset.Instrument);
            Assert.AreEqual("E2  F#3", result.Preset.StringSummary(NoteSpelling.Sharps));
            Assert.AreEqual("Custom tuning \u00b7 this session only", result.Preset.Description);
        });
    }

    [TestMethod]
    public void ValidationMessagesAreLocalizedAndKeepTheInvalidInput()
    {
        WithCulture("nl-NL", () =>
        {
            var result = CustomTuningValidator.Validate("", InstrumentKind.Guitar, 1, "H2");
            Assert.IsFalse(result.IsValid);
            Assert.IsNull(result.Preset);
            CollectionAssert.Contains(result.Errors.ToArray(), "Voer een naam in voor de stemming.");
            CollectionAssert.Contains(result.Errors.ToArray(), "'H2' is geen noot met octaaf, zoals E2 of F#3.");
        });
    }

    private static Dictionary<string, string> ReadCatalog(ResourceManager manager, CultureInfo culture)
    {
        var resources = manager.GetResourceSet(culture, true, false);
        Assert.IsNotNull(resources);
        return resources.Cast<DictionaryEntry>().ToDictionary(entry => (string)entry.Key, entry => (string)entry.Value!);
    }

    private static void WithCulture(string language, Action action)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(language);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            action();
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }
}
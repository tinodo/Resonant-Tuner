using System.Globalization;

namespace Resonant.App;

/// <summary>Applies the interface language, or restores the language Windows started the app with.</summary>
internal static class AppCulture
{
    private static CultureInfo _systemUi = CultureInfo.CurrentUICulture;
    private static CultureInfo _system = CultureInfo.CurrentCulture;

    /// <summary>Raised on the UI thread after the culture changed. View models subscribe first, then the window, then the pages.</summary>
    public static event EventHandler? Changed;

    public static void CaptureSystem()
    {
        _systemUi = CultureInfo.CurrentUICulture;
        _system = CultureInfo.CurrentCulture;
    }

    public static void Apply(LanguagePreference language)
    {
        var tag = language switch
        {
            LanguagePreference.English => "en-US",
            LanguagePreference.Dutch => "nl-NL",
            LanguagePreference.German => "de-DE",
            LanguagePreference.French => "fr-FR",
            LanguagePreference.Spanish => "es-ES",
            _ => null,
        };

        var ui = tag is null ? _systemUi : CultureInfo.GetCultureInfo(tag);
        var format = tag is null ? _system : ui;
        CultureInfo.CurrentUICulture = ui;
        CultureInfo.DefaultThreadCurrentUICulture = ui;
        CultureInfo.CurrentCulture = format;
        CultureInfo.DefaultThreadCurrentCulture = format;
        Changed?.Invoke(null, EventArgs.Empty);
    }
}

using System.Globalization;
using System.Resources;

namespace Resonant.Core.Localization;

public static class Text
{
    private static readonly ResourceManager Resources = new("Resonant.Core.Localization.Strings", typeof(Text).Assembly);

    public static string Get(string source) => Get(source, CultureInfo.CurrentUICulture);

    public static string Get(string source, CultureInfo culture) => Resources.GetString(source, culture) ?? source;

    public static string Format(string source, params object?[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, Get(source), arguments);
}
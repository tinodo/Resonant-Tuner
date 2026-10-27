using Microsoft.UI.Xaml;

namespace Resonant.App;

public enum ThemePreference
{
    System,
    Light,
    Dark,
}

public enum LanguagePreference
{
    System,
    English,
    Dutch,
    German,
    French,
    Spanish,
}

/// <summary>Device, theme and window preferences. Held in memory only; they reset when the app closes.</summary>
public sealed class AppPreferences
{
    public event EventHandler? Changed;

    public event EventHandler? LanguageChanged;

    public string? InputDeviceId { get; private set; }

    public int InputChannelIndex { get; private set; }

    public string? OutputDeviceId { get; private set; }

    public ThemePreference Theme { get; private set; } = ThemePreference.Dark;

    public LanguagePreference Language { get; private set; } = LanguagePreference.System;

    public bool AlwaysOnTop { get; private set; }

    public bool CompactMode { get; private set; }

    public ElementTheme ElementTheme => Theme switch
    {
        ThemePreference.Light => ElementTheme.Light,
        ThemePreference.Dark => ElementTheme.Dark,
        _ => ElementTheme.Default,
    };

    public void SetInput(string? deviceId, int channelIndex)
    {
        InputDeviceId = deviceId;
        InputChannelIndex = Math.Max(0, channelIndex);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetOutput(string? deviceId)
    {
        OutputDeviceId = deviceId;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetTheme(ThemePreference theme)
    {
        Theme = theme;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetLanguage(LanguagePreference language)
    {
        if (language == Language)
        {
            return;
        }

        Language = language;
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetAlwaysOnTop(bool value)
    {
        AlwaysOnTop = value;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetCompactMode(bool value)
    {
        CompactMode = value;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

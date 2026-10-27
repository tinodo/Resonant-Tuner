using Resonant.Core.Audio;
using Resonant.Core.Dsp;
using Resonant.Core.Tuning;
using Resonant.App.Audio;
using Resonant.App.ViewModels;

namespace Resonant.App;

/// <summary>Composition root. Wires the audio adapters, tuner controller and view models without a DI framework.</summary>
internal static class AppServices
{
    private static bool _initialized;

    public static SessionState Session { get; } = new();

    public static AppPreferences Preferences { get; } = new();

    public static TunerController Controller { get; private set; } = null!;

    public static TunerViewModel Tuner { get; private set; } = null!;

    public static TuningsViewModel Tunings { get; private set; } = null!;

    public static SettingsViewModel Settings { get; private set; } = null!;

    public static IAudioCapture Capture { get; private set; } = null!;

    public static IReferenceTonePlayer Player { get; private set; } = null!;

    public static void Initialize(IUiDispatcher dispatcher)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        Capture = new AudioGraphCapture();
        Player = new AudioGraphTonePlayer();
        Controller = new TunerController(Capture, Player, new TuningSession(), new YinPitchEstimator());

        Tuner = new TunerViewModel(Controller, Session, Preferences, dispatcher);
        Tunings = new TuningsViewModel(Session);
        Settings = new SettingsViewModel(Session, Preferences, Capture, Player, Controller, dispatcher);

        // Applied on a later dispatcher turn so the language ComboBox is not rebuilt while it is still raising SelectionChanged.
        Preferences.LanguageChanged += (_, _) => dispatcher.Post(() => AppCulture.Apply(Preferences.Language));
    }

    public static async Task ShutdownAsync()
    {
        if (!_initialized)
        {
            return;
        }

        await Controller.DisposeAsync();
        await Capture.DisposeAsync();
        await Player.DisposeAsync();
    }
}

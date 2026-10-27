using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Resonant.Core.Audio;
using Resonant.Core.Tuning;
using Resonant.Core.Localization;

namespace Resonant.App.ViewModels;

public sealed class TunerViewModel : ObservableBase
{
    private const string Placeholder = "\u2013";
    private static readonly double QuietInputLevel = (20.0 * Math.Log10(0.0015) + 72.0) / 72.0 * 100.0;
    private static readonly double HighInputLevel = (20.0 * Math.Log10(0.35) + 72.0) / 72.0 * 100.0;

    private readonly TunerController _controller;
    private readonly SessionState _state;
    private readonly AppPreferences _prefs;
    private readonly IUiDispatcher _ui;

    private TunerSnapshot _snapshot;
    private TunerSnapshot? _pending;
    private readonly DispatcherQueueTimer _refreshTimer;
    private int? _lastTrackedIndex;
    private int? _referenceIndex;
    private TuningPreset _lastNonChromatic = TuningPresets.GuitarStandard;

    private string _noteName = Placeholder;
    private string _octaveText = string.Empty;
    private string _correctionText = Text.Get("Ready");
    private string _correctionGlyph = "\uE720";
    private string _centsText = Placeholder;
    private double _meterCents = double.NaN;
    private bool _isOffScale;
    private bool _isLocked;
    private bool _isPitchStale;
    private string _measuredText = Placeholder;
    private string _targetText = Placeholder;
    private string _confidenceText = Placeholder;
    private string _statusLabel = Text.Get("Stopped");
    private string _statusText = Text.Get("Microphone is off");
    private double _level;
    private bool _isRunning;
    private bool _isStarting;
    private string? _message;
    private InfoBarSeverity _messageSeverity = InfoBarSeverity.Informational;
    private bool _showPermissionHelp;
    private bool _showRetry;
    private string? _outputMessage;
    private string? _directReferenceError;
    private bool _autoString = true;
    private bool _showSpectrum = true;
    private bool _listeningInitialized;

    public TunerViewModel(TunerController controller, SessionState state, AppPreferences prefs, IUiDispatcher ui)
    {
        _controller = controller;
        _state = state;
        _prefs = prefs;
        _ui = ui;
        _snapshot = controller.Snapshot;
        _refreshTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _refreshTimer.Interval = TimeSpan.FromMilliseconds(125);
        _refreshTimer.Tick += (_, _) =>
        {
            if (Interlocked.Exchange(ref _pending, null) is { } latest)
            {
                UpdateFromSnapshot(latest);
            }
        };
        _refreshTimer.Start();

        ToggleCaptureCommand = new AsyncRelayCommand(_ => ToggleCaptureAsync());
        RetryCommand = new AsyncRelayCommand(_ => StartAsync());
        PlayReferenceCommand = new AsyncRelayCommand(_ => ToggleReferenceAsync());
        OpenMicrophoneSettingsCommand = new AsyncRelayCommand(_ => OpenMicrophoneSettingsAsync());
        ToggleCompactCommand = new RelayCommand(_ => _prefs.SetCompactMode(!_prefs.CompactMode));

        _controller.SnapshotChanged += OnSnapshotChanged;
        _state.Changed += (_, _) => ApplyConfiguration();
        _prefs.Changed += (_, _) =>
        {
            Raise(nameof(IsCompact));
            Raise(nameof(CompactGlyph));
        };

        SyncPresets();
        ApplyConfiguration();
        AppCulture.Changed += (_, _) => ApplyLanguage();
    }

    private void ApplyLanguage()
    {
        // Preset names are read when the ComboBox items are created, so the items are recreated.
        Presets.Clear();
        SyncPresets();
        RebuildStrings();
        UpdateFromSnapshot(_snapshot);
        RaiseAll();
    }

    public event EventHandler? StringsRebuilt;

    public event EventHandler? ManualSelectionChanged;

    public ObservableCollection<TuningPreset> Presets { get; } = [];

    public ObservableCollection<StringItemViewModel> Strings { get; } = [];

    public ICommand ToggleCaptureCommand { get; }

    public ICommand RetryCommand { get; }

    public ICommand PlayReferenceCommand { get; }

    public ICommand OpenMicrophoneSettingsCommand { get; }

    public ICommand ToggleCompactCommand { get; }

    public int? ManualIndex => _controller.Session.ManualTargetIndex;

    public bool IsCompact => _prefs.CompactMode;

    public string CompactGlyph => IsCompact ? "\uE740" : "\uE73F";

    public string CaptureGlyph => "\uE720";

    public ReadOnlyMemory<float> Spectrum => _controller.Spectrum;
    public double IdealFrequencyHz
    {
        get
        {
            var targets = _controller.Session.Targets;
            if (ManualIndex is int index && index >= 0 && index < targets.Count)
            {
                return targets[index].Hz;
            }

            var feedback = _snapshot.Feedback;
            return feedback.HasReading && !feedback.IsStale && _snapshot.State == TunerState.Listening
                ? feedback.TargetHz
                : double.NaN;
        }
    }

    public bool ShowSpectrum
    {
        get => _showSpectrum;
        set
        {
            if (Set(ref _showSpectrum, value))
            {
                Raise(nameof(SpectrumVisibility));
            }
        }
    }

    public Visibility SpectrumVisibility => ShowSpectrum ? Visibility.Visible : Visibility.Collapsed;

    public string ReferenceGlyph => _snapshot.IsReferencePlaying ? "\uE769" : "\uE767";

    public bool IsReferencePlaying => _snapshot.IsReferencePlaying;

    public bool IsListeningRequested => _controller.IsListeningRequested;

    public string ReadoutAutomationName => Text.Format("{0} {1}, target {2}, confidence {3}", IsPitchStale ? Text.Get("Last reading") : Text.Get("Measured"), MeasuredText, TargetText, ConfidenceText);

    public TuningPreset? SelectedPreset
    {
        get => _lastNonChromatic;
        set
        {
            if (value is null || ReferenceEquals(value, _lastNonChromatic))
            {
                return;
            }

            _state.SelectPreset(value);
        }
    }

    public bool IsChromatic
    {
        get => _state.Preset is null;
        set
        {
            if (value == IsChromatic)
            {
                return;
            }

            _state.SelectPreset(value ? null : _lastNonChromatic);
        }
    }

    public bool IsPresetEnabled => !IsChromatic;

    public bool IsCapoEnabled => _state.Preset?.SupportsCapo == true;

    public Visibility StringsVisibility => IsChromatic ? Visibility.Collapsed : Visibility.Visible;

    public double CapoValue
    {
        get => _state.Settings.CapoSemitones;
        set
        {
            if (double.IsNaN(value))
            {
                return;
            }

            var capo = (int)Math.Round(Math.Clamp(value, 0, TuningSettings.MaxCapoSemitones));
            if (capo != _state.Settings.CapoSemitones)
            {
                _state.UpdateSettings(_state.Settings with { CapoSemitones = capo });
            }
        }
    }

    public string CalibrationText => $"A4 {_state.Settings.A4Hz.ToString("0.#", CultureInfo.CurrentCulture)} Hz";

    public bool AutoString
    {
        get => _autoString;
        set
        {
            if (value == _autoString)
            {
                return;
            }

            if (value)
            {
                SelectString(null);
            }
            else
            {
                SelectString(_lastTrackedIndex ?? 0);
            }
        }
    }

    public string NoteName
    {
        get => _noteName;
        private set => Set(ref _noteName, value);
    }

    public string OctaveText
    {
        get => _octaveText;
        private set => Set(ref _octaveText, value);
    }

    public string CorrectionText
    {
        get => _correctionText;
        private set => Set(ref _correctionText, value);
    }

    public string CorrectionGlyph
    {
        get => _correctionGlyph;
        private set => Set(ref _correctionGlyph, value);
    }

    public string CentsText
    {
        get => _centsText;
        private set => Set(ref _centsText, value);
    }

    public double MeterCents
    {
        get => _meterCents;
        private set
        {
            if (!(double.IsNaN(value) && double.IsNaN(_meterCents)) && Set(ref _meterCents, value))
            {
                Raise(nameof(PitchReadoutVisibility));
            }
        }
    }

    public Visibility PitchReadoutVisibility => double.IsFinite(MeterCents) ? Visibility.Visible : Visibility.Collapsed;

    public bool IsPitchStale
    {
        get => _isPitchStale;
        private set
        {
            if (Set(ref _isPitchStale, value))
            {
                Raise(nameof(ReadoutAutomationName));
            }
        }
    }

    public bool IsOffScale
    {
        get => _isOffScale;
        private set => Set(ref _isOffScale, value);
    }

    public bool IsLocked
    {
        get => _isLocked;
        private set => Set(ref _isLocked, value);
    }

    public string MeasuredText
    {
        get => _measuredText;
        private set
        {
            if (Set(ref _measuredText, value))
            {
                Raise(nameof(ReadoutAutomationName));
            }
        }
    }

    public string TargetText
    {
        get => _targetText;
        private set
        {
            if (Set(ref _targetText, value))
            {
                Raise(nameof(ReadoutAutomationName));
            }
        }
    }

    public string ConfidenceText
    {
        get => _confidenceText;
        private set
        {
            if (Set(ref _confidenceText, value))
            {
                Raise(nameof(ReadoutAutomationName));
            }
        }
    }

    public string StatusLabel
    {
        get => _statusLabel;
        private set => Set(ref _statusLabel, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => Set(ref _statusText, value);
    }

    /// <summary>Input level, 0-100, from the RMS of the latest analysis window (-72 dBFS to 0 dBFS).</summary>
    public double Level
    {
        get => _level;
        private set => Set(ref _level, value);
    }

    public bool IsInputQuiet => IsRunning && _snapshot.Reading is { IsClipping: false } && Level < QuietInputLevel;

    public bool IsInputNormal => IsRunning && _snapshot.Reading is { IsClipping: false }
        && Level >= QuietInputLevel && Level < HighInputLevel;

    public bool IsInputHigh => IsRunning && _snapshot.Reading is { IsClipping: false } && Level >= HighInputLevel;

    public bool IsInputClipping => IsRunning && _snapshot.Reading is { IsClipping: true };

    public string InputLevelDescription => IsInputClipping
        ? Text.Get("Clipping detected. Lower the input gain or move away from the microphone.")
        : IsInputHigh ? Text.Get("High input level. Leave more headroom by lowering the gain or moving back.")
        : IsInputQuiet ? Text.Get("Quiet input. A clean note may still be detected; move closer if detection is unreliable.")
        : IsInputNormal ? Text.Get("Normal input level. Pitch confidence is shown separately.")
        : IsRunning ? Text.Get("Waiting for microphone input.")
        : Text.Get("Microphone is off.");

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (Set(ref _isRunning, value))
            {
                Raise(nameof(StartStopText));
                Raise(nameof(CaptureButtonText));
            }
        }
    }

    public Visibility StartingVisibility => _isStarting ? Visibility.Visible : Visibility.Collapsed;

    public string StartStopText => IsListeningRequested ? Text.Get("Pause listening") : Text.Get("Resume listening");

    public string CaptureButtonText => IsListeningRequested ? Text.Get("Pause") : Text.Get("Resume");

    public string ReferenceButtonText
    {
        get
        {
            if (_snapshot.IsReferencePlaying)
            {
                return Text.Get("Stop reference tone");
            }

            var target = ReferenceTarget();
            return Text.Format("Reference {0}", target.Note.ToString(_state.Settings.Spelling));
        }
    }

    public bool IsMessageOpen => !string.IsNullOrEmpty(_message);

    public string Message => _message ?? string.Empty;

    public InfoBarSeverity MessageSeverity
    {
        get => _messageSeverity;
        private set => Set(ref _messageSeverity, value);
    }

    public Visibility PermissionHelpVisibility => _showPermissionHelp ? Visibility.Visible : Visibility.Collapsed;

    public Visibility RetryVisibility => _showRetry ? Visibility.Visible : Visibility.Collapsed;

    public bool IsOutputMessageOpen => !string.IsNullOrEmpty(_outputMessage);

    public string OutputMessage => _outputMessage ?? string.Empty;

    public void SelectString(int? index)
    {
        _controller.SelectString(index);
        if (index is not null)
        {
            _referenceIndex = index;
        }

        ClearPitchReadout();
        _autoString = index is null;
        Raise(nameof(AutoString));
        UpdateStringStates();
        ManualSelectionChanged?.Invoke(this, EventArgs.Empty);
        if (index is not null && _controller.IsReferencePlaying)
        {
            _ = RetuneReferenceAsync();
        }
    }

    public Task ToggleCaptureFromKeyboardAsync() => ToggleCaptureAsync();

    public Task InitializeListeningAsync()
    {
        if (_listeningInitialized)
        {
            return Task.CompletedTask;
        }

        _listeningInitialized = true;
        return StartAsync();
    }

    private async Task ToggleCaptureAsync()
    {
        if (_controller.IsListeningRequested)
        {
            await _controller.StopAsync();
            return;
        }

        await StartAsync();
    }

    private async Task StartAsync()
    {
        try
        {
            _controller.ConfigureTuning(_state.Preset, _state.Settings);
            await _controller.StartAsync(new CaptureRequest(_prefs.InputDeviceId, _prefs.InputChannelIndex));
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or ObjectDisposedException)
        {
            SetMessage(Text.Format("The microphone could not be started: {0}", ex.Message), InfoBarSeverity.Error, showPermissionHelp: false);
        }
    }

    private async Task ToggleReferenceAsync()
    {
        _directReferenceError = null;
        try
        {
            if (_controller.IsReferencePlaying)
            {
                await _controller.StopReferenceAsync();
                return;
            }

            _referenceIndex = _controller.Session.ManualTargetIndex ?? _referenceIndex ?? _lastTrackedIndex;
            await _controller.PlayReferenceAsync(ReferenceTarget(), _prefs.OutputDeviceId);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            ReportReferenceException(exception);
        }
    }

    private async Task RetuneReferenceAsync()
    {
        _directReferenceError = null;
        try
        {
            await _controller.RetuneReferenceAsync(ReferenceTarget(), _prefs.OutputDeviceId);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            ReportReferenceException(exception);
        }
    }

    private void ReportReferenceException(Exception exception)
    {
        _directReferenceError = Text.Format("Reference tone unavailable: {0}", exception.Message);
        SetOutputMessage(_directReferenceError);
    }

    private static async Task OpenMicrophoneSettingsAsync()
    {
        await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:privacy-microphone"));
    }

    private TuningTarget ReferenceTarget()
    {
        var targets = _controller.Session.Targets;
        if (targets.Count == 0)
        {
            var a4 = new Note(69);
            return new TuningTarget(1, a4, _state.Settings.A4Hz);
        }

        // A tracked string can drift (the mic also hears the reference tone), so an explicit choice wins over tracking.
        var index = _controller.Session.ManualTargetIndex ?? _referenceIndex ?? _lastTrackedIndex ?? 0;
        return targets[Math.Clamp(index, 0, targets.Count - 1)];
    }

    private void ApplyConfiguration()
    {
        var preset = _state.Preset;
        if (preset is not null)
        {
            _lastNonChromatic = preset;
        }

        SyncPresets();
        _controller.ConfigureTuning(preset, _state.Settings);
        ClearPitchReadout();
        _lastTrackedIndex = null;
        _referenceIndex = null;
        _autoString = _controller.Session.ManualTargetIndex is null;
        RebuildStrings();
        UpdateFromSnapshot(_controller.Snapshot);
        RaiseAll();
    }

    private void SyncPresets()
    {
        var wanted = _state.AllPresets.ToList();
        for (var i = Presets.Count - 1; i >= 0; i--)
        {
            if (!wanted.Contains(Presets[i]))
            {
                Presets.RemoveAt(i);
            }
        }

        foreach (var preset in wanted)
        {
            if (!Presets.Contains(preset))
            {
                Presets.Add(preset);
            }
        }
    }

    private void RebuildStrings()
    {
        Strings.Clear();
        var spelling = _state.Settings.Spelling;
        var targets = _controller.Session.Targets;
        for (var i = 0; i < targets.Count; i++)
        {
            var t = targets[i];
            Strings.Add(new StringItemViewModel(
                i,
                Text.Format("STRING {0}", t.StringNumber),
                t.Note.ToString(spelling),
                $"{t.Hz.ToString("0.00", CultureInfo.CurrentCulture)} Hz"));
        }

        UpdateStringStates();
        StringsRebuilt?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateStringStates()
    {
        var manual = _controller.Session.ManualTargetIndex;
        foreach (var item in Strings)
        {
            item.IsLocked = manual == item.Index;
            item.IsTracked = manual is null && _lastTrackedIndex == item.Index;
        }
    }

    private void OnSnapshotChanged(object? sender, TunerSnapshot snapshot)
    {
        Interlocked.Exchange(ref _pending, snapshot);
    }

    private void ClearPitchReadout()
    {
        NoteName = string.Empty;
        OctaveText = string.Empty;
        CentsText = string.Empty;
        MeterCents = double.NaN;
        MeasuredText = string.Empty;
        TargetText = Placeholder;
        ConfidenceText = Placeholder;
        IsPitchStale = false;
        IsLocked = false;
        IsOffScale = false;
    }

    private void UpdateFromSnapshot(TunerSnapshot s)
    {
        _snapshot = s;
        var fb = s.Feedback;
        var spelling = _state.Settings.Spelling;
        var hasCurrentReading = fb.HasReading && !fb.IsStale && s.State == TunerState.Listening;

        if (hasCurrentReading && fb.Note is { } note)
        {
            NoteName = note.Name(spelling);
            OctaveText = note.Octave.ToString(CultureInfo.CurrentCulture);
            CentsText = fb.Cents.ToString("+0;-0;0", CultureInfo.CurrentCulture);
            MeterCents = fb.Cents;
            MeasuredText = $"{fb.MeasuredHz.ToString("0.0", CultureInfo.CurrentCulture)} Hz";
            TargetText = $"{fb.TargetHz.ToString("0.0", CultureInfo.CurrentCulture)} Hz";
            ConfidenceText = fb.Confidence >= 0.93 || (ConfidenceText == Text.Get("Stable") && fb.Confidence >= 0.90)
                ? Text.Get("Stable")
                : fb.Confidence >= 0.88 || (ConfidenceText == Text.Get("Good") && fb.Confidence >= 0.85) ? Text.Get("Good") : Text.Get("Fair");
            _lastTrackedIndex = fb.Target is { } t ? _controller.Session.Targets.ToList().FindIndex(x => x.StringNumber == t.StringNumber) : null;
            if (_lastTrackedIndex < 0)
            {
                _lastTrackedIndex = null;
            }
        }
        else
        {
            ConfidenceText = Placeholder;
        }

        IsPitchStale = !hasCurrentReading && double.IsFinite(MeterCents);
        IsOffScale = hasCurrentReading && fb.IsOffScale;
        IsLocked = hasCurrentReading && fb.IsLocked;

        var (text, glyph) = DescribeCorrection(s);
        CorrectionText = IsPitchStale ? Text.Format("Last reading \u00B7 {0}", text) : text;
        CorrectionGlyph = glyph;

        StatusLabel = s.State switch
        {
            TunerState.Stopped => Text.Get("Paused"),
            TunerState.RequestingPermission => Text.Get("Requesting microphone"),
            TunerState.Starting => Text.Get("Starting microphone"),
            TunerState.Listening => Text.Get("Listening"),
            TunerState.NoSignal => Text.Get("No signal"),
            TunerState.UncertainPitch => Text.Get("Pitch unclear"),
            TunerState.Clipping => Text.Get("Input clipping"),
            TunerState.DeviceLost => Text.Get("Microphone lost"),
            _ => Text.Get("Error"),
        };

        IsRunning = s.State is TunerState.RequestingPermission or TunerState.Starting or TunerState.Listening
            or TunerState.NoSignal or TunerState.UncertainPitch or TunerState.Clipping;
        _isStarting = s.State is TunerState.Starting or TunerState.RequestingPermission;
        Raise(nameof(StartingVisibility));

        StatusText = s.State switch
        {
            TunerState.Stopped => Text.Get("Listening is paused"),
            TunerState.Clipping => Text.Get("Input is clipping. Move back from the microphone or lower the input level."),
            TunerState.NoSignal => Text.Get("No signal. Pluck one string and let it ring."),
            TunerState.UncertainPitch => Text.Get("Pitch is unclear. Mute the other strings and let one ring."),
            _ => _prefs.InputDeviceId is null ? Text.Get("Input: system default") : Text.Get("Input: selected device"),
        };

        if (s.Reading is { } reading)
        {
            var db = reading.Rms > 1e-6 ? 20.0 * Math.Log10(reading.Rms) : -120.0;
            Level += (Math.Clamp((db + 72.0) / 72.0 * 100.0, 0.0, 100.0) - Level) * 0.35;
        }
        else
        {
            Level = 0;
        }

        Raise(nameof(IsInputQuiet));
        Raise(nameof(IsInputNormal));
        Raise(nameof(IsInputHigh));
        Raise(nameof(IsInputClipping));
        Raise(nameof(InputLevelDescription));

        UpdateMessage(s);
        UpdateOutputMessage(s);
        UpdateStringStates();
        Raise(nameof(ReferenceButtonText));
        Raise(nameof(ReferenceGlyph));
        Raise(nameof(IsReferencePlaying));
        Raise(nameof(IsListeningRequested));
        Raise(nameof(StartStopText));
        Raise(nameof(CaptureButtonText));
    }

    private static (string Text, string Glyph) DescribeCorrection(TunerSnapshot s)
    {
        var fb = s.Feedback;
        if (fb.HasReading && !fb.IsStale && s.State == TunerState.Listening)
        {
            return fb.Correction switch
            {
                Correction.TuneUp => (Text.Get("Tune up"), "\uE74A"),
                Correction.TuneDown => (Text.Get("Tune down"), "\uE74B"),
                Correction.InTune => (Text.Get("In tune"), "\uE73E"),
                _ => (Text.Get("Hold steady"), "\uE769"),
            };
        }

        return s.State switch
        {
            TunerState.Stopped => (Text.Get("Paused"), "\uE720"),
            TunerState.RequestingPermission or TunerState.Starting => (Text.Get("Starting microphone"), "\uE895"),
            TunerState.NoSignal => (Text.Get("No signal"), "\uE720"),
            TunerState.UncertainPitch => (Text.Get("Pitch unclear"), "\uE897"),
            TunerState.Clipping => (Text.Get("Input too loud"), "\uE7BA"),
            TunerState.DeviceLost => (Text.Get("Microphone lost"), "\uE7BA"),
            TunerState.Error => (Text.Get("Microphone unavailable"), "\uE7BA"),
            _ => (Text.Get("Pluck one string"), "\uE720"),
        };
    }

    private void UpdateMessage(TunerSnapshot s)
    {
        switch (s.State)
        {
            case TunerState.Error:
            case TunerState.DeviceLost:
                SetMessage(
                    s.Message ?? Text.Get("The microphone is unavailable."),
                    InfoBarSeverity.Error,
                    s.Failure == CaptureFailureKind.PermissionDenied);
                break;
            case TunerState.Clipping:
                SetMessage(Text.Get("The input is clipping, so the pitch may be wrong. Lower the input level or move back."), InfoBarSeverity.Warning, false);
                break;
            default:
                if (!string.IsNullOrEmpty(s.Message))
                {
                    SetMessage(s.Message, InfoBarSeverity.Informational, false);
                }
                else
                {
                    SetMessage(null, InfoBarSeverity.Informational, false);
                }

                break;
        }
    }

    private void UpdateOutputMessage(TunerSnapshot s)
    {
        if (s.IsReferencePlaying)
        {
            _directReferenceError = null;
        }

        SetOutputMessage(s.ReferenceMessage ?? _directReferenceError);
    }

    private void SetOutputMessage(string? message)
    {
        if (_outputMessage == message)
        {
            return;
        }

        _outputMessage = message;
        Raise(nameof(OutputMessage));
        Raise(nameof(IsOutputMessageOpen));
    }

    private void SetMessage(string? message, InfoBarSeverity severity, bool showPermissionHelp)
    {
        _message = message;
        _showPermissionHelp = showPermissionHelp;
        _showRetry = severity == InfoBarSeverity.Error && !string.IsNullOrEmpty(message);
        MessageSeverity = severity;
        Raise(nameof(Message));
        Raise(nameof(IsMessageOpen));
        Raise(nameof(PermissionHelpVisibility));
        Raise(nameof(RetryVisibility));
    }
}

using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.UI.Xaml;
using Resonant.Core.Audio;
using Resonant.Core.Tuning;
using Resonant.Core.Localization;

namespace Resonant.App.ViewModels;

public sealed record DeviceChoice(string? Id, string Name);

public sealed class SettingsViewModel : ObservableBase
{
    private readonly SessionState _state;
    private readonly AppPreferences _prefs;
    private readonly IAudioCapture _capture;
    private readonly IReferenceTonePlayer _player;
    private readonly TunerController _controller;
    private readonly IUiDispatcher _ui;

    private DeviceChoice? _selectedInput;
    private DeviceChoice? _selectedOutput;
    private bool _permissionDenied;
    private string _deviceStatus = string.Empty;

    public SettingsViewModel(
        SessionState state,
        AppPreferences prefs,
        IAudioCapture capture,
        IReferenceTonePlayer player,
        TunerController controller,
        IUiDispatcher ui)
    {
        _state = state;
        _prefs = prefs;
        _capture = capture;
        _player = player;
        _controller = controller;
        _ui = ui;

        InputDevices.Add(new DeviceChoice(null, Text.Get("System default input")));
        OutputDevices.Add(new DeviceChoice(null, Text.Get("System default output")));
        _selectedInput = InputDevices[0];
        _selectedOutput = OutputDevices[0];

        RefreshDevicesCommand = new AsyncRelayCommand(_ => RefreshDevicesAsync());
        RetryCommand = new AsyncRelayCommand(_ => RetryAsync());
        OpenMicrophoneSettingsCommand = new AsyncRelayCommand(_ => Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:privacy-microphone")).AsTask());
        ToggleCompactCommand = new RelayCommand(_ => _prefs.SetCompactMode(!_prefs.CompactMode));

        _prefs.Changed += (_, _) =>
        {
            Raise(nameof(IsCompact));
            Raise(nameof(CompactButtonText));
        };
        _controller.SnapshotChanged += (_, snapshot) => _ui.Post(() => OnSnapshot(snapshot));
        AppCulture.Changed += (_, _) => ApplyLanguage();
    }

    public ObservableCollection<DeviceChoice> InputDevices { get; } = [];

    public ObservableCollection<DeviceChoice> OutputDevices { get; } = [];

    public IReadOnlyList<string> ChannelChoices { get; private set; } = BuildChannelChoices();

    public IReadOnlyList<string> SpellingChoices { get; private set; } = BuildSpellingChoices();

    public IReadOnlyList<string> ThemeChoices { get; private set; } = BuildThemeChoices();

    public IReadOnlyList<string> LanguageChoices { get; private set; } = BuildLanguageChoices();

    private static string[] BuildChannelChoices() =>
        [Text.Get("Channel 1 (first)"), Text.Get("Channel 2"), Text.Get("Channel 3"), Text.Get("Channel 4"), Text.Get("Channel 5"), Text.Get("Channel 6"), Text.Get("Channel 7"), Text.Get("Channel 8")];

    private static string[] BuildSpellingChoices() => [Text.Get("Sharps (C#)"), Text.Get("Flats (Db)")];

    private static string[] BuildThemeChoices() => [Text.Get("System"), Text.Get("Light"), Text.Get("Dark")];

    private static string[] BuildLanguageChoices() => [Text.Get("Follow Windows"), "English", "Nederlands", "Deutsch", "Fran\u00E7ais", "Espa\u00F1ol"];

    private void ApplyLanguage()
    {
        ChannelChoices = BuildChannelChoices();
        SpellingChoices = BuildSpellingChoices();
        ThemeChoices = BuildThemeChoices();
        LanguageChoices = BuildLanguageChoices();
        RaiseAll();
        _ = RefreshDevicesAsync();
    }

    /// <summary>Re-pushes the selections after the pages replaced their item lists, which clears the ComboBox selections.</summary>
    public void RefreshSelections()
    {
        Raise(nameof(ChannelIndex));
        Raise(nameof(SpellingIndex));
        Raise(nameof(ThemeIndex));
        Raise(nameof(LanguageIndex));
        Raise(nameof(SelectedInput));
        Raise(nameof(SelectedOutput));
    }

    public ICommand RefreshDevicesCommand { get; }

    public ICommand RetryCommand { get; }

    public ICommand OpenMicrophoneSettingsCommand { get; }

    public ICommand ToggleCompactCommand { get; }

    public DeviceChoice? SelectedInput
    {
        get => _selectedInput;
        set
        {
            if (value is null || !Set(ref _selectedInput, value))
            {
                return;
            }

            _prefs.SetInput(value.Id, _prefs.InputChannelIndex);
            _ = ApplyDeviceChangeAsync(_controller.ChangeInputAsync(new CaptureRequest(value.Id, _prefs.InputChannelIndex)));
        }
    }

    public DeviceChoice? SelectedOutput
    {
        get => _selectedOutput;
        set
        {
            if (value is null || !Set(ref _selectedOutput, value))
            {
                return;
            }

            _prefs.SetOutput(value.Id);
            _ = ApplyDeviceChangeAsync(_controller.ChangeOutputAsync(value.Id));
        }
    }

    public int ChannelIndex
    {
        get => _prefs.InputChannelIndex;
        set
        {
            if (value < 0 || value == _prefs.InputChannelIndex)
            {
                return;
            }

            _prefs.SetInput(_prefs.InputDeviceId, value);
            _ = ApplyDeviceChangeAsync(_controller.ChangeInputAsync(new CaptureRequest(_prefs.InputDeviceId, value)));
            Raise();
        }
    }

    public double A4Value
    {
        get => _state.Settings.A4Hz;
        set
        {
            if (double.IsNaN(value))
            {
                return;
            }

            _state.UpdateSettings(_state.Settings with { A4Hz = value });
            Raise();
        }
    }

    public double ToleranceValue
    {
        get => _state.Settings.ToleranceCents;
        set
        {
            if (double.IsNaN(value))
            {
                return;
            }

            _state.UpdateSettings(_state.Settings with { ToleranceCents = value });
            Raise();
        }
    }

    public int SpellingIndex
    {
        get => _state.Settings.Spelling == NoteSpelling.Sharps ? 0 : 1;
        set
        {
            if (value < 0)
            {
                return;
            }

            _state.UpdateSettings(_state.Settings with { Spelling = value == 0 ? NoteSpelling.Sharps : NoteSpelling.Flats });
            Raise();
        }
    }

    public int ThemeIndex
    {
        get => (int)_prefs.Theme;
        set
        {
            if (value < 0 || value == (int)_prefs.Theme)
            {
                return;
            }

            _prefs.SetTheme((ThemePreference)value);
            Raise();
        }
    }

    public int LanguageIndex
    {
        get => (int)_prefs.Language;
        set
        {
            if (value < 0 || value == (int)_prefs.Language)
            {
                return;
            }

            _prefs.SetLanguage((LanguagePreference)value);
            Raise();
        }
    }

    public bool AlwaysOnTop
    {
        get => _prefs.AlwaysOnTop;
        set
        {
            if (value != _prefs.AlwaysOnTop)
            {
                _prefs.SetAlwaysOnTop(value);
                Raise();
            }
        }
    }

    public bool IsCompact => _prefs.CompactMode;

    public string CompactButtonText => _prefs.CompactMode ? Text.Get("Restore") : Text.Get("Open");

    public Visibility PermissionAlertVisibility => _permissionDenied ? Visibility.Visible : Visibility.Collapsed;

    public string DeviceStatus
    {
        get => _deviceStatus;
        private set => Set(ref _deviceStatus, value);
    }

    private readonly object _deviceRefreshGate = new();
    private Task _deviceRefresh = Task.CompletedTask;

    public Task RefreshDevicesAsync()
    {
        lock (_deviceRefreshGate)
        {
            if (_deviceRefresh.IsCompleted)
                _deviceRefresh = RefreshDevicesCoreAsync();
            return _deviceRefresh;
        }
    }

    private async Task RefreshDevicesCoreAsync()
    {
        try
        {
            var inputs = await _capture.GetInputDevicesAsync();
            var outputs = await _player.GetOutputDevicesAsync();
            await UpdateDevicesOnUiAsync(() =>
            {
                Replace(InputDevices, Text.Get("System default input"), inputs, _prefs.InputDeviceId, choice => _selectedInput = choice, nameof(SelectedInput));
                Replace(OutputDevices, Text.Get("System default output"), outputs, _prefs.OutputDeviceId, choice => _selectedOutput = choice, nameof(SelectedOutput));
                DeviceStatus = Text.Format("{0} input and {1} output devices found.", inputs.Count, outputs.Count);
            });
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            await UpdateDevicesOnUiAsync(() =>
            {
                DeviceStatus = Text.Get("Audio devices could not be listed: ") + ex.Message;
            });
        }
    }

    private Task UpdateDevicesOnUiAsync(Action update)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            _ui.Post(() =>
            {
                try
                {
                    update();
                    completion.SetResult();
                }
                catch (Exception ex)
                {
                    completion.SetException(ex);
                }
            });
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }
        return completion.Task;
    }

    private async Task ApplyDeviceChangeAsync(Task change)
    {
        try
        {
            await change;
            _ui.Post(() => DeviceStatus = _controller.Snapshot.Message ?? _controller.Snapshot.ReferenceMessage ?? Text.Get("Audio device selection updated."));
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            _ui.Post(() => DeviceStatus = Text.Get("Audio device could not be changed: ") + exception.Message);
        }
    }

    private async Task RetryAsync()
    {
        await _controller.StartAsync(new CaptureRequest(_prefs.InputDeviceId, _prefs.InputChannelIndex));
    }

    private void OnSnapshot(TunerSnapshot snapshot)
    {
        var denied = snapshot.State == TunerState.Error && snapshot.Failure == CaptureFailureKind.PermissionDenied;
        if (denied != _permissionDenied)
        {
            _permissionDenied = denied;
            Raise(nameof(PermissionAlertVisibility));
        }
    }

    private void Replace(
        ObservableCollection<DeviceChoice> target,
        string defaultName,
        IReadOnlyList<AudioDeviceInfo> devices,
        string? selectedId,
        Action<DeviceChoice> assignSelected,
        string propertyName)
    {
        target.Clear();
        var defaultChoice = new DeviceChoice(null, defaultName);
        target.Add(defaultChoice);
        foreach (var device in devices)
        {
            target.Add(new DeviceChoice(device.Id, device.IsDefault ? Text.Format("{0} (default)", device.Name) : device.Name));
        }

        var selected = target.FirstOrDefault(c => c.Id == selectedId) ?? defaultChoice;
        assignSelected(selected);
        Raise(propertyName);
    }
}

using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.UI.Xaml;
using Resonant.Core.Tuning;
using Resonant.Core.Localization;

namespace Resonant.App.ViewModels;

public sealed class PresetItemViewModel
{
    public PresetItemViewModel(TuningPreset preset, NoteSpelling spelling)
    {
        Preset = preset;
        Title = preset.LocalizedName;
        Subtitle = Text.Format("{0} \u00B7 {1} strings \u00B7 {2}", preset.InstrumentName, preset.StringCount, preset.LocalizedDescription);
        Notes = preset.StringSummary(spelling);
        IconGlyph = "\uE8D6";
        AutomationName = Text.Format("{0} {1}, {2} strings: {3}", preset.InstrumentName, preset.LocalizedName, preset.StringCount, Notes);
    }

    public TuningPreset Preset { get; }

    public string Title { get; }

    public string Subtitle { get; }

    public string Notes { get; }

    public string IconGlyph { get; }

    public string AutomationName { get; }
}

public sealed class InstrumentChoice : ObservableBase
{
    private readonly string _key;

    public InstrumentChoice(InstrumentKind kind, string key)
    {
        Kind = kind;
        _key = key;
    }

    public InstrumentKind Kind { get; }

    public string Name => Text.Get(_key);

    public void Refresh() => Raise(nameof(Name));
}

public sealed class TuningsViewModel : ObservableBase
{
    public static string DefaultEditorName => Text.Get("My open tuning");
    public const string DefaultEditorTargets = "E2 A2 D3 G3 B3 E4";

    private readonly SessionState _state;

    private string _filter = "all";
    private string _editorName = DefaultEditorName;
    private string _shownDefaultName = DefaultEditorName;
    private InstrumentChoice _editorInstrument;
    private double _editorStringCount = 6;
    private string _editorTargets = DefaultEditorTargets;
    private string? _editorErrors;
    private int _customCount;
    private NoteSpelling _spelling;

    public TuningsViewModel(SessionState state)
    {
        _state = state;
        _editorInstrument = InstrumentChoices[0];
        _state.Changed += (_, _) =>
        {
            var spelling = _state.Settings.Spelling;
            if (_state.CustomTunings.Count != _customCount || spelling != _spelling)
            {
                RebuildPresets();
                RebuildCustom();
            }
            else
            {
                PresetsRebuilt?.Invoke(this, EventArgs.Empty);
            }

            Raise(nameof(CalibrationText));
            Raise(nameof(NoCustomVisibility));
        };

        UseForSessionCommand = new RelayCommand(_ => UseForSession());
        RemoveCustomCommand = new RelayCommand(RemoveCustom);
        RebuildPresets();
        RebuildCustom();
        AppCulture.Changed += (_, _) => ApplyLanguage();
    }

    private void ApplyLanguage()
    {
        // An untouched default name follows the language, so a language change is not an unsaved edit.
        var nameWasDefault = string.Equals(_editorName, _shownDefaultName, StringComparison.Ordinal);
        foreach (var choice in InstrumentChoices)
        {
            choice.Refresh();
        }

        _shownDefaultName = DefaultEditorName;
        if (nameWasDefault)
        {
            _editorName = _shownDefaultName;
        }

        RebuildPresets();
        RebuildCustom();
        RaiseAll();
    }

    public event EventHandler? PresetsRebuilt;

    public ObservableCollection<PresetItemViewModel> Presets { get; } = [];

    public ObservableCollection<PresetItemViewModel> CustomTunings { get; } = [];

    public IReadOnlyList<InstrumentChoice> InstrumentChoices { get; } =
    [
        new(InstrumentKind.Guitar, "Guitar"),
        new(InstrumentKind.Ukulele, "Ukulele"),
        new(InstrumentKind.Bass, "Bass"),
        new(InstrumentKind.Violin, "Violin"),
        new(InstrumentKind.Mandolin, "Mandolin"),
    ];

    public ICommand UseForSessionCommand { get; }

    public ICommand RemoveCustomCommand { get; }

    public string CalibrationText => Text.Format("Reference  A4 = {0:0.#} Hz", _state.Settings.A4Hz);

    public Visibility NoCustomVisibility => _state.CustomTunings.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility CustomListVisibility => _state.CustomTunings.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

    public TuningPreset? SelectedPreset => _state.Preset;

    public string Filter
    {
        get => _filter;
        set
        {
            if (Set(ref _filter, value))
            {
                RebuildPresets();
            }
        }
    }

    public string EditorName
    {
        get => _editorName;
        set
        {
            if (Set(ref _editorName, value))
            {
                Raise(nameof(HasUnsavedEdits));
            }
        }
    }

    public InstrumentChoice EditorInstrument
    {
        get => _editorInstrument;
        set
        {
            if (value is not null && Set(ref _editorInstrument, value))
            {
                Raise(nameof(HasUnsavedEdits));
            }
        }
    }

    public double EditorStringCount
    {
        get => _editorStringCount;
        set
        {
            if (Set(ref _editorStringCount, value))
            {
                Raise(nameof(HasUnsavedEdits));
            }
        }
    }

    public string EditorTargets
    {
        get => _editorTargets;
        set
        {
            if (Set(ref _editorTargets, value))
            {
                Raise(nameof(HasUnsavedEdits));
            }
        }
    }

    public bool HasUnsavedEdits =>
        !string.Equals(_editorName, DefaultEditorName, StringComparison.Ordinal) ||
        !ReferenceEquals(_editorInstrument, InstrumentChoices[0]) ||
        (int)Math.Round(double.IsNaN(_editorStringCount) ? 0 : _editorStringCount) != 6 ||
        !string.Equals(_editorTargets, DefaultEditorTargets, StringComparison.Ordinal);

    public string EditorErrors
    {
        get => _editorErrors ?? string.Empty;
        private set
        {
            if (Set(ref _editorErrors, value))
            {
                Raise(nameof(EditorErrorsOpen));
            }
        }
    }

    public bool EditorErrorsOpen => !string.IsNullOrEmpty(_editorErrors);

    public void DiscardEdits()
    {
        EditorName = DefaultEditorName;
        EditorInstrument = InstrumentChoices[0];
        EditorStringCount = 6;
        EditorTargets = DefaultEditorTargets;
        EditorErrors = string.Empty;
    }

    public void SelectPreset(TuningPreset preset)
    {
        if (!ReferenceEquals(_state.Preset, preset))
        {
            _state.SelectPreset(preset);
        }
    }

    private void UseForSession()
    {
        var kind = _editorInstrument.Kind;
        var count = double.IsNaN(_editorStringCount) ? 0 : (int)Math.Round(_editorStringCount);
        var result = CustomTuningValidator.Validate(_editorName, kind, count, _editorTargets);
        if (!result.IsValid)
        {
            EditorErrors = string.Join(Environment.NewLine, result.Errors);
            return;
        }

        EditorErrors = string.Empty;
        _state.AddCustomTuning(result.Preset!);
        DiscardEdits();
    }

    private void RemoveCustom(object? parameter)
    {
        if (parameter is PresetItemViewModel item)
        {
            _state.RemoveCustomTuning(item.Preset);
        }
    }

    private void RebuildPresets()
    {
        Presets.Clear();
        var spelling = _state.Settings.Spelling;
        _spelling = spelling;
        foreach (var preset in _state.AllPresets)
        {
            if (Matches(preset))
            {
                Presets.Add(new PresetItemViewModel(preset, spelling));
            }
        }

        PresetsRebuilt?.Invoke(this, EventArgs.Empty);
    }

    private void RebuildCustom()
    {
        CustomTunings.Clear();
        _customCount = _state.CustomTunings.Count;
        var spelling = _state.Settings.Spelling;
        foreach (var preset in _state.CustomTunings)
        {
            CustomTunings.Add(new PresetItemViewModel(preset, spelling));
        }

        Raise(nameof(CustomListVisibility));
    }

    private bool Matches(TuningPreset preset) => _filter switch
    {
        "guitar" => preset.Instrument == InstrumentKind.Guitar,
        "ukulele" => preset.Instrument == InstrumentKind.Ukulele,
        "bass" => preset.Instrument == InstrumentKind.Bass,
        "bowed" => preset.Instrument is InstrumentKind.Violin or InstrumentKind.Mandolin,
        _ => true,
    };
}

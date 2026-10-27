using Microsoft.UI.Xaml;
using Resonant.Core.Localization;

namespace Resonant.App.ViewModels;

/// <summary>One string target tile on the tuner page.</summary>
public sealed class StringItemViewModel : ObservableBase
{
    private bool _isTracked;
    private bool _isLocked;

    public StringItemViewModel(int index, string stringLabel, string noteText, string frequencyText)
    {
        Index = index;
        StringLabel = stringLabel;
        NoteText = noteText;
        FrequencyText = frequencyText;
    }

    public int Index { get; }

    public string StringLabel { get; }

    public string NoteText { get; }

    public string FrequencyText { get; }

    public bool IsTracked
    {
        get => _isTracked;
        set
        {
            if (Set(ref _isTracked, value))
            {
                Raise(nameof(DetailText));
                Raise(nameof(AutomationName));
                Raise(nameof(TrackingVisibility));
            }
        }
    }

    public bool IsLocked
    {
        get => _isLocked;
        set
        {
            if (Set(ref _isLocked, value))
            {
                Raise(nameof(DetailText));
                Raise(nameof(AutomationName));
                Raise(nameof(ActionText));
                Raise(nameof(LockedVisibility));
                Raise(nameof(UnlockedVisibility));
            }
        }
    }

    public string DetailText => $"{FrequencyText} \u00B7 {StateText}";

    public Visibility TrackingVisibility => IsTracked ? Visibility.Visible : Visibility.Collapsed;

    public Visibility LockedVisibility => IsLocked ? Visibility.Visible : Visibility.Collapsed;

    public Visibility UnlockedVisibility => IsLocked ? Visibility.Collapsed : Visibility.Visible;

    public string ActionText => IsLocked
        ? Text.Format("Unlock {0} and return to automatic string detection", NoteText)
        : Text.Format("Lock target to {0}", NoteText);

    public string AutomationName => Text.Format("{0}, {1}, {2}, {3}", StringLabel, NoteText, FrequencyText, StateText);

    private string StateText => _isLocked ? Text.Get("StringLocked") : _isTracked ? Text.Get("tracking") : Text.Get("unmeasured");
}

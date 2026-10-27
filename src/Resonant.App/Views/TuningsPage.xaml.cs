using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Resonant.App.ViewModels;

namespace Resonant.App.Views;

public sealed partial class TuningsPage : Page
{
    private bool _syncing;

    public TuningsPage()
    {
        ViewModel = AppServices.Tunings;
        InitializeComponent();

        ViewModel.PresetsRebuilt += (_, _) => SyncSelection();
        Loaded += (_, _) =>
        {
            AppCulture.Changed += OnCultureChanged;
            if (_culture != System.Globalization.CultureInfo.CurrentUICulture.Name)
            {
                OnCultureChanged(null, EventArgs.Empty);
            }

            SyncSelection();
        };
        Unloaded += (_, _) => AppCulture.Changed -= OnCultureChanged;
    }

    // Pages are cached and unsubscribe while unloaded, so a page that missed a language change refreshes when it loads again.
    private string _culture = System.Globalization.CultureInfo.CurrentUICulture.Name;

    private void OnCultureChanged(object? sender, EventArgs e)
    {
        _culture = System.Globalization.CultureInfo.CurrentUICulture.Name;
        Bindings.Update();
    }

    public TuningsViewModel ViewModel { get; }

    private void SyncSelection()
    {
        _syncing = true;
        try
        {
            var current = ViewModel.SelectedPreset;
            PresetList.SelectedItem = current is null
                ? null
                : ViewModel.Presets.FirstOrDefault(p => ReferenceEquals(p.Preset, current));
        }
        finally
        {
            _syncing = false;
        }
    }

    private void OnPresetSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || e.AddedItems.Count == 0)
        {
            return;
        }

        if (e.AddedItems[0] is PresetItemViewModel item)
        {
            ViewModel.SelectPreset(item.Preset);
        }
    }

    private void OnFilterChanged(object sender, SelectionChangedEventArgs args)
    {
        if (sender is ComboBox { SelectedItem: ComboBoxItem { Tag: string tag } })
        {
            ViewModel.Filter = tag;
        }
    }

    private void OnRemoveCustom(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: PresetItemViewModel item })
        {
            ViewModel.RemoveCustomCommand.Execute(item);
        }
    }
}

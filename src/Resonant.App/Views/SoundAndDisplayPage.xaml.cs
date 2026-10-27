using Microsoft.UI.Xaml.Controls;
using Resonant.App.ViewModels;

namespace Resonant.App.Views;

public sealed partial class SoundAndDisplayPage : Page
{
    public SoundAndDisplayPage()
    {
        ViewModel = AppServices.Settings;
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            AppCulture.Changed += OnCultureChanged;
            if (_culture != System.Globalization.CultureInfo.CurrentUICulture.Name)
            {
                OnCultureChanged(null, EventArgs.Empty);
            }

            await ViewModel.RefreshDevicesAsync();
        };
        Unloaded += (_, _) => AppCulture.Changed -= OnCultureChanged;
    }

    // Pages are cached and unsubscribe while unloaded, so a page that missed a language change refreshes when it loads again.
    private string _culture = System.Globalization.CultureInfo.CurrentUICulture.Name;

    private void OnCultureChanged(object? sender, EventArgs e)
    {
        _culture = System.Globalization.CultureInfo.CurrentUICulture.Name;
        Bindings.Update();
        ViewModel.RefreshSelections();
    }

    public SettingsViewModel ViewModel { get; }
}

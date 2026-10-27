using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Resonant.App.ViewModels;
using Resonant.Core.Localization;

namespace Resonant.App.Views;

public sealed partial class TunerPage : Page
{
    public TunerPage()
    {
        ViewModel = AppServices.Tuner;
        InitializeComponent();
        Spectrum.SpectrumProvider = () => ViewModel.Spectrum;
        Spectrum.IdealFrequencyProvider = () => ViewModel.IdealFrequencyHz;
        Loaded += async (_, _) =>
        {
            AppCulture.Changed += OnCultureChanged;
            if (_culture != System.Globalization.CultureInfo.CurrentUICulture.Name)
            {
                OnCultureChanged(null, EventArgs.Empty);
            }

            await ViewModel.InitializeListeningAsync();
        };
        Unloaded += (_, _) => AppCulture.Changed -= OnCultureChanged;
    }

    // Pages are cached and unsubscribe while unloaded, so a page that missed a language change refreshes when it loads again.
    private string _culture = System.Globalization.CultureInfo.CurrentUICulture.Name;

    private void OnCultureChanged(object? sender, EventArgs e)
    {
        _culture = System.Globalization.CultureInfo.CurrentUICulture.Name;
        Bindings.Update();
        Meter.RefreshAutomation();
    }

    public TunerViewModel ViewModel { get; }
    public SettingsViewModel Settings => AppServices.Settings;

    private async void OnDeviceMenuOpening(object sender, object e)
    {
        if (sender is not MenuFlyout menu)
            return;

        PopulateDeviceMenu(menu);
        try
        {
            await Settings.RefreshDevicesAsync();
            PopulateDeviceMenu(menu);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(exception);
            AddDeviceStatus(menu, Text.Get("Audio devices could not be listed: ") + exception.Message);
        }
    }

    private void PopulateDeviceMenu(MenuFlyout menu)
    {
        var isInput = ReferenceEquals(menu, InputDevicesFlyout);
        var devices = isInput ? Settings.InputDevices : Settings.OutputDevices;
        var selected = isInput ? Settings.SelectedInput : Settings.SelectedOutput;
        menu.Items.Clear();
        foreach (var device in devices)
        {
            var item = new ToggleMenuFlyoutItem
            {
                Text = device.Name,
                IsChecked = device.Id == selected?.Id,
                Tag = device
            };
            AutomationProperties.SetName(item, device.Name);
            item.Click += (_, _) =>
            {
                foreach (var sibling in menu.Items.OfType<ToggleMenuFlyoutItem>())
                    sibling.IsChecked = ReferenceEquals(sibling, item);

                var current = isInput ? Settings.SelectedInput : Settings.SelectedOutput;
                if (current?.Id == device.Id)
                    return;

                if (isInput)
                    Settings.SelectedInput = device;
                else
                    Settings.SelectedOutput = device;
            };
            menu.Items.Add(item);
        }

        AddDeviceStatus(menu, Settings.DeviceStatus);
    }

    private static void AddDeviceStatus(MenuFlyout menu, string status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return;

        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(new MenuFlyoutItem { Text = status, IsEnabled = false });
    }

    private void OnStringToggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { DataContext: StringItemViewModel item } button
            && (button.IsChecked == true) != item.IsLocked)
        {
            ViewModel.SelectString(button.IsChecked == true ? item.Index : null);
        }
    }

}

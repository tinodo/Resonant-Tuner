using Resonant.Core.Localization;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Resonant.App.Views;
using Windows.Graphics;
using Windows.System;
using Windows.UI.Core;

namespace Resonant.App;

public sealed partial class MainWindow : Window
{
    private const int DefaultWidth = 1000;
    private const int DefaultHeight = 700;
    private const int CompactWidth = 460;
    private const int CompactHeight = 580;

    private NavigationViewItem? _currentItem;
    private bool _reverting;
    private bool _wasCompact;

    public MainWindow()
    {
        InitializeComponent();
        Root.Language = System.Globalization.CultureInfo.CurrentUICulture.Name;
        Title = "resonant tuner";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(DragTitleBar);
        AppWindow.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Resonant.ico"));
        AppWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        Root.ActualThemeChanged += (_, _) => ApplyCaptionTheme();
        Root.SizeChanged += (_, _) => UpdateTitleBarInset();
        Root.PreviewKeyDown += OnTunerKeyDown;

        AppServices.Initialize(new DispatcherQueueDispatcher(DispatcherQueue));
        AppServices.Preferences.Changed += OnPreferencesChanged;
        AppCulture.Changed += OnCultureChanged;
        Closed += OnClosed;

        ResizeTo(DefaultWidth, DefaultHeight);
        ApplyLanguage();
        ApplyPreferences();

        Nav.SelectedItem = Nav.MenuItems[0];
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private async void OnTunerKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Handled || ContentFrame.Content is not TunerPage page ||
            VisualTreeHelper.GetOpenPopupsForXamlRoot(Root.XamlRoot).Count > 0)
        {
            return;
        }

        foreach (var modifier in new[] { VirtualKey.Control, VirtualKey.Menu, VirtualKey.Shift,
                     VirtualKey.LeftWindows, VirtualKey.RightWindows })
        {
            if ((Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(modifier) &
                 CoreVirtualKeyStates.Down) != 0)
            {
                return;
            }
        }

        var nativeSpace = false;
        for (var element = FocusManager.GetFocusedElement(Root.XamlRoot) as DependencyObject;
             element is not null; element = VisualTreeHelper.GetParent(element))
        {
            if (element is TextBox or NumberBox or ComboBox or PasswordBox or RichEditBox or ContentDialog)
            {
                return;
            }

            nativeSpace |= element is ButtonBase or ToggleSwitch or SelectorItem or Slider;
        }

        if (args.Key == VirtualKey.Space)
        {
            if (!nativeSpace)
            {
                args.Handled = true;
                await page.ViewModel.ToggleCaptureFromKeyboardAsync();
            }

            return;
        }

        var key = (int)args.Key;
        var number = key >= (int)VirtualKey.Number1 && key <= (int)VirtualKey.Number9
            ? key - (int)VirtualKey.Number1 + 1
            : key >= (int)VirtualKey.NumberPad1 && key <= (int)VirtualKey.NumberPad9
                ? key - (int)VirtualKey.NumberPad1 + 1 : 0;
        var count = page.ViewModel.Strings.Count;
        if (!page.ViewModel.IsChromatic && number > 0 && number <= count)
        {
            args.Handled = true;
            var index = count - number;
            page.ViewModel.SelectString(page.ViewModel.ManualIndex == index ? null : index);
        }
    }

    private void OnPreferencesChanged(object? sender, EventArgs args) => ApplyPreferences();

    private void OnCultureChanged(object? sender, EventArgs args)
    {
        ApplyLanguage();
        ApplyPreferences();
    }

    private void ApplyLanguage()
    {
        Root.Language = System.Globalization.CultureInfo.CurrentUICulture.Name;
        TunerNavItem.Content = Text.Get("Tuner");
        TuningsNavItem.Content = Text.Get("Tunings");
        SettingsNavItem.Content = Text.Get("Sound & display");
        AutomationProperties.SetName(CompactButton, Text.Get("Toggle compact mode"));
    }

    private void ApplyPreferences()
    {
        var prefs = AppServices.Preferences;
        Root.RequestedTheme = prefs.ElementTheme;
        ApplyCaptionTheme();
        CompactIcon.Glyph = prefs.CompactMode ? "\uE740" : "\uE73F";
        ToolTipService.SetToolTip(CompactButton, prefs.CompactMode ? Text.Get("Restore window size") : Text.Get("Compact window"));

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = prefs.AlwaysOnTop;
        }

        if (prefs.CompactMode != _wasCompact)
        {
            _wasCompact = prefs.CompactMode;
            if (_wasCompact)
            {
                ResizeTo(CompactWidth, CompactHeight);
            }
            else
            {
                ResizeTo(DefaultWidth, DefaultHeight);
            }
        }
    }

    private void OnToggleCompact(object sender, RoutedEventArgs args)
    {
        var prefs = AppServices.Preferences;
        prefs.SetCompactMode(!prefs.CompactMode);
    }

    private void UpdateTitleBarInset()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var scale = GetDpiForWindow(hwnd) / 96.0;
        CaptionInset.Width = new GridLength(AppWindow.TitleBar.RightInset / Math.Max(1.0, scale));
    }

    private void ResizeTo(int width, int height)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var dpi = GetDpiForWindow(hwnd);
        var scale = dpi == 0 ? 1.0 : dpi / 96.0;
        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        AppWindow.ResizeClient(new SizeInt32(
            Math.Min((int)(width * scale), Math.Max(320, workArea.Width - 32)),
            Math.Min((int)(height * scale), Math.Max(320, workArea.Height - 48))));
    }

    private void ApplyCaptionTheme()
    {
        var foreground = Root.ActualTheme == ElementTheme.Dark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
        AppWindow.TitleBar.ButtonForegroundColor = foreground;
        AppWindow.TitleBar.ButtonInactiveForegroundColor = foreground;
    }

    private async void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_reverting || args.SelectedItem is not NavigationViewItem item)
        {
            return;
        }

        if (_currentItem is { Tag: "tunings" } &&
            !ReferenceEquals(item, _currentItem) &&
            AppServices.Tunings.HasUnsavedEdits)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = Root.XamlRoot,
                RequestedTheme = Root.RequestedTheme,
                Title = Text.Get("Leave this custom tuning?"),
                Content = Text.Get("Custom tunings are never saved. Your edit is kept only until you close Resonant Tuner, and it is not applied until you choose Use for this session."),
                PrimaryButtonText = Text.Get("Keep editing"),
                CloseButtonText = Text.Get("Leave"),
                DefaultButton = ContentDialogButton.Primary,
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                _reverting = true;
                sender.SelectedItem = _currentItem;
                _reverting = false;
                return;
            }
        }

        _currentItem = item;
        var tag = item.Tag as string;
        var page = tag switch
        {
            "tunings" => typeof(TuningsPage),
            "settings" => typeof(SoundAndDisplayPage),
            _ => typeof(TunerPage),
        };
        ContentFrame.Navigate(page);
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        AppServices.Preferences.Changed -= OnPreferencesChanged;
        AppCulture.Changed -= OnCultureChanged;

        // Release the microphone and output handles deterministically before the process exits.
        Task.Run(AppServices.ShutdownAsync).Wait(TimeSpan.FromSeconds(2));
    }
}

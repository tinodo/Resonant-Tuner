using System.Diagnostics;
using System.Globalization;
using Microsoft.UI.Xaml;

namespace Resonant.App;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        var arguments = Environment.GetCommandLineArgs();
        var languageIndex = Array.IndexOf(arguments, "--language");
        if (languageIndex >= 0 && languageIndex + 1 < arguments.Length)
        {
            try
            {
                var culture = CultureInfo.GetCultureInfo(arguments[languageIndex + 1]);
                CultureInfo.CurrentUICulture = culture;
                CultureInfo.DefaultThreadCurrentUICulture = culture;
                CultureInfo.CurrentCulture = culture;
                CultureInfo.DefaultThreadCurrentCulture = culture;
            }
            catch (CultureNotFoundException)
            {
            }
        }

        AppCulture.CaptureSystem();

        UnhandledException += (_, args) =>
        {
            Debug.WriteLine(args.Exception.ToString());
            Console.Error.WriteLine(args.Exception.ToString());
            if (Debugger.IsAttached)
            {
                Debugger.Break();
            }
        };
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}

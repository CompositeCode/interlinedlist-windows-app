using InterlinedList.Services;
using Microsoft.Win32;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace InterlinedList;

public partial class App : Application
{
    private const string ThemeRegistryKey =
        @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public App()
    {
        // Wire diagnostics in the constructor — before InitializeComponent parses
        // App.xaml — so even a resource-load failure is logged instead of silent.
        AppLog.Startup();
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        // OnStartup is `async void`: any exception that escapes here is posted to
        // the dispatcher and tears the process down before a window ever shows —
        // which reads as "the app installs but doesn't run". Guard the whole body.
        try
        {
            base.OnStartup(e);
            ApplyTheme(IsSystemDarkMode());
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

            bool restored;
            try
            {
                restored = await AppServices.Session.TryRestoreSessionAsync();
            }
            catch (Exception ex)
            {
                // A transient network/DNS/timeout failure while validating a saved
                // token must never block the app — fall back to the login window.
                // (The token is intentionally NOT cleared here; only a genuine auth
                // rejection inside TryRestoreSessionAsync clears it.)
                AppLog.Error("Session restore failed unexpectedly; falling back to login.", ex);
                restored = false;
            }

            if (restored)
            {
                AppLog.Info("Session restored; showing main window.");
                ShowMainWindow();
            }
            else
            {
                AppLog.Info("No restorable session; showing login window.");
                ShowLoginWindow();
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Fatal error during startup.", ex);
            ShowStartupFailure(ex);
            Shutdown(1);
        }
    }

    // ── Global exception diagnostics ────────────────────────────────────────────

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Error("Unhandled UI-thread exception.", e.Exception);
        e.Handled = true; // logged + surfaced rather than a silent crash
        ShowStartupFailure(e.Exception);
    }

    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        AppLog.Error(
            "Unhandled non-UI exception" + (e.IsTerminating ? " (process terminating)." : "."),
            e.ExceptionObject as Exception);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        AppLog.Error("Unobserved task exception.", e.Exception);
        e.SetObserved();
    }

    private static void ShowStartupFailure(Exception ex)
    {
        try
        {
            MessageBox.Show(
                $"InterlinedList hit an error:\n\n{ex.Message}\n\nA full log was written to:\n{AppLog.CurrentLogFile}",
                "InterlinedList", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch
        {
            // No interactive desktop — the file/Event log already has the detail.
        }
    }

    // ── Login/session orchestration ────────────────────────────────────────────
    // No StartupUri: which window appears first depends on whether a saved
    // session restores successfully, so both windows are shown manually.

    private void ShowLoginWindow()
    {
        var login = new LoginWindow();
        login.LoginSucceeded += (_, _) =>
        {
            ShowMainWindow();
            login.Close();
        };
        MainWindow = login;
        login.Show();
    }

    private void ShowMainWindow()
    {
        var main = new MainWindow();
        main.LoggedOut += (_, _) =>
        {
            main.Close();
            ShowLoginWindow();
        };
        MainWindow = main;
        main.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        base.OnExit(e);
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General)
            Dispatcher.Invoke(() => ApplyTheme(IsSystemDarkMode()));
    }

    internal void ApplyTheme(bool dark)
    {
        var source = dark
            ? new Uri("Resources/Theme.Dark.xaml", UriKind.Relative)
            : new Uri("Resources/Theme.Light.xaml", UriKind.Relative);

        var dict = new ResourceDictionary { Source = source };
        // Index 1 is the semantic theme slot; index 0 is Palette (never swapped)
        Resources.MergedDictionaries[1] = dict;
    }

    internal static bool IsSystemDarkMode()
    {
        var value = Registry.GetValue(ThemeRegistryKey, "AppsUseLightTheme", 1);
        return value is int v && v == 0;
    }
}

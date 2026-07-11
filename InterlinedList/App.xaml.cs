using InterlinedList.Services;
using Microsoft.Win32;
using System.Windows;

namespace InterlinedList;

public partial class App : Application
{
    private const string ThemeRegistryKey =
        @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ApplyTheme(IsSystemDarkMode());
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        var restored = await AppServices.Session.TryRestoreSessionAsync();
        if (restored)
            ShowMainWindow();
        else
            ShowLoginWindow();
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

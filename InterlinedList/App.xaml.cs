using Microsoft.Win32;
using System.Windows;

namespace InterlinedList;

public partial class App : Application
{
    private const string ThemeRegistryKey =
        @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ApplyTheme(IsSystemDarkMode());
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
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

using System.Windows;
using System.Windows.Controls;
using InterlinedList.Sync.Core;

namespace InterlinedList.Sync;

/// <summary>Sync folder, poll interval, and launch-at-login. Returns true if saved.</summary>
internal sealed class SettingsWindow : Window
{
    public bool SyncFolderChanged { get; private set; }

    public SettingsWindow(SyncOptions options)
    {
        Title = "InterlinedList Sync — Settings";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;

        var originalFolder = options.SyncFolder;

        var folder = new TextBox { Text = options.SyncFolder, VerticalContentAlignment = VerticalAlignment.Center };
        var browse = new Button { Content = "Browse…", Width = 84, Margin = new Thickness(8, 0, 0, 0) };
        browse.Click += (_, _) =>
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog { InitialDirectory = folder.Text };
            if (dlg.ShowDialog() == true) folder.Text = dlg.FolderName;
        };
        var folderRow = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        DockPanel.SetDock(browse, Dock.Right);
        folderRow.Children.Add(browse);
        folderRow.Children.Add(folder);

        var interval = new TextBox { Text = options.PollIntervalSeconds.ToString(), Width = 80, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
        var autostart = new CheckBox { Content = "Start automatically when I sign in to Windows", IsChecked = options.AutoStart, Margin = new Thickness(0, 16, 0, 0) };
        var note = new TextBlock
        {
            Text = "Folders in InterlinedList are mirrored as subfolders. Deleting a file here deletes the document; deleting a folder here does NOT delete it on the server.",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.7, Margin = new Thickness(0, 16, 0, 0), FontSize = 11,
        };

        var save = new Button { Content = "Save", IsDefault = true, Width = 84, Padding = new Thickness(0, 4, 0, 4) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Width = 84, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(0, 4, 0, 4) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        buttons.Children.Add(save);
        buttons.Children.Add(cancel);

        save.Click += (_, _) =>
        {
            options.SyncFolder = string.IsNullOrWhiteSpace(folder.Text) ? SyncPaths.DefaultSyncFolder : folder.Text.Trim();
            if (int.TryParse(interval.Text, out var secs)) options.PollIntervalSeconds = Math.Max(10, secs);
            options.AutoStart = autostart.IsChecked == true;
            SyncPreferencesStore.Save(options);
            AutoStartManager.Apply(options.AutoStart);
            SyncFolderChanged = !string.Equals(originalFolder, options.SyncFolder, StringComparison.OrdinalIgnoreCase);
            DialogResult = true;
            Close();
        };

        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = "Sync folder" });
        panel.Children.Add(folderRow);
        panel.Children.Add(new TextBlock { Text = "Check for changes every (seconds)", Margin = new Thickness(0, 16, 0, 0) });
        panel.Children.Add(interval);
        panel.Children.Add(autostart);
        panel.Children.Add(note);
        panel.Children.Add(buttons);
        Content = panel;
    }
}

using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Hardcodet.Wpf.TaskbarNotification;
using InterlinedList.Sync.Core;

namespace InterlinedList.Sync;

/// <summary>
/// The tray (system-notification-area) host. Owns the NotifyIcon, its menu, and the
/// coordinator lifecycle. No main window — this is a background agent.
/// </summary>
internal sealed class TrayApplication : Application
{
    private readonly SyncOptions _options;
    private readonly SyncCoordinator _coordinator;
    private readonly AuthClient _auth;
    private readonly ICredentialSource _credentials;

    private TaskbarIcon? _tray;
    private MenuItem? _statusItem;
    private MenuItem? _signInItem;
    private MenuItem? _signOutItem;
    private MenuItem? _pauseItem;

    public TrayApplication(SyncOptions options, SyncCoordinator coordinator, AuthClient auth, ICredentialSource credentials)
    {
        _options = options;
        _coordinator = coordinator;
        _auth = auth;
        _credentials = credentials;
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        DispatcherUnhandledException += (_, e) =>
        {
            SyncLog.Error("Unhandled UI exception.", e.Exception);
            e.Handled = true;
        };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _tray = new TaskbarIcon { ToolTipText = "InterlinedList Sync" };
        TryLoadIcon();
        _tray.ContextMenu = BuildMenu();
        _tray.TrayMouseDoubleClick += (_, _) => OpenSyncFolder();
        _coordinator.StatusChanged += OnStatusChanged;

        if (_credentials.GetToken() is null)
            PromptSignIn();

        _coordinator.Start();
        RefreshSignInState();
    }

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();

        _statusItem = new MenuItem { Header = "Starting…", IsEnabled = false };
        menu.Items.Add(_statusItem);
        menu.Items.Add(new Separator());

        _signInItem = new MenuItem { Header = "Sign in…" };
        _signInItem.Click += (_, _) => PromptSignIn();
        menu.Items.Add(_signInItem);

        var openFolder = new MenuItem { Header = "Open Sync Folder" };
        openFolder.Click += (_, _) => OpenSyncFolder();
        menu.Items.Add(openFolder);

        var syncNow = new MenuItem { Header = "Sync Now" };
        syncNow.Click += (_, _) => _ = _coordinator.SyncNowAsync();
        menu.Items.Add(syncNow);

        _pauseItem = new MenuItem { Header = "Pause Sync" };
        _pauseItem.Click += (_, _) => TogglePause();
        menu.Items.Add(_pauseItem);

        var settings = new MenuItem { Header = "Settings…" };
        settings.Click += (_, _) => OpenSettings();
        menu.Items.Add(settings);

        menu.Items.Add(new Separator());

        _signOutItem = new MenuItem { Header = "Sign Out" };
        _signOutItem.Click += (_, _) => SignOut();
        menu.Items.Add(_signOutItem);

        var exit = new MenuItem { Header = "Exit" };
        exit.Click += (_, _) => Shutdown();
        menu.Items.Add(exit);

        return menu;
    }

    private void OnStatusChanged(SyncStatus status, string message) =>
        Dispatcher.Invoke(() =>
        {
            if (_tray is not null) _tray.ToolTipText = $"InterlinedList Sync — {message}";
            if (_statusItem is not null) _statusItem.Header = message;
            RefreshSignInState();
        });

    private void RefreshSignInState()
    {
        var signedIn = _credentials.GetToken() is not null;
        if (_signInItem is not null) _signInItem.Visibility = signedIn ? Visibility.Collapsed : Visibility.Visible;
        if (_signOutItem is not null) _signOutItem.Visibility = signedIn ? Visibility.Visible : Visibility.Collapsed;
        if (_pauseItem is not null) _pauseItem.Header = _coordinator.IsPaused ? "Resume Sync" : "Pause Sync";
    }

    private void PromptSignIn()
    {
        var window = new SignInWindow(_auth);
        if (window.ShowDialog() == true)
        {
            RefreshSignInState();
            _ = _coordinator.SyncNowAsync();
        }
    }

    private void SignOut()
    {
        SessionTokenFile.Clear();
        SyncLog.Info("Signed out (token cleared).");
        RefreshSignInState();
    }

    private void TogglePause()
    {
        if (_coordinator.IsPaused) _coordinator.Resume();
        else _coordinator.Pause();
        RefreshSignInState();
    }

    private void OpenSyncFolder()
    {
        try
        {
            Directory.CreateDirectory(_options.SyncFolder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_options.SyncFolder}\"") { UseShellExecute = true });
        }
        catch (Exception ex) { SyncLog.Error("Failed to open sync folder.", ex); }
    }

    private void OpenSettings()
    {
        var window = new SettingsWindow(_options);
        if (window.ShowDialog() == true && window.SyncFolderChanged)
        {
            _tray?.ShowBalloonTip("InterlinedList Sync",
                "Restart the sync utility to start syncing the new folder.", BalloonIcon.Info);
        }
    }

    private void TryLoadIcon()
    {
        try
        {
            var icoPath = Path.Combine(AppContext.BaseDirectory, "tray.ico");
            if (File.Exists(icoPath)) _tray!.Icon = new System.Drawing.Icon(icoPath);
        }
        catch (Exception ex) { SyncLog.Warn($"Could not load tray icon: {ex.Message}"); }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _coordinator.Dispose();
        _tray?.Dispose();
        SyncLog.Info("Sync utility exited.");
        base.OnExit(e);
    }
}

using InterlinedList.Sync.Core;

namespace InterlinedList.Sync;

internal static class Program
{
    [STAThread]
    public static int Main()
    {
        SyncLog.Startup();
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            SyncLog.Error("Unhandled exception.", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            SyncLog.Error("Unobserved task exception.", e.Exception);
            e.SetObserved();
        };

        try
        {
            var options = SyncPreferencesStore.Load();
            Directory.CreateDirectory(options.SyncFolder);
            Directory.CreateDirectory(SyncPaths.SyncDataDir);

            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(100) };
            var credentials = new DpapiCredentialSource();
            var state = new SqliteSyncStateStore(SyncPaths.StateDbFile);
            var mapper = new FileMapper(options.SyncFolder);
            var client = new HttpDocumentSyncClient(http, credentials);
            var engine = new SyncEngine(client, state, mapper, trace: SyncLog.Info);
            var coordinator = new SyncCoordinator(engine, options, credentials);
            var auth = new AuthClient(http);

            var app = new TrayApplication(options, coordinator, auth, credentials);
            return app.Run();
        }
        catch (Exception ex)
        {
            SyncLog.Error("Fatal startup error.", ex);
            return 1;
        }
    }
}

using System.Net.Sockets;
using InterlinedList.Sync.Core;

namespace InterlinedList.Sync;

internal enum SyncStatus { Idle, Syncing, Paused, Offline, SignedOut, AuthExpired, Error }

/// <summary>
/// Drives the engine: a periodic pull loop (with a full-snapshot reconcile every
/// Nth cycle) plus watcher-driven pushes. All engine calls are serialized through a
/// single gate because the SQLite state connection is not thread-safe.
/// </summary>
internal sealed class SyncCoordinator(SyncEngine engine, SyncOptions options, ICredentialSource credentials)
    : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private FolderWatcher? _watcher;
    private Task? _pollTask;
    private volatile bool _paused;
    private int _pollCounter;

    public event Action<SyncStatus, string>? StatusChanged;
    public SyncStatus Status { get; private set; } = SyncStatus.Idle;
    public DateTimeOffset? LastSyncedAt { get; private set; }
    public bool IsPaused => _paused;

    public void Start()
    {
        Directory.CreateDirectory(options.SyncFolder);
        _watcher = new FolderWatcher(options.SyncFolder, OnLocalChange);
        _watcher.Start();
        _pollTask = Task.Run(() => PollLoopAsync(_cts.Token));
        SyncLog.Info($"Coordinator started; watching '{options.SyncFolder}'.");
    }

    public void Pause() { _paused = true; SetStatus(SyncStatus.Paused, "Paused"); }

    public void Resume()
    {
        _paused = false;
        SetStatus(SyncStatus.Idle, "Resumed");
        _ = RunSyncCycleAsync(fullSnapshot: true, _cts.Token);
    }

    public Task SyncNowAsync() => RunSyncCycleAsync(fullSnapshot: false, _cts.Token);

    private async Task PollLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (credentials.GetToken() is null)
                SetStatus(SyncStatus.SignedOut, "Not signed in");
            else if (!_paused)
            {
                var full = _pollCounter % Math.Max(1, options.FullReconcileEveryNPolls) == 0;
                await RunSyncCycleAsync(full, ct);
                _pollCounter++;
            }

            try { await Task.Delay(TimeSpan.FromSeconds(options.EffectivePollIntervalSeconds), ct); }
            catch (TaskCanceledException) { break; }
        }
    }

    private async Task RunSyncCycleAsync(bool fullSnapshot, CancellationToken ct)
    {
        if (credentials.GetToken() is null) { SetStatus(SyncStatus.SignedOut, "Not signed in"); return; }

        await _gate.WaitAsync(ct);
        try
        {
            SetStatus(SyncStatus.Syncing, fullSnapshot ? "Reconciling…" : "Syncing…");
            var report = await engine.PullAsync(fullSnapshot, ct);
            LastSyncedAt = DateTimeOffset.Now;
            SetStatus(_paused ? SyncStatus.Paused : SyncStatus.Idle, $"Up to date · {report}");
        }
        catch (AuthExpiredException) { SetStatus(SyncStatus.AuthExpired, "Sign-in expired"); }
        catch (NotSignedInException) { SetStatus(SyncStatus.SignedOut, "Not signed in"); }
        catch (RateLimitedException e)
        {
            SetStatus(SyncStatus.Idle, "Rate limited — backing off");
            await SafeDelay(e.RetryAfterSeconds ?? 30, ct);
        }
        catch (OperationCanceledException) { /* shutting down */ }
        catch (Exception ex) when (ex is HttpRequestException or IOException or SocketException)
        {
            SetStatus(SyncStatus.Offline, "Offline");
            SyncLog.Warn($"Sync offline: {ex.Message}");
        }
        catch (Exception ex)
        {
            SetStatus(SyncStatus.Error, "Error (see log)");
            SyncLog.Error("Sync cycle failed.", ex);
        }
        finally { _gate.Release(); }
    }

    private void OnLocalChange(LocalChange change)
    {
        if (_paused || credentials.GetToken() is null) return;

        _ = Task.Run(async () =>
        {
            await _gate.WaitAsync(_cts.Token);
            try
            {
                await engine.PushLocalChangeAsync(change, _cts.Token);
                LastSyncedAt = DateTimeOffset.Now;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { SyncLog.Warn($"Push failed for '{change.Path}': {ex.Message}"); }
            finally { _gate.Release(); }
        });
    }

    private static async Task SafeDelay(int seconds, CancellationToken ct)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(seconds, 1, 300)), ct); }
        catch { /* cancelled */ }
    }

    private void SetStatus(SyncStatus status, string message)
    {
        Status = status;
        StatusChanged?.Invoke(status, message);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _watcher?.Dispose();
        try { _pollTask?.Wait(1000); } catch { /* ignore */ }
        _cts.Dispose();
        _gate.Dispose();
    }
}

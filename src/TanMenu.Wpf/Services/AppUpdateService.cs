using Serilog;
using Velopack;
using Velopack.Sources;

namespace TanMenu.Wpf.Services;

public enum AppUpdateStatus
{
    Disabled,
    StoreManaged,
    Idle,
    Checking,
    UpToDate,
    Available,
    Downloading,
    ReadyToRestart,
    Failed,
}

public sealed record AppUpdateState(
    AppUpdateStatus Status,
    string? Version = null,
    int Progress = 0,
    string? Error = null);

public interface IAppUpdateService
{
    AppUpdateState State { get; }
    event EventHandler? StateChanged;
    Task CheckForUpdatesAsync(CancellationToken cancellationToken = default);
    Task DownloadUpdateAsync(CancellationToken cancellationToken = default);
    void ApplyAndRestart();
}

/// <summary>GitHub Releases updater for the Velopack portable build.</summary>
public sealed class VelopackAppUpdateService : IAppUpdateService
{
    private const string RepositoryUrl = "https://github.com/kylin30/TanMenu";

    private readonly UpdateManager _manager;
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private UpdateInfo? _availableUpdate;

    public VelopackAppUpdateService()
    {
        _manager = new UpdateManager(new GithubSource(
            RepositoryUrl,
            accessToken: null!,
            prerelease: false));

        State = _manager.UpdatePendingRestart is { } pending
            ? new AppUpdateState(AppUpdateStatus.ReadyToRestart, pending.Version.ToString())
            : new AppUpdateState(AppUpdateStatus.Idle);
    }

    public AppUpdateState State { get; private set; }
    public event EventHandler? StateChanged;

    public async Task CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        if (!await _operationLock.WaitAsync(0, cancellationToken))
            return;

        try
        {
            if (_manager.UpdatePendingRestart is { } pending)
            {
                SetState(new AppUpdateState(AppUpdateStatus.ReadyToRestart, pending.Version.ToString()));
                return;
            }

            SetState(new AppUpdateState(AppUpdateStatus.Checking));
            cancellationToken.ThrowIfCancellationRequested();
            _availableUpdate = await _manager.CheckForUpdatesAsync();
            SetState(_availableUpdate is null
                ? new AppUpdateState(AppUpdateStatus.UpToDate)
                : new AppUpdateState(
                    AppUpdateStatus.Available,
                    _availableUpdate.TargetFullRelease.Version.ToString()));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetState(new AppUpdateState(AppUpdateStatus.Idle));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to check for TanMenu updates");
            SetState(new AppUpdateState(AppUpdateStatus.Failed, Error: ex.Message));
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task DownloadUpdateAsync(CancellationToken cancellationToken = default)
    {
        if (_availableUpdate is null || !await _operationLock.WaitAsync(0, cancellationToken))
            return;

        try
        {
            var update = _availableUpdate;
            SetState(new AppUpdateState(
                AppUpdateStatus.Downloading,
                update.TargetFullRelease.Version.ToString()));

            await _manager.DownloadUpdatesAsync(
                update,
                progress => SetState(new AppUpdateState(
                    AppUpdateStatus.Downloading,
                    update.TargetFullRelease.Version.ToString(),
                    progress)),
                cancellationToken);

            SetState(new AppUpdateState(
                AppUpdateStatus.ReadyToRestart,
                update.TargetFullRelease.Version.ToString(),
                100));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetState(new AppUpdateState(
                AppUpdateStatus.Available,
                _availableUpdate.TargetFullRelease.Version.ToString()));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to download TanMenu update");
            SetState(new AppUpdateState(AppUpdateStatus.Failed, Error: ex.Message));
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public void ApplyAndRestart()
    {
        var update = _manager.UpdatePendingRestart ?? _availableUpdate?.TargetFullRelease;
        if (update is null)
            return;

        _manager.ApplyUpdatesAndRestart(update);
    }

    private void SetState(AppUpdateState state)
    {
        State = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}

public sealed class FixedAppUpdateService(AppUpdateStatus status) : IAppUpdateService
{
    public AppUpdateState State { get; } = new(status);
    public event EventHandler? StateChanged { add { } remove { } }
    public Task CheckForUpdatesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task DownloadUpdateAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public void ApplyAndRestart() { }
}

using System.Diagnostics;

namespace CyRevision.Sync;

public sealed class ManagedSyncthingEngine : ISyncEngine, IAsyncDisposable
{
    private readonly SyncthingIsolationOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Process? _ownedProcess;
    private SyncthingApiClient? _apiClient;
    private SyncEngineStatus _status;
    private string _deviceId = string.Empty;

    public ManagedSyncthingEngine(SyncthingIsolationOptions options)
    {
        _options = options;
        _status = options.Enabled
            ? new SyncEngineStatus(SyncEngineState.Stopped, 0, 0, "Instance CyRevision arrêtée")
            : new SyncEngineStatus(SyncEngineState.Disabled, 0, 0, "Synchronisation désactivée");
    }

    public SyncEngineStatus Status => _status;

    public string DeviceId => _deviceId;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!_options.Enabled)
            {
                _status = new SyncEngineStatus(SyncEngineState.Disabled, 0, 0, "Synchronisation désactivée");
                return;
            }

            if (_ownedProcess is { HasExited: false })
            {
                return;
            }

            _options.Validate();
            if (!File.Exists(_options.ExecutablePath))
            {
                throw new FileNotFoundException("L'exécutable Syncthing configuré est introuvable.", _options.ExecutablePath);
            }

            Directory.CreateDirectory(_options.ConfigurationDirectory);
            Directory.CreateDirectory(_options.DataDirectory);
            Directory.CreateDirectory(_options.ExchangeDirectory);
            _status = new SyncEngineStatus(SyncEngineState.Starting, 0, 0, "Démarrage de l'instance Syncthing CyRevision…");

            ProcessStartInfo startInfo = CreateStartInfo();
            Process process = new() { StartInfo = startInfo, EnableRaisingEvents = true };
            process.Exited += OnOwnedProcessExited;
            if (!process.Start())
            {
                process.Dispose();
                throw new InvalidOperationException("Syncthing n'a pas pu démarrer.");
            }

            _ownedProcess = process;
            WriteOwnerMarker(process);
            _apiClient?.Dispose();
            _apiClient = new SyncthingApiClient(_options.ApiEndpoint, _options.ApiKey);
            await WaitUntilHealthyAsync(cancellationToken);
            _status = await GetRunningStatusAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            _status = new SyncEngineStatus(SyncEngineState.Faulted, 0, 0, exception.Message);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task PauseAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureOwnedRunningInstance();
            await _apiClient!.PauseAsync(cancellationToken);
            SyncthingRuntimeStatus runtime = await _apiClient.GetRuntimeStatusAsync(cancellationToken);
            _status = new SyncEngineStatus(SyncEngineState.Paused, runtime.ConnectedPeers, runtime.PendingBytes, "Synchronisation en pause");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ResumeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureOwnedRunningInstance();
            await _apiClient!.ResumeAsync(cancellationToken);
            _status = await GetRunningStatusAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SyncEngineStatus> RefreshStatusAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!_options.Enabled)
            {
                return _status = new SyncEngineStatus(SyncEngineState.Disabled, 0, 0, "Synchronisation désactivée");
            }

            if (_ownedProcess is null || _ownedProcess.HasExited)
            {
                return _status = new SyncEngineStatus(SyncEngineState.Stopped, 0, 0, "Instance CyRevision arrêtée");
            }

            return _status = await GetRunningStatusAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            return _status = new SyncEngineStatus(SyncEngineState.Faulted, 0, 0, exception.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopOwnedInstanceAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Process? process = _ownedProcess;
            if (process is null)
            {
                _status = new SyncEngineStatus(_options.Enabled ? SyncEngineState.Stopped : SyncEngineState.Disabled, 0, 0);
                return;
            }

            if (!process.HasExited)
            {
                try
                {
                    if (_apiClient is not null)
                    {
                        await _apiClient.ShutdownAsync(cancellationToken);
                    }
                }
                catch (HttpRequestException)
                {
                    // The owned process may already be shutting down.
                }

                using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(8));
                try
                {
                    await process.WaitForExitAsync(timeout.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(cancellationToken);
                }
            }

            CleanupOwnedProcess();
            _status = new SyncEngineStatus(SyncEngineState.Stopped, 0, 0, "Instance CyRevision arrêtée");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopOwnedInstanceAsync();
        _apiClient?.Dispose();
        _gate.Dispose();
    }

    private ProcessStartInfo CreateStartInfo()
    {
        ProcessStartInfo info = new()
        {
            FileName = Path.GetFullPath(_options.ExecutablePath),
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(_options.ExecutablePath))!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = false,
            RedirectStandardError = false
        };
        info.ArgumentList.Add("serve");
        info.ArgumentList.Add("--config=" + Path.GetFullPath(_options.ConfigurationDirectory));
        info.ArgumentList.Add("--data=" + Path.GetFullPath(_options.DataDirectory));
        info.ArgumentList.Add("--gui-address=" + _options.ApiEndpoint.GetLeftPart(UriPartial.Authority));
        info.ArgumentList.Add("--gui-apikey=" + _options.ApiKey);
        info.ArgumentList.Add("--no-browser");
        info.ArgumentList.Add("--no-restart");
        info.ArgumentList.Add("--no-upgrade");
        info.ArgumentList.Add("--no-default-folder");
        info.ArgumentList.Add("--logfile=" + Path.GetFullPath(_options.LogPath));
        if (OperatingSystem.IsWindows())
        {
            info.ArgumentList.Add("--no-console");
        }

        return info;
    }

    private async Task WaitUntilHealthyAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(20);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_ownedProcess is null || _ownedProcess.HasExited)
            {
                throw new InvalidOperationException("L'instance Syncthing CyRevision s'est arrêtée pendant son démarrage.");
            }

            if (await _apiClient!.IsHealthyAsync(cancellationToken))
            {
                return;
            }

            await Task.Delay(250, cancellationToken);
        }

        throw new TimeoutException("L'API Syncthing CyRevision n'a pas répondu dans le délai prévu.");
    }

    private async Task<SyncEngineStatus> GetRunningStatusAsync(CancellationToken cancellationToken)
    {
        SyncthingRuntimeStatus runtime = await _apiClient!.GetRuntimeStatusAsync(cancellationToken);
        _deviceId = runtime.DeviceId;
        return new SyncEngineStatus(
            SyncEngineState.Running,
            runtime.ConnectedPeers,
            runtime.PendingBytes,
            string.IsNullOrWhiteSpace(runtime.Version) ? "Instance CyRevision active" : $"Syncthing {runtime.Version}");
    }

    private void EnsureOwnedRunningInstance()
    {
        if (_ownedProcess is null || _ownedProcess.HasExited || _apiClient is null)
        {
            throw new InvalidOperationException("Aucune instance Syncthing lancée par CyRevision n'est active.");
        }
    }

    private void WriteOwnerMarker(Process process)
    {
        string markerPath = Path.Combine(_options.DataDirectory, "owned-process.txt");
        File.WriteAllText(markerPath, $"{process.Id}\n{process.StartTime.ToUniversalTime():O}\n{_options.ExecutablePath}");
    }

    private void OnOwnedProcessExited(object? sender, EventArgs e)
    {
        if (ReferenceEquals(sender, _ownedProcess))
        {
            _status = new SyncEngineStatus(SyncEngineState.Stopped, 0, 0, "Instance CyRevision arrêtée");
        }
    }

    private void CleanupOwnedProcess()
    {
        Process? process = _ownedProcess;
        _ownedProcess = null;
        if (process is not null)
        {
            process.Exited -= OnOwnedProcessExited;
            process.Dispose();
        }

        string markerPath = Path.Combine(_options.DataDirectory, "owned-process.txt");
        File.Delete(markerPath);
    }
}

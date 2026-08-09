using CyRevision.Core.Projects;

namespace CyRevision.Server;

public sealed class BackupSchedulerService : BackgroundService
{
    private readonly ServerOptions _options;
    private readonly IProjectCatalog _catalog;
    private readonly ServerRuntime _runtime;
    private readonly ILogger<BackupSchedulerService> _logger;

    public BackupSchedulerService(
        ServerOptions options,
        IProjectCatalog catalog,
        ServerRuntime runtime,
        ILogger<BackupSchedulerService> logger)
    {
        _options = options;
        _catalog = catalog;
        _runtime = runtime;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromMinutes(5));
        do
        {
            try
            {
                IReadOnlyList<ProjectDefinition> projects = await _catalog.GetAllAsync(stoppingToken);
                foreach (ProjectDefinition project in projects.Where(item => item.Features.BackupEnabled))
                {
                    IReadOnlyList<CyRevision.Backup.BackupSnapshot> snapshots = await _runtime.GetBackupsAsync(project.Id, stoppingToken);
                    if (snapshots.Count == 0 || DateTimeOffset.UtcNow - snapshots[0].CreatedAt >= _options.BackupInterval)
                    {
                        await _runtime.CreateBackupAsync(project.Id, stoppingToken);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Scheduled CyRevision backup pass failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

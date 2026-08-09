using CyRevision.Core.Projects;

namespace CyRevision.Server;

public sealed class GitExchangeSchedulerService : BackgroundService
{
    private readonly IProjectCatalog _catalog;
    private readonly ServerRuntime _runtime;
    private readonly ILogger<GitExchangeSchedulerService> _logger;

    public GitExchangeSchedulerService(
        IProjectCatalog catalog,
        ServerRuntime runtime,
        ILogger<GitExchangeSchedulerService> logger)
    {
        _catalog = catalog;
        _runtime = runtime;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            IReadOnlyList<ProjectDefinition> projects = await _catalog.GetAllAsync(stoppingToken);
            foreach (ProjectDefinition project in projects.Where(item => item.Features.GitEnabled && item.Features.PeerSyncEnabled))
            {
                try
                {
                    await _runtime.ExchangeGitAsync(project.Id, stoppingToken);
                }
                catch (InvalidOperationException)
                {
                    // The optional Sync engine is simply stopped or not configured.
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Git peer exchange failed for project {ProjectId}.", project.Id);
                }
            }
        }
    }
}

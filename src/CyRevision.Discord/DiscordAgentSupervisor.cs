using System.Collections.Concurrent;

namespace CyRevision.Discord;

public sealed class DiscordAgentSupervisor : IAsyncDisposable
{
    private readonly JsonDiscordAgentStore _store;
    private readonly Func<DiscordProjectAgent> _agentFactory;
    private readonly ConcurrentDictionary<Guid, AgentRuntime> _runtimes = new();
    private readonly SemaphoreSlim _operationGate = new(1, 1);

    public DiscordAgentSupervisor(
        JsonDiscordAgentStore store,
        Func<DiscordProjectAgent> agentFactory)
    {
        _store = store;
        _agentFactory = agentFactory;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<DiscordAgentRegistration> registrations = await _store.GetRegistrationsAsync(cancellationToken);
        foreach (DiscordAgentRegistration registration in registrations.Where(item => item.Profile.StartAutomatically))
        {
            try
            {
                await StartAsync(registration.ProjectId, cancellationToken);
            }
            catch (Exception exception)
            {
                AgentRuntime runtime = GetOrCreateRuntime(registration.ProjectId);
                runtime.Status = new DiscordAgentStatus(DiscordAgentRuntimeState.Error, exception.Message);
            }
        }
    }

    public Task<IReadOnlyList<DiscordAgentRegistration>> GetRegistrationsAsync(
        CancellationToken cancellationToken = default) =>
        _store.GetRegistrationsAsync(cancellationToken);

    public async Task<DiscordAgentPublicStatus?> GetStatusAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        DiscordAgentRegistration? registration = await _store.GetRegistrationAsync(projectId, cancellationToken);
        if (registration is null)
        {
            return null;
        }

        DiscordAgentCheckpoint? checkpoint = await _store.GetCheckpointAsync(projectId, cancellationToken);
        DiscordAgentStatus status = _runtimes.TryGetValue(projectId, out AgentRuntime? runtime)
            ? runtime.Status
            : new DiscordAgentStatus(DiscordAgentRuntimeState.Stopped, "Autonomous agent ready — stopped.");
        return new DiscordAgentPublicStatus(
            projectId,
            registration.ProjectName,
            registration.RepositoryPath,
            DiscordWebhookAddress.TryCreate(registration.Profile.WebhookUrl, out _),
            registration.Profile.StartAutomatically,
            registration.Profile.DisplayName,
            registration.Profile.ProjectLabel,
            registration.Profile.RepositoryWebUrl,
            registration.Profile.PollIntervalSeconds,
            registration.Profile.NotifyCommits,
            registration.Profile.NotifyBranchChanges,
            runtime?.Agent.IsRunning == true,
            status.State,
            status.Details,
            status.Branch ?? checkpoint?.LastBranch,
            status.LastCheckedAt ?? checkpoint?.LastCheckedAt,
            status.LastNotificationAt ?? checkpoint?.LastNotificationAt);
    }

    public async Task ConfigureAsync(
        DiscordAgentRegistration registration,
        CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            bool restart = _runtimes.TryGetValue(registration.ProjectId, out AgentRuntime? runtime) &&
                           runtime.Agent.IsRunning;
            if (restart)
            {
                await runtime!.Agent.StopAsync(cancellationToken);
            }

            await _store.SaveRegistrationAsync(registration, cancellationToken);
            if (restart)
            {
                await runtime!.Agent.StartAsync(
                    registration.Profile,
                    registration.RepositoryPath,
                    registration.ProjectName,
                    cancellationToken);
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task StartAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            DiscordAgentRegistration registration = await RequireRegistrationAsync(projectId, cancellationToken);
            if (!Directory.Exists(registration.RepositoryPath))
            {
                throw new DirectoryNotFoundException(
                    $"The autonomous agent cannot access repository '{registration.RepositoryPath}'.");
            }

            AgentRuntime runtime = GetOrCreateRuntime(projectId);
            await runtime.Agent.StartAsync(
                registration.Profile,
                registration.RepositoryPath,
                registration.ProjectName,
                cancellationToken);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task StopAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            if (_runtimes.TryGetValue(projectId, out AgentRuntime? runtime))
            {
                await runtime.Agent.StopAsync(cancellationToken);
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task PollNowAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        if (!_runtimes.TryGetValue(projectId, out AgentRuntime? runtime) || !runtime.Agent.IsRunning)
        {
            throw new InvalidOperationException("The autonomous Discord agent is not running for this project.");
        }

        await runtime.Agent.PollNowAsync(cancellationToken);
    }

    public async Task SendTestAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        DiscordAgentRegistration registration = await RequireRegistrationAsync(projectId, cancellationToken);
        AgentRuntime runtime = GetOrCreateRuntime(projectId);
        await runtime.Agent.SendTestAsync(registration.Profile, registration.ProjectName, cancellationToken);
    }

    public async Task RemoveAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            if (_runtimes.TryRemove(projectId, out AgentRuntime? runtime))
            {
                runtime.Agent.StatusChanged -= runtime.StatusHandler;
                await runtime.Agent.DisposeAsync();
            }

            await _store.RemoveProjectAsync(projectId, cancellationToken);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach ((_, AgentRuntime runtime) in _runtimes)
        {
            runtime.Agent.StatusChanged -= runtime.StatusHandler;
            await runtime.Agent.DisposeAsync();
        }

        _runtimes.Clear();
        _operationGate.Dispose();
    }

    private AgentRuntime GetOrCreateRuntime(Guid projectId) => _runtimes.GetOrAdd(projectId, _ =>
    {
        DiscordProjectAgent agent = _agentFactory();
        AgentRuntime runtime = new(agent);
        EventHandler<DiscordAgentStatus> handler = (_, status) => runtime.Status = status;
        runtime.StatusHandler = handler;
        agent.StatusChanged += handler;
        return runtime;
    });

    private async Task<DiscordAgentRegistration> RequireRegistrationAsync(
        Guid projectId,
        CancellationToken cancellationToken) =>
        await _store.GetRegistrationAsync(projectId, cancellationToken)
        ?? throw new KeyNotFoundException($"No Discord agent registration exists for project {projectId}.");

    private sealed class AgentRuntime(DiscordProjectAgent agent)
    {
        public DiscordProjectAgent Agent { get; } = agent;

        public EventHandler<DiscordAgentStatus> StatusHandler { get; set; } = null!;

        public DiscordAgentStatus Status { get; set; } = new(
            DiscordAgentRuntimeState.Stopped,
            "Autonomous agent ready — stopped.");
    }
}

using System.Reflection;
using CyRevision.Discord;
using CyRevision.Discord.Agent;
using CyRevision.Git;

AutonomousAgentOptions options;
try
{
    options = AutonomousAgentOptions.Parse(args);
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    Console.Error.WriteLine(AutonomousAgentOptions.HelpText);
    return 2;
}

if (options.ShowHelp)
{
    Console.WriteLine(AutonomousAgentOptions.HelpText);
    return 0;
}

Directory.CreateDirectory(options.DataDirectory);
(string controlToken, bool tokenCreated) = await ControlTokenProvider.LoadOrCreateAsync(options.TokenFile!);
if (tokenCreated || options.PrintToken)
{
    Console.WriteLine(tokenCreated
        ? "A new control token was created. Store it as a secret in the CyRevision controller:"
        : "Autonomous agent control token:");
    Console.WriteLine(controlToken);
}

JsonDiscordAgentStore store = new(Path.Combine(options.DataDirectory, "projects"));
GitCliRepositoryService git = new();
await using DiscordAgentSupervisor supervisor = new(
    store,
    () => new DiscordProjectAgent(
        new GitDiscordProjectSnapshotProvider(git),
        store,
        new DiscordWebhookClient()));
await supervisor.InitializeAsync();

WebApplicationBuilder builder = WebApplication.CreateBuilder(Array.Empty<string>());
builder.WebHost.UseUrls(options.ListenUrl);
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(console =>
{
    console.SingleLine = true;
    console.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
});

WebApplication app = builder.Build();
DateTimeOffset startedAt = DateTimeOffset.UtcNow;
string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

app.Use(async (context, next) =>
{
    if (!ControlTokenProvider.IsValid(controlToken, context.Request.Headers.Authorization.ToString()))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(
            new DiscordAgentCommandResult(false, "A valid autonomous agent bearer token is required."));
        return;
    }

    context.Response.Headers.CacheControl = "no-store";
    await next();
});

app.MapGet("/api/v1/health", async (CancellationToken cancellationToken) =>
{
    IReadOnlyList<DiscordAgentRegistration> registrations = await supervisor.GetRegistrationsAsync(cancellationToken);
    int running = 0;
    foreach (DiscordAgentRegistration registration in registrations)
    {
        DiscordAgentPublicStatus? status = await supervisor.GetStatusAsync(registration.ProjectId, cancellationToken);
        if (status?.IsRunning == true)
        {
            running++;
        }
    }

    return Results.Json(new DiscordAgentHostStatus(
        "CyRevision.Discord.Agent",
        version,
        startedAt,
        registrations.Count,
        running));
});

app.MapGet("/api/v1/projects", async (CancellationToken cancellationToken) =>
{
    IReadOnlyList<DiscordAgentRegistration> registrations = await supervisor.GetRegistrationsAsync(cancellationToken);
    List<DiscordAgentPublicStatus> statuses = [];
    foreach (DiscordAgentRegistration registration in registrations)
    {
        DiscordAgentPublicStatus? status = await supervisor.GetStatusAsync(registration.ProjectId, cancellationToken);
        if (status is not null)
        {
            statuses.Add(status);
        }
    }

    return Results.Json(statuses);
});

app.MapGet("/api/v1/projects/{projectId:guid}", async (Guid projectId, CancellationToken cancellationToken) =>
{
    DiscordAgentPublicStatus? status = await supervisor.GetStatusAsync(projectId, cancellationToken);
    return status is null ? Results.NotFound() : Results.Json(status);
});

app.MapPut("/api/v1/projects/{projectId:guid}", async (
    Guid projectId,
    DiscordAgentConfigurationRequest request,
    CancellationToken cancellationToken) =>
{
    if (projectId != request.ProjectId)
    {
        return Results.BadRequest(new DiscordAgentCommandResult(false, "Route and payload project IDs differ."));
    }

    try
    {
        DiscordAgentRegistration? existing = await store.GetRegistrationAsync(projectId, cancellationToken);
        string webhookUrl = string.IsNullOrWhiteSpace(request.WebhookUrl)
            ? existing?.Profile.WebhookUrl ?? string.Empty
            : request.WebhookUrl.Trim();
        DiscordAgentProfile profile = new(
            projectId,
            webhookUrl,
            request.DisplayName,
            request.ProjectLabel,
            request.RepositoryWebUrl,
            request.PollIntervalSeconds,
            request.NotifyCommits,
            request.NotifyBranchChanges,
            request.StartAutomatically);
        DiscordAgentRegistration registration = new(
            projectId,
            request.ProjectName,
            Path.GetFullPath(request.RepositoryPath),
            profile);
        await supervisor.ConfigureAsync(registration, cancellationToken);
        return Results.Json(new DiscordAgentCommandResult(true, "Autonomous Discord project configuration saved."));
    }
    catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException or ArgumentException)
    {
        return Results.BadRequest(new DiscordAgentCommandResult(false, exception.Message));
    }
});

app.MapPost("/api/v1/projects/{projectId:guid}/start", (Guid projectId, CancellationToken cancellationToken) =>
    ExecuteCommandAsync(() => supervisor.StartAsync(projectId, cancellationToken), "Autonomous Discord agent started."));
app.MapPost("/api/v1/projects/{projectId:guid}/stop", (Guid projectId, CancellationToken cancellationToken) =>
    ExecuteCommandAsync(() => supervisor.StopAsync(projectId, cancellationToken), "Autonomous Discord agent stopped."));
app.MapPost("/api/v1/projects/{projectId:guid}/check", (Guid projectId, CancellationToken cancellationToken) =>
    ExecuteCommandAsync(() => supervisor.PollNowAsync(projectId, cancellationToken), "Autonomous Discord check completed."));
app.MapPost("/api/v1/projects/{projectId:guid}/test", (Guid projectId, CancellationToken cancellationToken) =>
    ExecuteCommandAsync(() => supervisor.SendTestAsync(projectId, cancellationToken), "Discord test message sent."));
app.MapDelete("/api/v1/projects/{projectId:guid}", (Guid projectId, CancellationToken cancellationToken) =>
    ExecuteCommandAsync(() => supervisor.RemoveAsync(projectId, cancellationToken), "Autonomous Discord configuration removed."));

Console.WriteLine($"CyRevision Discord agent listening on {options.ListenUrl}");
if (options.ListenUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
    !options.ListenUrl.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase) &&
    !options.ListenUrl.Contains("localhost", StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine("Remote HTTP is enabled: use this only through a trusted WireGuard VPN.");
}

await app.RunAsync();
return 0;

static async Task<IResult> ExecuteCommandAsync(Func<Task> command, string successMessage)
{
    try
    {
        await command();
        return Results.Json(new DiscordAgentCommandResult(true, successMessage));
    }
    catch (KeyNotFoundException exception)
    {
        return Results.NotFound(new DiscordAgentCommandResult(false, exception.Message));
    }
    catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException or
                                      DirectoryNotFoundException or ArgumentException)
    {
        return Results.BadRequest(new DiscordAgentCommandResult(false, exception.Message));
    }
}

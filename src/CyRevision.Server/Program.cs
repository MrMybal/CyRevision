using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics;
using CyRevision.Core.Configuration;
using CyRevision.Core.Projects;
using CyRevision.Git;
using CyRevision.Security;
using CyRevision.Server;
using CyRevision.Sync;
using CyRevision.Vpn;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options => options.SingleLine = true);
builder.Logging.AddDebug();
ServerOptions options = ServerOptions.Create(builder.Configuration, builder.Environment);
options.EnsureDirectories();

builder.Services.AddSingleton(options);
builder.Services.AddSingleton<IProjectCatalog>(_ => new JsonProjectCatalog(options.ProjectCatalogPath));
builder.Services.AddSingleton<IGitRepositoryService, GitCliRepositoryService>();
builder.Services.AddSingleton<IGitPeerExchangeService, GitPeerExchangeService>();
builder.Services.AddSingleton<ISyncthingProfileStore>(_ => new JsonSyncthingProfileStore(options.SyncthingDirectory));
builder.Services.AddSingleton<IVpnProfileStore>(_ => new JsonVpnProfileStore(options.VpnDirectory));
builder.Services.AddSingleton<WireGuardKeyService>();
builder.Services.AddSingleton(_ => new WireGuardConfigService(options.VpnDirectory));
builder.Services.AddSingleton(provider => new ManagedWireGuardEngine(
    options.VpnDirectory,
    provider.GetRequiredService<WireGuardConfigService>()));
builder.Services.AddSingleton<ServerRuntime>();
builder.Services.AddHostedService<BackupSchedulerService>();
builder.Services.AddHostedService<GitExchangeSchedulerService>();
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(json => json.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

WebApplication app = builder.Build();
app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    Exception exception = context.Features.Get<IExceptionHandlerFeature>()?.Error
                          ?? new InvalidOperationException("Unknown server error.");
    context.Response.StatusCode = exception switch
    {
        KeyNotFoundException => StatusCodes.Status404NotFound,
        UnauthorizedAccessException => StatusCodes.Status403Forbidden,
        ArgumentException or InvalidOperationException => StatusCodes.Status400BadRequest,
        _ => StatusCodes.Status500InternalServerError
    };
    await Results.Problem(
            statusCode: context.Response.StatusCode,
            title: "CyRevision Server",
            detail: exception.Message)
        .ExecuteAsync(context);
}));
app.UseDefaultFiles();
app.UseStaticFiles();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api/v1") &&
        !context.Request.Path.StartsWithSegments("/api/v1/capabilities"))
    {
        string supplied = context.Request.Headers.Authorization.ToString();
        if (supplied.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            supplied = supplied[7..].Trim();
        }

        byte[] expected = Encoding.UTF8.GetBytes(options.ApiToken);
        byte[] actual = Encoding.UTF8.GetBytes(supplied);
        if (expected.Length != actual.Length || !CryptographicOperations.FixedTimeEquals(expected, actual))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Jeton CyRevision invalide." });
            return;
        }
    }

    await next();
});

app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    service = "CyRevision.Server",
    optionalPeer = true,
    utc = DateTimeOffset.UtcNow
}));

app.MapGet("/api/v1/capabilities", () => Results.Ok(new
{
    optionalLinuxPeer = true,
    dashboard = true,
    git = true,
    lfs = true,
    signedGitBundles = true,
    peerSync = true,
    backup = true,
    wireGuardVpn = true,
    vpnOnlyPeers = true,
    unrealSwarmPreset = true,
    presets = ProjectPresets.All.Select(preset => new { preset.Kind, preset.Name, preset.Description })
}));

app.MapGet("/api/v1/projects", async (IProjectCatalog catalog, CancellationToken cancellationToken) =>
    Results.Ok(await catalog.GetAllAsync(cancellationToken)));

app.MapPost("/api/v1/projects", async (
    CreateServerProjectRequest request,
    ServerRuntime runtime,
    CancellationToken cancellationToken) =>
{
    ProjectDefinition project = await runtime.CreateProjectAsync(request, cancellationToken);
    return Results.Created($"/api/v1/projects/{project.Id}", project);
});

app.MapDelete("/api/v1/projects/{projectId:guid}", async (
    Guid projectId,
    ServerRuntime runtime,
    CancellationToken cancellationToken) =>
{
    await runtime.RemoveProjectAsync(projectId, cancellationToken);
    return Results.NoContent();
});

app.MapGet("/api/v1/projects/{projectId:guid}/git/status", async (
    Guid projectId,
    ServerRuntime runtime,
    CancellationToken cancellationToken) =>
{
    GitRepositoryStatus? status = await runtime.GetGitStatusAsync(projectId, cancellationToken);
    return status is null ? Results.NoContent() : Results.Ok(status);
});

app.MapGet("/api/v1/projects/{projectId:guid}/git/history", async (
    Guid projectId,
    ServerRuntime runtime,
    CancellationToken cancellationToken) =>
    Results.Ok(await runtime.GetGitHistoryAsync(projectId, cancellationToken)));

app.MapGet("/api/v1/projects/{projectId:guid}/backups", async (
    Guid projectId,
    ServerRuntime runtime,
    CancellationToken cancellationToken) =>
    Results.Ok(await runtime.GetBackupsAsync(projectId, cancellationToken)));

app.MapPost("/api/v1/projects/{projectId:guid}/backups", async (
    Guid projectId,
    ServerRuntime runtime,
    CancellationToken cancellationToken) =>
    Results.Ok(await runtime.CreateBackupAsync(projectId, cancellationToken)));

app.MapPost("/api/v1/projects/{projectId:guid}/sync/configure", async (
    Guid projectId,
    ConfigureServerSyncRequest request,
    ServerRuntime runtime,
    CancellationToken cancellationToken) =>
    Results.Ok(await runtime.ConfigureSyncAsync(projectId, request.ExecutablePath, cancellationToken)));

app.MapPost("/api/v1/projects/{projectId:guid}/sync/start", async (
    Guid projectId,
    ServerRuntime runtime,
    CancellationToken cancellationToken) =>
    Results.Ok(await runtime.StartSyncAsync(projectId, cancellationToken)));

app.MapPost("/api/v1/projects/{projectId:guid}/sync/pause", async (
    Guid projectId,
    ServerRuntime runtime,
    CancellationToken cancellationToken) =>
    Results.Ok(await runtime.PauseSyncAsync(projectId, cancellationToken)));

app.MapPost("/api/v1/projects/{projectId:guid}/sync/stop", async (
    Guid projectId,
    ServerRuntime runtime,
    CancellationToken cancellationToken) =>
    Results.Ok(await runtime.StopSyncAsync(projectId, cancellationToken)));

app.MapGet("/api/v1/projects/{projectId:guid}/sync/status", async (
    Guid projectId,
    ServerRuntime runtime,
    CancellationToken cancellationToken) =>
    Results.Ok(await runtime.GetSyncStatusAsync(projectId, cancellationToken)));

app.MapPost("/api/v1/projects/{projectId:guid}/vpn/configure", async (
    Guid projectId,
    ConfigureServerVpnRequest request,
    ServerRuntime runtime,
    CancellationToken cancellationToken) =>
    Results.Ok(await runtime.ConfigureVpnAsync(projectId, request, cancellationToken)));

app.MapGet("/api/v1/projects/{projectId:guid}/vpn/profile", async (
    Guid projectId,
    ServerRuntime runtime,
    CancellationToken cancellationToken) =>
    Results.Ok(await runtime.GetVpnProfileAsync(projectId, cancellationToken)));

app.MapGet("/api/v1/projects/{projectId:guid}/vpn/status", async (
    Guid projectId,
    ServerRuntime runtime,
    CancellationToken cancellationToken) =>
    Results.Ok(await runtime.GetVpnStatusAsync(projectId, cancellationToken)));

app.MapPost("/api/v1/projects/{projectId:guid}/vpn/start", async (
    Guid projectId,
    ServerRuntime runtime,
    CancellationToken cancellationToken) =>
    Results.Ok(await runtime.StartVpnAsync(projectId, cancellationToken)));

app.MapPost("/api/v1/projects/{projectId:guid}/vpn/stop", async (
    Guid projectId,
    ServerRuntime runtime,
    CancellationToken cancellationToken) =>
    Results.Ok(await runtime.StopVpnAsync(projectId, cancellationToken)));

app.MapPost("/api/v1/projects/{projectId:guid}/vpn/invitations", async (
    Guid projectId,
    CreateVpnInvitationRequest request,
    ServerRuntime runtime,
    CancellationToken cancellationToken) =>
{
    SignedVpnInvitation invitation = await runtime.CreateVpnInvitationAsync(projectId, request.Capabilities, cancellationToken);
    return Results.Ok(new { exchangeText = VpnPeerExchangeCodec.ExportInvitation(invitation), invitation.Invitation.ExpiresAt });
});

app.MapPost("/api/v1/projects/{projectId:guid}/vpn/join", async (
    Guid projectId,
    VpnPeerExchangeRequest request,
    ServerRuntime runtime,
    CancellationToken cancellationToken) =>
    Results.Ok(new { exchangeText = await runtime.JoinVpnInvitationAsync(projectId, request.ExchangeText, request.Capabilities, cancellationToken) }));

app.MapPost("/api/v1/projects/{projectId:guid}/vpn/accept", async (
    Guid projectId,
    PeerExchangeRequest request,
    ServerRuntime runtime,
    CancellationToken cancellationToken) =>
    Results.Ok(await runtime.AcceptVpnResponseAsync(projectId, request.ExchangeText, cancellationToken)));

app.MapDelete("/api/v1/projects/{projectId:guid}/vpn/peers/{peerId:guid}", async (
    Guid projectId,
    Guid peerId,
    ServerRuntime runtime,
    CancellationToken cancellationToken) =>
{
    await runtime.RemoveVpnPeerAsync(projectId, peerId, cancellationToken);
    return Results.NoContent();
});

app.MapPost("/api/v1/projects/{projectId:guid}/peers/invitations", async (
    Guid projectId,
    CreatePeerInvitationRequest request,
    ServerRuntime runtime,
    CancellationToken cancellationToken) =>
{
    PeerInvitationPackage package = await runtime.CreatePeerInvitationAsync(projectId, request.Role, cancellationToken);
    return Results.Ok(new
    {
        exchangeText = PeerExchangeCodec.ExportInvitation(package),
        verificationCode = package.VerificationCode,
        expiresAt = package.Invitation.ExpiresAt
    });
});

app.MapPost("/api/v1/projects/{projectId:guid}/peers/join-request", async (
    Guid projectId,
    PeerExchangeRequest request,
    ServerRuntime runtime,
    CancellationToken cancellationToken) =>
    Results.Ok(new { exchangeText = await runtime.PrepareJoinRequestAsync(projectId, request.ExchangeText, request.VerificationCode, cancellationToken) }));

app.MapPost("/api/v1/projects/{projectId:guid}/peers/approve", async (
    Guid projectId,
    PeerExchangeRequest request,
    ServerRuntime runtime,
    CancellationToken cancellationToken) =>
    Results.Ok(new { exchangeText = await runtime.ApproveJoinRequestAsync(projectId, request.ExchangeText, cancellationToken) }));

app.MapPost("/api/v1/projects/{projectId:guid}/peers/membership", async (
    Guid projectId,
    PeerExchangeRequest request,
    ServerRuntime runtime,
    CancellationToken cancellationToken) =>
    Results.Ok(await runtime.ImportMembershipGrantAsync(projectId, request.ExchangeText, cancellationToken)));

app.MapGet("/api/v1/projects/{projectId:guid}/peers", async (
    Guid projectId,
    ServerRuntime runtime,
    CancellationToken cancellationToken) =>
    Results.Ok(await runtime.GetMembersAsync(projectId, cancellationToken)));

app.MapDelete("/api/v1/projects/{projectId:guid}/peers/{deviceId:guid}", async (
    Guid projectId,
    Guid deviceId,
    ServerRuntime runtime,
    CancellationToken cancellationToken) =>
{
    await runtime.RevokePeerAsync(projectId, deviceId, cancellationToken);
    return Results.NoContent();
});

app.MapPost("/api/v1/projects/{projectId:guid}/git/exchange", async (
    Guid projectId,
    ServerRuntime runtime,
    CancellationToken cancellationToken) =>
    Results.Ok(await runtime.ExchangeGitAsync(projectId, cancellationToken)));

app.MapFallbackToFile("index.html");
app.Run();

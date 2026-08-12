using System.Diagnostics;
using System.Reflection;
using CyRevision.Build.Agent;
using CyRevision.RemoteBuild;
using Microsoft.AspNetCore.Http.Features;

BuildAgentOptions options;
try
{
    options = BuildAgentOptions.Parse(args);
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    Console.Error.WriteLine(BuildAgentOptions.HelpText);
    return 2;
}

if (options.ShowHelp)
{
    Console.WriteLine(BuildAgentOptions.HelpText);
    return 0;
}

Directory.CreateDirectory(options.DataDirectory);
(RemoteBuildAgentConfiguration configuration, bool configurationCreated) =
    await BuildAgentConfigurationStore.LoadOrCreateAsync(options.ConfigurationFile, options.DataDirectory);
(string token, bool tokenCreated) = await RemoteBuildTokenProvider.LoadOrCreateAsync(options.TokenFile);
if (configurationCreated)
    Console.WriteLine($"A safe empty build configuration was created at: {options.ConfigurationFile}");
if (tokenCreated || options.PrintToken)
{
    Console.WriteLine(tokenCreated ? "A new remote build token was created:" : "Remote build token:");
    Console.WriteLine(token);
}

long maximumRequest = Math.Max(16L * 1024 * 1024,
    configuration.Projects.Select(project => project.MaximumSnapshotBytes).DefaultIfEmpty(16L * 1024 * 1024).Max() + 1024 * 1024);
WebApplicationBuilder builder = WebApplication.CreateBuilder(Array.Empty<string>());
builder.WebHost.UseUrls(options.ListenUrl);
builder.WebHost.ConfigureKestrel(server => server.Limits.MaxRequestBodySize = maximumRequest);
builder.Services.Configure<FormOptions>(form => form.MultipartBodyLengthLimit = maximumRequest);
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(console =>
{
    console.SingleLine = true;
    console.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
});

WebApplication app = builder.Build();
DateTimeOffset startedAt = DateTimeOffset.UtcNow;
string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
await using RemoteBuildJobCoordinator jobs = new(configuration.JobsRoot, configuration.MaximumParallelJobs);

app.Use(async (context, next) =>
{
    if (!RemoteBuildTokenProvider.IsValid(token, context.Request.Headers.Authorization.ToString()))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { error = "A valid remote build bearer token is required." });
        return;
    }
    context.Response.Headers.CacheControl = "no-store";
    await next();
});

app.MapGet("/api/v1/health", () => Results.Json(new RemoteBuildAgentStatus(
    "CyRevision.Build.Agent", version, startedAt, jobs.RunningCount, configuration.Projects.Count)));

app.MapGet("/api/v1/projects", () => Results.Json(configuration.Projects.Select(project =>
    new RemoteBuildProjectDescriptor(
        project.ProjectId,
        project.ProjectName,
        project.AllowUploadedSnapshots,
        project.Recipes.Select(recipe => new RemoteBuildRecipeDescriptor(
            recipe.Id, recipe.DisplayName, recipe.TimeoutMinutes)).ToArray())).ToArray()));

app.MapPost("/api/v1/projects/{projectId:guid}/builds", async (
    Guid projectId,
    HttpRequest request,
    CancellationToken cancellationToken) =>
{
    RemoteBuildAgentProject? project = configuration.Projects.FirstOrDefault(item => item.ProjectId == projectId);
    if (project is null)
        return Results.NotFound(new { error = "Build project is not configured on this agent." });
    if (!request.HasFormContentType)
        return Results.BadRequest(new { error = "Build requests must use multipart/form-data." });
    IFormCollection form = await request.ReadFormAsync(cancellationToken);
    string recipeId = form["recipeId"].ToString();
    RemoteBuildRecipe? recipe = project.Recipes.FirstOrDefault(item =>
        string.Equals(item.Id, recipeId, StringComparison.OrdinalIgnoreCase));
    if (recipe is null)
        return Results.BadRequest(new { error = "The requested recipe is not allowlisted on this agent." });
    if (!Enum.TryParse(form["sourceMode"].ToString(), true, out RemoteBuildSourceMode sourceMode))
        return Results.BadRequest(new { error = "Unknown remote build source mode." });

    string? snapshotPath = null;
    try
    {
        if (sourceMode == RemoteBuildSourceMode.UploadedSnapshot)
        {
            if (!project.AllowUploadedSnapshots)
                return Results.BadRequest(new { error = "Uploaded snapshots are disabled for this project." });
            IFormFile? snapshot = form.Files.GetFile("snapshot");
            if (snapshot is null || snapshot.Length == 0 || snapshot.Length > project.MaximumSnapshotBytes)
                return Results.BadRequest(new { error = "The uploaded snapshot is missing or exceeds the project limit." });
            string uploads = Path.Combine(configuration.JobsRoot, "uploads");
            Directory.CreateDirectory(uploads);
            snapshotPath = Path.Combine(uploads, Guid.NewGuid().ToString("N") + ".zip");
            await using FileStream output = new(snapshotPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, true);
            await snapshot.CopyToAsync(output, cancellationToken);
        }
        else
        {
            string expectedRevision = form["expectedRevision"].ToString().Trim();
            string? actualRevision = await TryReadRevisionAsync(project.WorkspaceRoot, cancellationToken);
            if (!string.IsNullOrWhiteSpace(expectedRevision) && actualRevision is not null &&
                !string.Equals(expectedRevision, actualRevision, StringComparison.OrdinalIgnoreCase))
                return Results.Conflict(new
                {
                    error = $"Agent workspace is at {actualRevision[..Math.Min(12, actualRevision.Length)]}, not requested revision {expectedRevision[..Math.Min(12, expectedRevision.Length)]}. Synchronize it first or upload a snapshot."
                });
        }

        return Results.Accepted(value: jobs.Start(project, recipe, sourceMode, snapshotPath));
    }
    catch
    {
        if (snapshotPath is not null)
            File.Delete(snapshotPath);
        throw;
    }
});

app.MapGet("/api/v1/projects/{projectId:guid}/builds/{jobId:guid}", (Guid projectId, Guid jobId) =>
{
    RemoteBuildJobStatus? status = jobs.Get(jobId);
    return status is null || status.ProjectId != projectId ? Results.NotFound() : Results.Json(status);
});

app.MapGet("/api/v1/projects/{projectId:guid}/builds/{jobId:guid}/artifacts", (Guid projectId, Guid jobId) =>
{
    RemoteBuildJobStatus? status = jobs.Get(jobId);
    string? path = status?.ProjectId == projectId ? jobs.GetArtifactPath(jobId) : null;
    return path is null
        ? Results.NotFound(new { error = "Build artifacts are not available." })
        : Results.File(path, "application/zip", $"CyRevision-build-{jobId:N}.zip", enableRangeProcessing: true);
});

app.MapDelete("/api/v1/projects/{projectId:guid}/builds/{jobId:guid}", (Guid projectId, Guid jobId) =>
{
    RemoteBuildJobStatus? status = jobs.Get(jobId);
    return status is null || status.ProjectId != projectId || !jobs.Cancel(jobId)
        ? Results.NotFound()
        : Results.Ok(new { message = "Cancellation requested." });
});

Console.WriteLine($"CyRevision remote build agent listening on {options.ListenUrl}");
Console.WriteLine($"Configured projects: {configuration.Projects.Count}");
if (options.ListenUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !options.ListenUrl.Contains("127.0.0.1"))
    Console.WriteLine("Plain HTTP is enabled. Restrict this port to the WireGuard interface and VPN subnet.");
await app.RunAsync();
return 0;

static async Task<string?> TryReadRevisionAsync(string workspace, CancellationToken cancellationToken)
{
    if (!Directory.Exists(Path.Combine(Path.GetFullPath(workspace), ".git")))
        return null;
    ProcessStartInfo start = new("git")
    {
        WorkingDirectory = Path.GetFullPath(workspace),
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true
    };
    start.ArgumentList.Add("rev-parse");
    start.ArgumentList.Add("HEAD");
    using Process process = Process.Start(start) ?? throw new InvalidOperationException("Unable to inspect agent workspace revision.");
    string output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
    await process.WaitForExitAsync(cancellationToken);
    return process.ExitCode == 0 ? output.Trim() : null;
}

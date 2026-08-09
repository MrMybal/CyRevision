using CyRevision.Core.Configuration;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(ProjectPresets.All);

WebApplication app = builder.Build();

app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    service = "CyRevision.Server",
    syncRequired = false,
    utc = DateTimeOffset.UtcNow
}));

app.MapGet("/api/v1/capabilities", (IReadOnlyList<ProjectPreset> presets) => Results.Ok(new
{
    optionalLinuxPeer = true,
    git = true,
    lfs = true,
    peerSync = true,
    backup = true,
    presets = presets.Select(preset => new { preset.Kind, preset.Name, preset.Description })
}));

app.Run();


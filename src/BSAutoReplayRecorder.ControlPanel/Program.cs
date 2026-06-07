using BSAutoReplayRecorder.ControlPanel;
using System.Diagnostics;

var settings = LocalSettingsFile.LoadOrDefault();
var bindUrl = Environment.GetEnvironmentVariable("BSARR_CONTROL_PANEL_URL");
if (!string.IsNullOrWhiteSpace(bindUrl))
{
    settings.BindUrl = bindUrl;
}

var workspaceDirectory = Environment.GetEnvironmentVariable("BSARR_CONTROL_PANEL_WORKSPACE");
if (!string.IsNullOrWhiteSpace(workspaceDirectory))
{
    settings.WorkspaceDirectory = workspaceDirectory;
}

settings.Normalize();

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(settings.BindUrl);
builder.Services.AddSingleton(settings);
builder.Services.AddSingleton<IRecordingAudioVerifier, FfprobeRecordingAudioVerifier>();
builder.Services.AddSingleton<IBeatSaverMapDownloader>(_ => new BeatSaverMapDownloader(new HttpClient()));
builder.Services.AddSingleton<IScoreSaberReplayDownloader>(_ => new ScoreSaberReplayDownloader(new HttpClient()));
builder.Services.AddSingleton<IDisplayInfoProvider, WindowsDisplayInfoProvider>();
builder.Services.AddSingleton<ControlPanelStore>();
builder.Services.AddSingleton<IStackShutdownLauncher, StopScriptShutdownLauncher>();
builder.Services.AddHostedService<IdleShutdownHostedService>();

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/state", (ControlPanelStore store) => Results.Ok(store.Snapshot()));
app.MapGet("/api/displays", (IDisplayInfoProvider displays) => Results.Ok(displays.GetDisplays()));

app.MapPost("/api/settings", (SettingsUpdateRequest request, ControlPanelStore store) =>
{
    var state = store.UpdateSettings(request);
    return Results.Ok(state);
});

app.MapPost("/api/replays/import", async (HttpRequest request, ControlPanelStore store) =>
{
    if (!request.HasFormContentType)
    {
        return Results.BadRequest(new { error = "Expected multipart/form-data." });
    }

    var form = await request.ReadFormAsync().ConfigureAwait(false);
    var imported = store.ImportFiles(form.Files);
    return Results.Ok(new { count = imported.Count, state = store.Snapshot() });
});
app.MapPost("/api/replays/import-references", async (ReplayReferenceImportRequest request, ControlPanelStore store, CancellationToken cancellationToken) =>
{
    var imported = await store.ImportReferencesAsync(request, cancellationToken).ConfigureAwait(false);
    return Results.Ok(new { count = imported.Count, state = store.Snapshot() });
});

app.MapPost("/api/queue/clear", (ControlPanelStore store) => Results.Ok(store.ClearQueue()));
app.MapPost("/api/queue/{id}/edit", (string id, QueueItemUpdateRequest request, ControlPanelStore store) =>
    ExecuteApi(() => store.UpdateQueueItem(id, request)));
app.MapPost("/api/queue/{id}/calibration", (string id, ReplayCalibrationRequest request, ControlPanelStore store) =>
    ExecuteApi(() => store.UpdateReplayCalibration(id, request)));
app.MapPost("/api/queue/{id}/move-up", (string id, ControlPanelStore store) =>
    ExecuteApi(() => store.MoveQueueItem(id, -1)));
app.MapPost("/api/queue/{id}/move-down", (string id, ControlPanelStore store) =>
    ExecuteApi(() => store.MoveQueueItem(id, 1)));
app.MapPost("/api/queue/{id}/requeue", (string id, ControlPanelStore store) =>
    ExecuteApi(() => store.RequeueQueueItem(id)));
app.MapPost("/api/queue/{id}/remove", (string id, ControlPanelStore store) =>
    ExecuteApi(() => store.RemoveQueueItem(id)));
app.MapPost("/api/queue/{id}/map/download", (string id, ControlPanelStore store) =>
    ExecuteApi(() => store.DownloadQueueMap(id)));
app.MapPost("/api/queue/{id}/map/upload", async (string id, HttpRequest request, ControlPanelStore store) =>
{
    if (!request.HasFormContentType)
    {
        return Results.BadRequest(new { error = "Expected multipart/form-data." });
    }

    var form = await request.ReadFormAsync().ConfigureAwait(false);
    var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
    return ExecuteApi(() => store.ImportQueueMap(id, file!));
});
app.MapGet("/api/queue/{id}/recording", (string id, ControlPanelStore store) =>
    ExecuteRedirect(() => store.GetRecordedFileUri(id)));
app.MapPost("/api/queue/{id}/recording/open", (string id, ControlPanelStore store) =>
    ExecuteApi(() =>
    {
        var path = OpenRecordedFileInExplorer(store.GetRecordedFilePath(id));
        return new { status = "Recording opened in File Explorer", path };
    }));
app.MapGet("/api/queue/{id}/cover", (string id, ControlPanelStore store) =>
    ExecutePhysicalFile(() => store.GetQueueCoverPath(id)));
app.MapPost("/api/run/start", (ControlPanelStore store) =>
    ExecuteApi(() => store.StartRun()));
app.MapPost("/api/run/stop", (ControlPanelStore store) => Results.Ok(store.StopRun()));
app.MapPost("/api/quit", (ControlPanelStore store, IStackShutdownLauncher shutdownLauncher) =>
    ExecuteApi(() =>
    {
        store.StopRun();
        shutdownLauncher.StartStopScript(stopGames: true);
        return new { status = "Quit requested" };
    }));
app.MapPost("/api/instances/launch", (ControlPanelStore store) =>
    ExecuteApi(() => store.LaunchAllInstances()));
app.MapPost("/api/instances/{index:int}/launch", (int index, ControlPanelStore store) =>
    ExecuteApi(() => store.LaunchInstance(index)));
app.MapPost("/api/instances/{index:int}/quit", (int index, ControlPanelStore store) =>
    ExecuteApi(() => store.QuitInstance(index)));
app.MapPost("/api/instances/{index:int}/enabled", (int index, InstanceEnabledRequest request, ControlPanelStore store) =>
    ExecuteApi(() => store.SetInstanceEnabled(index, request.Enabled)));
app.MapPost("/api/instances/{index:int}/remove", (int index, ControlPanelStore store) =>
    ExecuteApi(() => store.RemoveManagedInstance(index)));
app.MapPost("/api/instances/provision", (InstanceProvisionRequest request, ControlPanelStore store) =>
    ExecuteApi(() => store.ProvisionManagedInstances(request)));
app.MapPost("/api/instances/baseline/check", (ControlPanelStore store) =>
    ExecuteApi(() => store.CheckInstanceBaseline()));
app.MapPost("/api/song-folders/check", (ControlPanelStore store) =>
    ExecuteApi(() => store.CheckSongFolderLinks()));
app.MapPost("/api/song-folders/repair", (ControlPanelStore store) =>
    ExecuteApi(() => store.RepairSongFolderLinks()));

app.MapPost("/api/workers/register", (WorkerRegisterRequest request, ControlPanelStore store) =>
    ExecuteApi(() => store.RegisterWorker(request)));

app.MapPost("/api/workers/heartbeat", (WorkerHeartbeatRequest request, ControlPanelStore store) =>
    ExecuteApi(() => store.Heartbeat(request)));

app.MapGet("/api/workers/{workerId}/assignment", (string workerId, ControlPanelStore store) =>
    ExecuteApi(() => store.GetAssignment(workerId)));

app.MapPost("/api/workers/report", (WorkerReportRequest request, ControlPanelStore store) =>
    ExecuteApi(() => store.ReportAssignment(request)));

app.Logger.LogInformation("Recorder control panel listening on {BindUrl}", settings.BindUrl);
app.Logger.LogInformation("Open {BindUrl} in a browser.", settings.BindUrl);
await app.RunAsync().ConfigureAwait(false);

static IResult ExecuteApi<T>(Func<T> action)
{
    try
    {
        return Results.Ok(action());
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}

static IResult ExecuteRedirect(Func<string> action)
{
    try
    {
        return Results.Redirect(action());
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}

static IResult ExecutePhysicalFile(Func<string> action)
{
    try
    {
        var path = action();
        return Results.File(File.ReadAllBytes(path), GetImageContentType(path));
    }
    catch (InvalidOperationException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
}

static string GetImageContentType(string path)
{
    var extension = Path.GetExtension(path);
    if (string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase))
    {
        return "image/png";
    }

    return "image/jpeg";
}

static string OpenRecordedFileInExplorer(string path)
{
    var fullPath = Path.GetFullPath(path);
    if (!File.Exists(fullPath))
    {
        throw new InvalidOperationException("Recorded file was not found: " + fullPath);
    }

    if (!OperatingSystem.IsWindows())
    {
        throw new InvalidOperationException("Opening recordings in File Explorer is only supported on Windows.");
    }

    var startInfo = new ProcessStartInfo
    {
        FileName = "explorer.exe",
        UseShellExecute = false,
        CreateNoWindow = true
    };
    startInfo.ArgumentList.Add("/select," + fullPath);

    try
    {
        Process.Start(startInfo);
    }
    catch (Exception ex)
    {
        throw new InvalidOperationException("Could not open File Explorer for recorded file: " + ex.Message, ex);
    }

    return fullPath;
}

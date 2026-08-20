using NexaPlay;
using NexaPlay.Services;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

var root = Path.Combine(Path.GetTempPath(), "NexaPlay-Smoke-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var clean = DownloadService.SafeName("My: Game? <Test>");
    Require(!clean.Any(Path.GetInvalidFileNameChars().Contains), "Filename sanitization failed.");
    var keySettings = new AppSettings();
    var keyService = new SettingsService();
    const string testArtworkKey = "steamgriddb-owner-only-test-key-123456";
    keyService.SetSteamGridDbKey(keySettings, testArtworkKey);
    Require(keySettings.ProtectedSteamGridDbKey.Length > 0 && !keySettings.ProtectedSteamGridDbKey.Contains(testArtworkKey, StringComparison.Ordinal) && keyService.GetSteamGridDbKey(keySettings) == testArtworkKey, "SteamGridDB key encryption round-trip failed.");
    var catalog = new CatalogDocument { Games = [new GameEntry { Title = "Test Game", DownloadUrl = "https://example.test/game.zip", UpdateDownloadUrl = "https://example.test/update.zip", OnlineFixDownloadUrl = "https://example.test/fix.zip", Tags = ["CO-OP"], IsMultiplayer = true, CommunityRating = 4.7, RatingCount = 120 }] };
    CatalogService.Validate(catalog);
    var duplicateRejected = false;
    try { CatalogService.Validate(new CatalogDocument { Games = [new GameEntry { Id = "same", Title = "One" }, new GameEntry { Id = "same", Title = "Two" }] }); }
    catch (InvalidDataException) { duplicateRejected = true; }
    Require(duplicateRejected, "Duplicate catalog IDs were not rejected.");
    var jsonPath = Path.Combine(root, "catalog.json");
    await JsonFile.WriteAtomicAsync(jsonPath, catalog);
    var loaded = await JsonFile.ReadAsync<CatalogDocument>(jsonPath);
    Require(loaded?.Games.Single().Title == "Test Game", "Atomic catalog round-trip failed.");
    var ownerCatalogPath = Path.Combine(root, "owner-catalog.json");
    var ownerCatalogService = new CatalogService(new HttpClient(), ownerCatalogPath);
    await ownerCatalogService.SaveAsync(catalog);
    Require(File.Exists(ownerCatalogPath) && (await ownerCatalogService.LoadAsync()).Games.Single().Title == "Test Game", "Separate Owner Studio draft catalog failed.");
    var remoteStamp = new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);
    var remoteCatalog = new CatalogDocument { UpdatedUtc = remoteStamp, Games = [new GameEntry { Title = "Remote Game" }] };
    var remoteResponse = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(remoteCatalog, JsonFile.Options)) };
    remoteResponse.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
    var remoteCatalogPath = Path.Combine(root, "remote-catalog.json");
    var syncedCatalog = await new CatalogService(new HttpClient(new FakeHandler(remoteResponse)), remoteCatalogPath).SyncAsync("https://example.test/catalog.json");
    Require(syncedCatalog.UpdatedUtc == remoteStamp, "Catalog sync replaced the publisher timestamp with the local refresh time.");
    var githubResponse = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(remoteCatalog, JsonFile.Options)) };
    githubResponse.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
    var githubHandler = new CapturingHandler(githubResponse);
    var githubCatalogPath = Path.Combine(root, "github-catalog.json");
    var githubSyncedCatalog = await new CatalogService(new HttpClient(githubHandler), githubCatalogPath).SyncAsync("https://raw.githubusercontent.com/example-owner/example-repo/main/catalog/nexaplay-catalog.json", preferGitHubApi: true);
    Require(githubSyncedCatalog.Games.Single().Title == "Remote Game" && githubHandler.RequestUri?.Host == "api.github.com" && githubHandler.RequestUri.AbsolutePath == "/repos/example-owner/example-repo/contents/catalog/nexaplay-catalog.json", "Fresh GitHub catalog sync did not use the Contents API endpoint.");

    var archive = Path.Combine(root, "test.zip");
    using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
    {
        var entry = zip.CreateEntry("TestGame/TestGame.exe");
        await using (var stream = entry.Open())
            await stream.WriteAsync(new byte[] { 0x4D, 0x5A, 0x00, 0x00 });
        var runMe = zip.CreateEntry("TestGame/Run Me!.bat");
        await using (var runStream = runMe.Open())
            await runStream.WriteAsync("@echo off\r\nstart TestGame.exe\r\n"u8.ToArray());
    }
    var library = Path.Combine(root, "library");
    var game = new GameEntry { Title = "Test Game", Id = "test", ExecutablePath = "TestGame/TestGame.exe" };
    var install = new InstallService();
    var extractionProgress = new ProgressRecorder();
    var installed = await install.ExtractAsync(game, archive, library, extractionProgress, CancellationToken.None);
    Require(File.Exists(Path.Combine(installed, ".nexaplay.json")), "Install manifest was not created.");
    Require(extractionProgress.Events.Any(p => p.Message.StartsWith("Extracting ") && p.Message.Contains(" / ")), "Byte-level extraction progress was not reported.");
    var manifest = await JsonFile.ReadAsync<InstallManifest>(Path.Combine(installed, ".nexaplay.json"));
    Require(manifest?.ExecutablePath.EndsWith("Run Me!.bat", StringComparison.OrdinalIgnoreCase) == true, "Run Me!.bat was not selected as the preferred launcher.");
    InstallService.RefreshInstallState(game, library);
    Require(game.IsInstalled, "Installed-state detection failed.");
    game.NotifyRuntimeChanged();
    Require(game.ActionLabel == "Play", "Installed game card did not switch to Play.");

    var interrupted = new GameEntry { Title = "Interrupted", Id = "interrupted" };
    var interruptedFolder = Path.Combine(library, "Interrupted");
    Directory.CreateDirectory(interruptedFolder);
    await JsonFile.WriteAtomicAsync(Path.Combine(interruptedFolder, ".nexaplay-installing.json"), new { StartedUtc = DateTime.UtcNow });
    InstallService.RefreshInstallState(interrupted, library);
    Require(interrupted.HasIncompleteInstall && !interrupted.IsInstalled, "Interrupted-install recovery state was not detected.");

    var cachedGame = new GameEntry { Title = "Cached", Id = "cached", DownloadUrl = "https://example.test/cached.zip", HasIncompleteInstall = true };
    var cachedFolder = Path.Combine(library, "_downloads");
    Directory.CreateDirectory(cachedFolder);
    var cachedArchive = Path.Combine(cachedFolder, "Cached-game.zip");
    await File.WriteAllBytesAsync(cachedArchive, "saved archive"u8.ToArray());
    var cachedProgress = new ProgressRecorder();
    var cachedDownloader = new DownloadService(new HttpClient(new ThrowingHandler()));
    var reusedArchive = await cachedDownloader.DownloadAsync(cachedGame, library, cachedProgress, CancellationToken.None);
    Require(reusedArchive == cachedArchive && cachedProgress.Events.Any(p => p.Message.StartsWith("Using saved archive")), "Completed archive was not reused for install recovery.");

    var updateArchive = Path.Combine(root, "update.zip");
    using (var zip = ZipFile.Open(updateArchive, ZipArchiveMode.Create))
    {
        var entry = zip.CreateEntry("TestGame/updated.txt");
        await using var stream = entry.Open();
        await stream.WriteAsync("updated"u8.ToArray());
    }
    game.Version = "2.0";
    await install.ApplyPackageAsync(game, DownloadPackageKind.Update, updateArchive, new Progress<DownloadProgress>(), CancellationToken.None);
    InstallService.RefreshInstallState(game, library);
    Require(game.InstalledVersion == "2.0" && !game.HasUpdate, "Update package did not advance the installed manifest version.");
    Require(File.Exists(Path.Combine(installed, "TestGame", "updated.txt")), "Update package was not extracted into the installed game folder.");

    var externalGameFolder = Path.Combine(root, "already-owned-game");
    Directory.CreateDirectory(externalGameFolder);
    await install.ApplyPackageToFolderAsync(game, DownloadPackageKind.Update, updateArchive, externalGameFolder, new Progress<DownloadProgress>(), CancellationToken.None);
    Require(File.Exists(Path.Combine(externalGameFolder, "TestGame", "updated.txt")), "A standalone update could not be applied to a user-selected game folder.");
    Require(!File.Exists(Path.Combine(externalGameFolder, ".nexaplay.json")), "NexaPlay incorrectly claimed ownership of an external game folder.");

    var htmlHandler = new FakeHandler(new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StringContent("<html>landing page</html>") { Headers = { ContentType = new MediaTypeHeaderValue("text/html") } }
    });
    var downloader = new DownloadService(new HttpClient(htmlHandler));
    var rejected = false;
    try
    {
        await downloader.DownloadAsync(new GameEntry { Title = "Landing", Id = "landing", DownloadUrl = "https://example.test/share" }, library, new Progress<DownloadProgress>(), CancellationToken.None);
    }
    catch (InvalidDataException) { rejected = true; }
    Require(rejected, "HTML landing-page download was not rejected.");

    var speedContent = new ByteArrayContent(new byte[2 * 1024 * 1024]);
    speedContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
    var speedRecorder = new ProgressRecorder();
    var speedDownloader = new DownloadService(new HttpClient(new FakeHandler(new HttpResponseMessage(HttpStatusCode.OK) { Content = speedContent })));
    await speedDownloader.DownloadAsync(new GameEntry { Title = "Speed Test", Id = "speed-test", DownloadUrl = "https://example.test/speed-test.zip" }, library, speedRecorder, CancellationToken.None);
    Require(speedRecorder.Events.Last().Percent == 100 && speedRecorder.Events.Last().BytesPerSecond > 0 && speedRecorder.Events.Last().EstimatedFinishLocal is not null, "Live download speed and completion estimate were not reported.");

    Require(SystemRequirementsService.DetectCpu().Length > 0, "CPU detection failed.");
    var detectedGpu = SystemRequirementsService.DetectGpu();
    Require(detectedGpu.Length > 0, "GPU detection failed.");
    Require(!SystemRequirementsService.IsVirtualGpu(detectedGpu), $"A virtual GPU was selected: {detectedGpu}");
    Require(SystemRequirementsService.DetectRamGb() > 0 && SystemRequirementsService.DetectOs().StartsWith("Windows", StringComparison.Ordinal), "RAM/Windows detection failed.");
    Require(SystemRequirementsService.GetTechnicalCityGameUrl("Forza Horizon 6") == "https://technical.city/en/system-requirements/forza-horizon-6" &&
        SystemRequirementsService.GetTechnicalCityGameUrl("PEAK") == "https://technical.city/en/system-requirements/peak", "Technical City game-link generation failed.");
    var bundledCatalogPath = Path.Combine(AppContext.BaseDirectory, "default-catalog.json");
    var bundledCatalog = await JsonFile.ReadAsync<CatalogDocument>(bundledCatalogPath);
    Require(bundledCatalog?.Games.Any(item => item.Title == "PEAK") == true && bundledCatalog.Games.Any(item => item.Title == "Forza Horizon 6"), "The friend installer catalog is missing the current games.");
    var transfer = new DownloadTaskItem { Key = "test", GameId = "test", GameTitle = "Test Game", CoverUrl = "", Kind = DownloadPackageKind.Game, BytesReceived = 1024 * 1024, TotalBytes = 2 * 1024 * 1024, BytesPerSecond = 1024 * 1024, Eta = TimeSpan.FromSeconds(1), EstimatedFinishLocal = DateTime.Now.AddSeconds(1) };
    Require(transfer.SpeedLabel == "1.0 MB/s" && transfer.EtaLabel == "1s" && transfer.TransferredLabel == "1.0 MB / 2.0 MB", "Download speed/ETA labels failed.");
    Exception? updateIconError = null;
    var updateIconLoaded = false;
    var iconThread = new Thread(() =>
    {
        try
        {
            _ = System.Windows.Application.Current ?? new System.Windows.Application();
            var updateIcon = new System.Windows.Media.Imaging.BitmapImage(new Uri(DownloadTaskItem.NexaPlayIconUri, UriKind.Absolute));
            updateIconLoaded = updateIcon.PixelWidth > 0 && updateIcon.PixelHeight > 0;
        }
        catch (Exception ex) { updateIconError = ex; }
    });
    iconThread.SetApartmentState(ApartmentState.STA);
    iconThread.Start();
    iconThread.Join();
    Require(updateIconLoaded, $"App update download icon could not be loaded: {updateIconError?.Message}");

    var updateChannelPath = Path.Combine(AppContext.BaseDirectory, "update-channel.json");
    var originalChannel = await File.ReadAllTextAsync(updateChannelPath);
    var bundledChannel = JsonSerializer.Deserialize<UpdateChannel>(originalChannel, JsonFile.Options);
    Require(bundledChannel?.ManifestUrl.Contains("githubusercontent.com", StringComparison.OrdinalIgnoreCase) == true && bundledChannel.CatalogUrl.Contains("githubusercontent.com", StringComparison.OrdinalIgnoreCase), "The friend build is missing its GitHub catalog/update channel.");
    try
    {
        await File.WriteAllTextAsync(updateChannelPath, JsonSerializer.Serialize(new UpdateChannel("https://updates.example.test/manifest.json"), JsonFile.Options));
        var updateJson = JsonSerializer.Serialize(new UpdateRelease("9.0.0", "https://updates.example.test/NexaPlay-Setup-v9.0.0.exe", new string('A', 64), "Smoke update"), JsonFile.Options);
        var updateResponse = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(updateJson) };
        updateResponse.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        var updateService = new UpdateService(new HttpClient(new FakeHandler(updateResponse)));
        var availableUpdate = await updateService.CheckAsync();
        Require(updateService.IsConfigured && availableUpdate?.Version == "9.0.0", "HTTPS update manifest detection failed.");
    }
    finally { await File.WriteAllTextAsync(updateChannelPath, originalChannel); }

    if (args.Contains("--live", StringComparer.OrdinalIgnoreCase))
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("NexaPlay-SmokeTests/1.0");
        var liveCatalogPath = Path.Combine(root, "live-github-catalog.json");
        var liveCatalog = await new CatalogService(http, liveCatalogPath).SyncAsync("https://raw.githubusercontent.com/itourboy-OG/NexaPlay/main/catalog/nexaplay-catalog.json", preferGitHubApi: true);
        Require(liveCatalog.Games.Any(game => game.Title == "Big Walk"), "Fresh GitHub catalog sync did not return the newly published game.");
        var metadata = await new SteamMetadataService(http).FetchAsync(620);
        Require(metadata.Title == "Portal 2" && metadata.Developer.Length > 0 && metadata.CoverUrl.StartsWith("https://") && !metadata.Description.Contains("&quot;"), "Live Steam metadata compatibility failed.");
        Require(metadata.IsMultiplayer && metadata.Tags.Any(tag => tag.Contains("Co-op", StringComparison.OrdinalIgnoreCase)), "Steam multiplayer/category automation failed.");
        Require(metadata.CommunityRating > 0 && metadata.RatingCount > 0, "Steam community rating automation failed.");
        Require(metadata.MinimumRequirements.Length > 0 && metadata.MinimumRamGb is > 0, "Steam system-requirements automation failed.");
        var match = await new SteamMetadataService(http).SearchAsync("Portal 2");
        Require(match.AppId == 620 && match.Metadata.Title == "Portal 2", "Live Steam title matching failed.");
    }

    Console.WriteLine($"Detected physical GPU: {detectedGpu}");
    Console.WriteLine($"NexaPlay smoke tests passed: safe paths, bundled friend catalog, installed Play action, independent external-folder update, download speed/ETA labels, HTTPS app-update manifest detection, expanded catalog validation, atomic catalog, byte-level ZIP extraction, Run Me launcher selection, synchronous installed-state refresh, interrupted-install detection, saved-archive reuse, in-place update manifest, landing-page rejection, CPU/GPU/RAM/Windows detection, and Technical City game links{(args.Contains("--live") ? ", live Steam ratings, multiplayer tags, requirements, screenshots, trailer metadata, and title search" : "")}.");
}
finally
{
    if (Directory.Exists(root)) Directory.Delete(root, true);
}

sealed class FakeHandler(HttpResponseMessage response) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(response);
}

sealed class ThrowingHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("The network should not be used when a completed archive already exists.");
}

sealed class CapturingHandler(HttpResponseMessage response) : HttpMessageHandler
{
    public Uri? RequestUri { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestUri = request.RequestUri;
        return Task.FromResult(response);
    }
}

sealed class ProgressRecorder : IProgress<DownloadProgress>
{
    public List<DownloadProgress> Events { get; } = [];
    public void Report(DownloadProgress value) => Events.Add(value);
}

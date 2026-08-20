using System.Net.Http;
using System.Net.Http.Headers;

namespace NexaPlay.Services;

public sealed class CatalogService(HttpClient httpClient, string? catalogFile = null)
{
    private readonly string _catalogFile = catalogFile ?? AppPaths.CatalogFile;

    public async Task<CatalogDocument> LoadAsync()
    {
        AppPaths.EnsureCreated();
        if (!File.Exists(_catalogFile))
        {
            var bundled = Path.Combine(AppContext.BaseDirectory, "default-catalog.json");
            var initial = await JsonFile.ReadAsync<CatalogDocument>(bundled) ?? new CatalogDocument();
            await SaveAsync(initial);
        }
        var document = await JsonFile.ReadAsync<CatalogDocument>(_catalogFile) ?? new CatalogDocument();
        Normalize(document);
        return document;
    }

    public Task SaveAsync(CatalogDocument catalog)
    {
        catalog.UpdatedUtc = DateTime.UtcNow;
        return JsonFile.WriteAtomicAsync(_catalogFile, catalog);
    }

    public async Task<CatalogDocument> ImportAsync(string path)
    {
        var document = await JsonFile.ReadAsync<CatalogDocument>(path)
            ?? throw new InvalidDataException("The selected file is not a NexaPlay catalog.");
        Validate(document);
        Normalize(document);
        await SaveAsync(document);
        return document;
    }

    public async Task ExportAsync(CatalogDocument catalog, string path) => await JsonFile.WriteAtomicAsync(path, catalog);

    public async Task<CatalogDocument> SyncAsync(string url, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Remote catalogs must use a trusted HTTPS URL.");
        var freshUri = AddCacheBuster(uri);
        using var request = new HttpRequestMessage(HttpMethod.Get, freshUri);
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
        request.Headers.Pragma.ParseAdd("no-cache");
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var mediaType = response.Content.Headers.ContentType?.MediaType ?? "";
        if (mediaType.Contains("html", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("That URL returned a web page, not a catalog JSON file.");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var document = await System.Text.Json.JsonSerializer.DeserializeAsync<CatalogDocument>(stream, JsonFile.Options, cancellationToken)
            ?? throw new InvalidDataException("The remote file is not a NexaPlay catalog.");
        Validate(document);
        Normalize(document);
        AppPaths.EnsureCreated();
        await JsonFile.WriteAtomicAsync(_catalogFile, document);
        return document;
    }

    private static Uri AddCacheBuster(Uri uri)
    {
        var builder = new UriBuilder(uri);
        var separator = string.IsNullOrWhiteSpace(builder.Query) ? "" : builder.Query.TrimStart('?') + "&";
        builder.Query = $"{separator}nexaplay_refresh={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        return builder.Uri;
    }

    public static void Validate(CatalogDocument document)
    {
        if (document.SchemaVersion != 1) throw new InvalidDataException($"Unsupported catalog schema {document.SchemaVersion}.");
        if (document.Games.Count > 5000) throw new InvalidDataException("The catalog contains too many games.");
        var duplicateId = document.Games.GroupBy(game => game.Id, StringComparer.OrdinalIgnoreCase).FirstOrDefault(group => group.Count() > 1)?.Key;
        if (!string.IsNullOrWhiteSpace(duplicateId)) throw new InvalidDataException($"The catalog contains a duplicate game ID: {duplicateId}");
        foreach (var game in document.Games)
        {
            if (string.IsNullOrWhiteSpace(game.Id) || string.IsNullOrWhiteSpace(game.Title))
                throw new InvalidDataException("Every catalog game needs an ID and title.");
            ValidateWebUrl(game.DownloadUrl, game.Title, "game download");
            ValidateWebUrl(game.UpdateDownloadUrl, game.Title, "update download");
            ValidateWebUrl(game.OnlineFixDownloadUrl, game.Title, "online-fix download");
            ValidateWebUrl(game.ReportUrl, game.Title, "report");
            ValidateWebUrl(game.TrailerUrl, game.Title, "trailer");
            ValidateWebUrl(game.GameplayUrl, game.Title, "gameplay");
            foreach (var screenshot in game.ScreenshotUrls) ValidateWebUrl(screenshot, game.Title, "screenshot");
            if (game.CommunityRating is < 0 or > 5) throw new InvalidDataException($"{game.Title} has a rating outside the 0–5 range.");
        }
    }

    private static void ValidateWebUrl(string value, string title, string field)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && uri.Scheme != "http"))
            throw new InvalidDataException($"{title} has an invalid {field} URL.");
    }

    private static void Normalize(CatalogDocument document)
    {
        foreach (var game in document.Games)
            if (game.UpdatedUtc == default) game.UpdatedUtc = document.UpdatedUtc;
    }
}

using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Diagnostics;

namespace NexaPlay.Services;

public sealed class DownloadService(HttpClient httpClient)
{
    public async Task<string> DownloadAsync(
        GameEntry game,
        string libraryFolder,
        IProgress<DownloadProgress> progress,
        CancellationToken cancellationToken)
        => await DownloadAsync(game, DownloadPackageKind.Game, libraryFolder, progress, cancellationToken);

    public async Task<string> DownloadAsync(
        GameEntry game,
        DownloadPackageKind kind,
        string libraryFolder,
        IProgress<DownloadProgress> progress,
        CancellationToken cancellationToken)
    {
        var package = GetPackageInfo(game, kind);
        if (!Uri.TryCreate(package.Url, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && uri.Scheme != "http"))
            throw new InvalidOperationException($"{package.DisplayName} does not have a valid HTTP or HTTPS download link.");

        var downloadRoot = kind == DownloadPackageKind.Game
            ? Path.Combine(libraryFolder, "_downloads")
            : Path.Combine(game.InstallPath, "_NexaPlay Packages", kind == DownloadPackageKind.Update ? "Updates" : "Online Fix");
        Directory.CreateDirectory(downloadRoot);
        var extension = Path.GetExtension(uri.AbsolutePath);
        if (extension.Length is < 2 or > 8) extension = ".archive";
        var suffix = kind == DownloadPackageKind.Game ? "game" : kind == DownloadPackageKind.Update ? $"update-{SafeName(game.Version)}" : "online-fix";
        var finalPath = Path.Combine(downloadRoot, $"{SafeName(game.Title)}-{suffix}{extension}");
        var partialPath = finalPath + ".part";

        if (kind == DownloadPackageKind.Game && game.HasIncompleteInstall && File.Exists(finalPath))
        {
            var savedBytes = new FileInfo(finalPath).Length;
            progress.Report(new DownloadProgress(100, savedBytes, savedBytes, $"Using saved archive — {FormatBytes(savedBytes)}"));
            return await VerifyAsync(finalPath, package.Sha256, cancellationToken);
        }

        var existing = File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0;

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (existing > 0) request.Headers.Range = new RangeHeaderValue(existing, null);
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            File.Move(partialPath, finalPath, true);
            return await VerifyAsync(finalPath, package.Sha256, cancellationToken);
        }
        response.EnsureSuccessStatusCode();

        var mediaType = response.Content.Headers.ContentType?.MediaType ?? "";
        if (mediaType.Contains("html", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The link returned a website page instead of the game archive. Use the host's direct file-download URL.");
        var append = response.StatusCode == HttpStatusCode.PartialContent && existing > 0;
        if (!append) existing = 0;
        long? total = response.Content.Headers.ContentLength is long contentLength ? contentLength + existing : null;
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(partialPath, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read, 1024 * 1024, true);
        var buffer = new byte[1024 * 1024];
        long received = existing;
        var sessionStartBytes = existing;
        var timer = Stopwatch.StartNew();
        var lastReport = TimeSpan.Zero;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            received += read;
            var percent = total is > 0 ? received * 100d / total.Value : 0;
            var elapsed = timer.Elapsed;
            if (elapsed - lastReport < TimeSpan.FromMilliseconds(100) && (!total.HasValue || received < total.Value)) continue;
            lastReport = elapsed;
            var speed = elapsed.TotalSeconds > 0 ? (received - sessionStartBytes) / elapsed.TotalSeconds : 0;
            TimeSpan? eta = total is > 0 && speed > 0 ? TimeSpan.FromSeconds(Math.Max(0, (total.Value - received) / speed)) : null;
            progress.Report(new DownloadProgress(
                percent,
                received,
                total,
                $"Downloading {FormatBytes(received)}{(total is > 0 ? $" / {FormatBytes(total.Value)}" : "")}",
                speed,
                eta,
                eta is { } remaining ? DateTime.Now.Add(remaining) : null));
        }
        var finalSpeed = timer.Elapsed.TotalSeconds > 0 ? (received - sessionStartBytes) / timer.Elapsed.TotalSeconds : 0;
        progress.Report(new DownloadProgress(100, received, total ?? received, $"Downloaded {FormatBytes(received)}", finalSpeed, TimeSpan.Zero, DateTime.Now));
        await output.FlushAsync(cancellationToken);
        output.Close();
        File.Move(partialPath, finalPath, true);
        return await VerifyAsync(finalPath, package.Sha256, cancellationToken);
    }

    public static PackageInfo GetPackageInfo(GameEntry game, DownloadPackageKind kind) => kind switch
    {
        DownloadPackageKind.Game => new(game.DownloadUrl, game.Sha256, game.ArchivePassword, game.DownloadSizeBytes, "Game download"),
        DownloadPackageKind.Update => new(game.UpdateDownloadUrl, game.UpdateSha256, game.UpdateArchivePassword, game.UpdateSizeBytes, "Game update"),
        DownloadPackageKind.OnlineFix => new(game.OnlineFixDownloadUrl, game.OnlineFixSha256, game.OnlineFixArchivePassword, game.OnlineFixSizeBytes, "Online fix"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static async Task<string> VerifyAsync(string path, string expected, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(expected)) return path;
        await using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
        var normalized = expected.Replace(" ", "").Replace("-", "");
        if (!actual.Equals(normalized, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(path);
            throw new InvalidDataException($"Security check failed. Expected SHA-256 {normalized}, received {actual}. The downloaded file was removed.");
        }
        return path;
    }

    public static string SafeName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(clean) ? "Game" : clean;
    }

    private static string FormatBytes(long value)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = value; var unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return $"{size:0.0} {units[unit]}";
    }
}

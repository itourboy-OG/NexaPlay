using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace NexaPlay.Services;

public sealed class UpdateService(HttpClient httpClient)
{
    public Version CurrentVersion { get; } = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);
    public bool IsConfigured { get; private set; }

    public async Task<UpdateRelease?> CheckAsync(CancellationToken cancellationToken = default)
    {
        var channel = await LoadChannelAsync(cancellationToken) ?? new UpdateChannel();
        IsConfigured = !string.IsNullOrWhiteSpace(channel.ManifestUrl);
        if (string.IsNullOrWhiteSpace(channel.ManifestUrl)) return null;
        var manifestUri = AddRefreshToken(RequireHttps(channel.ManifestUrl, "update manifest"));
        using var request = new HttpRequestMessage(HttpMethod.Get, manifestUri);
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > 262_144) throw new InvalidDataException("The update manifest is unexpectedly large.");
        var mediaType = response.Content.Headers.ContentType?.MediaType ?? "";
        if (mediaType.Contains("html", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The update URL returned a web page instead of update information.");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var release = await JsonSerializer.DeserializeAsync<UpdateRelease>(stream, JsonFile.Options, cancellationToken)
            ?? throw new InvalidDataException("The update manifest is not valid JSON.");
        if (!Version.TryParse(release.Version.TrimStart('v', 'V'), out var available)) throw new InvalidDataException("The update manifest has an invalid version.");
        RequireHttps(release.InstallerUrl, "installer");
        if (release.Sha256.Length != 64 || release.Sha256.Any(ch => !Uri.IsHexDigit(ch))) throw new InvalidDataException("The update manifest must contain the installer's SHA-256 checksum.");
        return available > CurrentVersion ? release : null;
    }

    public static Task<UpdateChannel?> LoadChannelAsync(CancellationToken cancellationToken = default) =>
        JsonFile.ReadAsync<UpdateChannel>(Path.Combine(AppContext.BaseDirectory, "update-channel.json"), cancellationToken);

    public async Task<string> DownloadInstallerAsync(UpdateRelease release, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var uri = RequireHttps(release.InstallerUrl, "installer");
        AppPaths.EnsureCreated();
        var safeVersion = string.Concat(release.Version.Where(ch => char.IsLetterOrDigit(ch) || ch is '.' or '-' or '_'));
        if (safeVersion.Length == 0) safeVersion = "update";
        var destination = Path.Combine(AppPaths.UpdatesRoot, $"NexaPlay-Setup-v{safeVersion}.exe");
        var partial = destination + ".partial";
        using var response = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;
        await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 128, true))
        {
            var buffer = new byte[1024 * 128];
            long received = 0;
            var started = Stopwatch.StartNew();
            int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                received += read;
                var speed = started.Elapsed.TotalSeconds > 0 ? received / started.Elapsed.TotalSeconds : 0;
                TimeSpan? eta = total is > 0 && speed > 0 ? TimeSpan.FromSeconds(Math.Max(0, (total.Value - received) / speed)) : null;
                progress?.Report(new DownloadProgress(total is > 0 ? received * 100d / total.Value : 0, received, total, "Downloading NexaPlay update…", speed, eta, eta is null ? null : DateTime.Now + eta));
            }
        }
        string actual;
        await using (var input = File.OpenRead(partial)) actual = Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken));
        if (!actual.Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(partial);
            throw new InvalidDataException("The downloaded installer failed SHA-256 verification and was removed.");
        }
        File.Move(partial, destination, true);
        return destination;
    }

    public static void LaunchInstaller(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

    private static Uri RequireHttps(string value, string label)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) throw new InvalidDataException($"The {label} URL must use HTTPS.");
        return uri;
    }

    private static Uri AddRefreshToken(Uri uri)
    {
        var separator = string.IsNullOrEmpty(uri.Query) ? "?" : "&";
        return new Uri(uri.AbsoluteUri + separator + "nexaplay_refresh=" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }
}

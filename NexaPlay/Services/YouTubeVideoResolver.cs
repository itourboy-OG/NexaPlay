using System.Collections.Concurrent;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;

namespace NexaPlay.Services;

public sealed partial class YouTubeVideoResolver
{
    private static readonly HttpClient Http = CreateHttpClient();
    private static readonly ConcurrentDictionary<string, string> SearchCache = new(StringComparer.OrdinalIgnoreCase);

    public async Task<Uri?> ResolvePlayerUriAsync(Uri source, CancellationToken cancellationToken = default)
    {
        var videoId = TryGetVideoId(source);
        if (videoId.Length == 0 && IsSearchUri(source))
        {
            if (!SearchCache.TryGetValue(source.AbsoluteUri, out videoId))
            {
                using var response = await Http.GetAsync(source, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();
                var html = await response.Content.ReadAsStringAsync(cancellationToken);
                videoId = VideoIdRegex().Matches(html)
                    .Select(match => match.Groups["id"].Value)
                    .FirstOrDefault(id => IsValidVideoId(id)) ?? "";
                if (videoId.Length > 0) SearchCache[source.AbsoluteUri] = videoId;
            }
        }

        if (!IsValidVideoId(videoId)) return null;
        return new Uri($"https://www.youtube.com/embed/{videoId}?autoplay=1&controls=1&fs=1&playsinline=1&rel=0");
    }

    public static string TryGetVideoId(Uri source)
    {
        if (source.Host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase))
            return source.AbsolutePath.Trim('/').Split('/')[0];

        if (!source.Host.Equals("youtube.com", StringComparison.OrdinalIgnoreCase) &&
            !source.Host.EndsWith(".youtube.com", StringComparison.OrdinalIgnoreCase)) return "";

        if (source.AbsolutePath.Equals("/watch", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var part in source.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var pieces = part.Split('=', 2);
                if (pieces.Length == 2 && pieces[0].Equals("v", StringComparison.OrdinalIgnoreCase))
                    return Uri.UnescapeDataString(pieces[1]);
            }
        }

        var segments = source.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length >= 2 && (segments[0].Equals("embed", StringComparison.OrdinalIgnoreCase) ||
            segments[0].Equals("shorts", StringComparison.OrdinalIgnoreCase)) ? segments[1] : "";
    }

    private static bool IsSearchUri(Uri source) =>
        (source.Host.Equals("youtube.com", StringComparison.OrdinalIgnoreCase) || source.Host.EndsWith(".youtube.com", StringComparison.OrdinalIgnoreCase)) &&
        source.AbsolutePath.Equals("/results", StringComparison.OrdinalIgnoreCase);

    private static bool IsValidVideoId(string value) => value.Length == 11 && value.All(character => char.IsLetterOrDigit(character) || character is '-' or '_');

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) NexaPlay/1.12.0");
        client.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("en-US"));
        return client;
    }

    [GeneratedRegex("\\\"videoId\\\":\\\"(?<id>[A-Za-z0-9_-]{11})\\\"", RegexOptions.CultureInvariant)]
    private static partial Regex VideoIdRegex();
}

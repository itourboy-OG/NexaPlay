using System.Net.Http.Headers;
using System.Net.Http;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NexaPlay.Services;

public sealed class SteamMetadataService(HttpClient httpClient)
{
    public async Task<SteamMatch> SearchAsync(string title, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(title)) throw new InvalidOperationException("A game title is required for Steam matching.");
        var url = "https://store.steampowered.com/search/suggest" +
            $"?term={Uri.EscapeDataString(title.Trim())}&f=games&cc=US&l=english&v=1&use_store_query=1&use_search_spellcheck=1";
        using var response = await httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        var ids = Regex.Matches(html, "data-ds-appid=\\\"(?<id>\\d+)\\\"", RegexOptions.IgnoreCase)
            .Select(match => int.Parse(match.Groups["id"].Value, System.Globalization.CultureInfo.InvariantCulture))
            .Distinct()
            .Take(5)
            .ToList();
        if (ids.Count == 0) throw new InvalidOperationException($"Steam did not find a match for “{title}”. You can still enter the information manually.");

        SteamMatch? best = null;
        var bestScore = double.MinValue;
        foreach (var id in ids)
        {
            try
            {
                var metadata = await FetchAsync(id, cancellationToken);
                var score = TitleScore(title, metadata.Title);
                if (score > bestScore) { bestScore = score; best = new SteamMatch(id, metadata); }
                if (score >= 99) break;
            }
            catch when (!cancellationToken.IsCancellationRequested) { }
        }
        if (best is null || bestScore < 50)
            throw new InvalidOperationException($"Steam did not find a close enough match for “{title}”. Enter its Steam App ID to prevent artwork from another game being used.");
        return best;
    }

    public async Task<SteamMetadata> FetchAsync(int appId, CancellationToken cancellationToken = default)
    {
        var url = $"https://store.steampowered.com/api/appdetails?appids={appId}&cc=us&l=en";
        using var response = await httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!json.RootElement.TryGetProperty(appId.ToString(), out var root) ||
            !root.TryGetProperty("success", out var success) || !success.GetBoolean() ||
            !root.TryGetProperty("data", out var data))
            throw new InvalidOperationException("Steam did not return metadata for that App ID.");

        static string Text(JsonElement parent, string name) =>
            parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
        static string FirstArrayName(JsonElement parent, string name)
        {
            if (!parent.TryGetProperty(name, out var list) || list.ValueKind != JsonValueKind.Array || list.GetArrayLength() == 0) return "";
            var first = list[0];
            return first.ValueKind == JsonValueKind.String ? first.GetString() ?? "" : Text(first, "name");
        }

        var title = Text(data, "name");
        var description = CleanText(Text(data, "short_description"));
        var developer = FirstArrayName(data, "developers");
        var publisher = FirstArrayName(data, "publishers");
        var date = data.TryGetProperty("release_date", out var release) ? Text(release, "date") : "";
        var genres = new List<string>();
        if (data.TryGetProperty("genres", out var genreList) && genreList.ValueKind == JsonValueKind.Array)
            genres.AddRange(genreList.EnumerateArray().Select(x => Text(x, "description")).Where(x => x.Length > 0));
        var tags = new List<string>();
        if (data.TryGetProperty("categories", out var categoryList) && categoryList.ValueKind == JsonValueKind.Array)
            tags.AddRange(categoryList.EnumerateArray().Select(x => Text(x, "description")).Where(IsUsefulTag));
        var isMultiplayer = tags.Any(tag => tag.Contains("multi-player", StringComparison.OrdinalIgnoreCase) ||
            tag.Contains("multiplayer", StringComparison.OrdinalIgnoreCase) || tag.Contains("co-op", StringComparison.OrdinalIgnoreCase) ||
            tag.Contains("pvp", StringComparison.OrdinalIgnoreCase));
        var minimum = "";
        var recommended = "";
        if (data.TryGetProperty("pc_requirements", out var requirements) && requirements.ValueKind == JsonValueKind.Object)
        {
            minimum = CleanRequirements(Text(requirements, "minimum"));
            recommended = CleanRequirements(Text(requirements, "recommended"));
        }
        var minimumRamGb = ExtractRequirementGb(minimum, "ram", "memory");
        var requiredStorageGb = ExtractRequirementGb(minimum, "storage", "space", "disk");
        var hero = Text(data, "background_raw");
        if (hero.Length == 0) hero = Text(data, "background");
        var portraitCover = $"https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/{appId}/library_600x900.jpg";
        var cover = await IsImageUrlAvailableAsync(portraitCover, cancellationToken) ? portraitCover : Text(data, "header_image");
        if (cover.Length == 0) cover = Text(data, "capsule_image");
        var screenshots = new List<string>();
        if (data.TryGetProperty("screenshots", out var screenshotList) && screenshotList.ValueKind == JsonValueKind.Array)
            screenshots.AddRange(screenshotList.EnumerateArray().Select(x => Text(x, "path_full")).Where(x => x.Length > 0).Take(8));
        var trailer = "";
        if (data.TryGetProperty("movies", out var movies) && movies.ValueKind == JsonValueKind.Array && movies.GetArrayLength() > 0)
        {
            var firstMovie = movies[0];
            if (firstMovie.TryGetProperty("mp4", out var mp4))
                trailer = Text(mp4, "max").Length > 0 ? Text(mp4, "max") : Text(mp4, "480");
        }
        if (trailer.Length == 0) trailer = YouTubeSearch(title, "official trailer");
        var gameplay = YouTubeSearch(title, "official gameplay");
        var (communityRating, ratingCount) = await FetchRatingAsync(appId, cancellationToken);
        return new SteamMetadata(title, description, developer, publisher, date, genres, tags, isMultiplayer,
            communityRating, ratingCount, minimum, recommended, minimumRamGb, requiredStorageGb,
            cover, hero, screenshots, trailer, gameplay);
    }

    private static string YouTubeSearch(string title, string kind) =>
        $"https://www.youtube.com/results?search_query={Uri.EscapeDataString($"{title} {kind}")}";

    private async Task<(double Rating, int Count)> FetchRatingAsync(int appId, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync($"https://store.steampowered.com/appreviews/{appId}?json=1&language=all&purchase_type=all&num_per_page=0", cancellationToken);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (!json.RootElement.TryGetProperty("query_summary", out var summary)) return (0, 0);
            var total = summary.TryGetProperty("total_reviews", out var totalValue) ? totalValue.GetInt32() : 0;
            var positive = summary.TryGetProperty("total_positive", out var positiveValue) ? positiveValue.GetInt32() : 0;
            return total > 0 ? (Math.Round(positive * 5d / total, 1), total) : (0, 0);
        }
        catch when (!cancellationToken.IsCancellationRequested) { return (0, 0); }
    }

    public async Task<bool> IsImageUrlAvailableAsync(string value, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)) return false;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, uri);
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.IsSuccessStatusCode && (response.Content.Headers.ContentType?.MediaType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ?? false)) return true;

            using var fallback = new HttpRequestMessage(HttpMethod.Get, uri);
            fallback.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 0);
            using var fallbackResponse = await httpClient.SendAsync(fallback, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            return fallbackResponse.IsSuccessStatusCode && (fallbackResponse.Content.Headers.ContentType?.MediaType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ?? false);
        }
        catch when (!cancellationToken.IsCancellationRequested) { return false; }
    }

    public static bool TitlesLikelyMatch(string requested, string candidate) => TitleScore(requested, candidate) >= 50;

    private static bool IsUsefulTag(string value) =>
        value.Contains("multi", StringComparison.OrdinalIgnoreCase) || value.Contains("co-op", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("pvp", StringComparison.OrdinalIgnoreCase) || value.Contains("single-player", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("cross-platform", StringComparison.OrdinalIgnoreCase) || value.Contains("split screen", StringComparison.OrdinalIgnoreCase);

    private static string CleanRequirements(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var withLines = Regex.Replace(value, "<(br|/p|/li)[^>]*>", "\n", RegexOptions.IgnoreCase);
        withLines = Regex.Replace(withLines, "<li[^>]*>", "• ", RegexOptions.IgnoreCase);
        var withoutTags = WebUtility.HtmlDecode(Regex.Replace(withLines, "<[^>]+>", " "));
        var lines = withoutTags.Replace("\r", "").Split('\n')
            .Select(line => Regex.Replace(line, "\\s+", " ").Trim())
            .Where(line => line.Length > 0 && !line.Equals("Minimum:", StringComparison.OrdinalIgnoreCase) && !line.Equals("Recommended:", StringComparison.OrdinalIgnoreCase));
        return string.Join(Environment.NewLine, lines);
    }

    private static double? ExtractRequirementGb(string requirements, params string[] keywords)
    {
        foreach (var line in requirements.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!keywords.Any(keyword => line.Contains(keyword, StringComparison.OrdinalIgnoreCase))) continue;
            var match = Regex.Match(line, "(?<value>\\d+(?:\\.\\d+)?)\\s*GB", RegexOptions.IgnoreCase);
            if (match.Success && double.TryParse(match.Groups["value"].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value)) return value;
        }
        return null;
    }

    private static double TitleScore(string requested, string candidate)
    {
        static string Normalize(string value) => string.Join(' ', Regex.Split(value.ToLowerInvariant(), "[^a-z0-9]+").Where(part => part.Length > 0));
        var left = Normalize(requested); var right = Normalize(candidate);
        if (left == right) return 100;
        if (right.StartsWith(left, StringComparison.Ordinal) || left.StartsWith(right, StringComparison.Ordinal)) return 85;
        var leftWords = left.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var rightWords = right.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var union = leftWords.Union(rightWords).Count();
        return union == 0 ? 0 : leftWords.Intersect(rightWords).Count() * 70d / union;
    }

    private static string CleanText(string value)
    {
        var withoutTags = Regex.Replace(value, "<[^>]+>", " ");
        return Regex.Replace(WebUtility.HtmlDecode(withoutTags), "\\s+", " ").Trim();
    }
}

public sealed class SteamGridDbService(HttpClient httpClient)
{
    public async Task<ArtworkResult> FindArtworkAsync(string title, string apiKey, CancellationToken cancellationToken = default)
        => await FindArtworkAsync(title, apiKey, null, cancellationToken);

    public async Task<ArtworkResult> FindArtworkAsync(string title, string apiKey, int? steamAppId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) throw new InvalidOperationException("Add your SteamGridDB API key from the Owner Studio toolbar first.");
        int id;
        if (steamAppId is int appId)
            id = await TryFindBySteamIdAsync(appId, apiKey, cancellationToken)
                ?? throw new InvalidOperationException("SteamGridDB does not have artwork mapped to this Steam App ID. Official Steam artwork will be kept instead.");
        else id = await FindBestTitleMatchAsync(title, apiKey, cancellationToken);

        var coverTask = TryGetFirstUrlAsync($"https://www.steamgriddb.com/api/v2/grids/game/{id}?dimensions=600x900&types=static", apiKey, cancellationToken);
        var heroTask = TryGetFirstUrlAsync($"https://www.steamgriddb.com/api/v2/heroes/game/{id}?types=static", apiKey, cancellationToken);
        await Task.WhenAll(coverTask, heroTask);
        var cover = await coverTask;
        var hero = await heroTask;
        if (cover.Length == 0 && hero.Length == 0)
            throw new InvalidOperationException("SteamGridDB found the game, but it does not have a usable static cover or hero image yet.");
        return new ArtworkResult(id, cover, hero);
    }

    private async Task<int?> TryFindBySteamIdAsync(int appId, string apiKey, CancellationToken cancellationToken)
    {
        try
        {
            using var request = CreateRequest($"https://www.steamgriddb.com/api/v2/games/steam/{appId}", apiKey);
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            return json.RootElement.TryGetProperty("data", out var data) && data.TryGetProperty("id", out var id) ? id.GetInt32() : null;
        }
        catch when (!cancellationToken.IsCancellationRequested) { return null; }
    }

    private async Task<int> FindBestTitleMatchAsync(string title, string apiKey, CancellationToken cancellationToken)
    {
        using var search = CreateRequest($"https://www.steamgriddb.com/api/v2/search/autocomplete/{Uri.EscapeDataString(title)}", apiKey);
        using var response = await httpClient.SendAsync(search, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var data = json.RootElement.GetProperty("data");
        if (data.GetArrayLength() == 0) throw new InvalidOperationException("No SteamGridDB match was found for that title.");
        static string Normalize(string value) => string.Concat(value.Where(char.IsLetterOrDigit)).ToLowerInvariant();
        var normalizedTitle = Normalize(title);
        foreach (var candidate in data.EnumerateArray())
        {
            var name = candidate.TryGetProperty("name", out var value) ? value.GetString() ?? "" : "";
            if (Normalize(name) == normalizedTitle) return candidate.GetProperty("id").GetInt32();
        }
        return data[0].GetProperty("id").GetInt32();
    }

    private async Task<string> TryGetFirstUrlAsync(string url, string apiKey, CancellationToken cancellationToken)
    {
        try
        {
            using var request = CreateRequest(url, apiKey);
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return "";
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var data = json.RootElement.GetProperty("data");
            return data.GetArrayLength() > 0 && data[0].TryGetProperty("url", out var urlValue) ? urlValue.GetString() ?? "" : "";
        }
        catch when (!cancellationToken.IsCancellationRequested) { return ""; }
    }

    private static HttpRequestMessage CreateRequest(string url, string apiKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        return request;
    }
}

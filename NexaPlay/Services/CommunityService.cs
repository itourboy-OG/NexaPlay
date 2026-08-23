using System.Net.Http.Json;
using System.Net.Http;

namespace NexaPlay.Services;

public sealed class CommunityService(HttpClient httpClient)
{
    public async Task<RatingSummary?> GetRatingAsync(string baseUrl, string gameId, string userId, CancellationToken cancellationToken = default)
    {
        var endpoint = BuildEndpoint(baseUrl, $"games/{Uri.EscapeDataString(gameId)}/rating?userId={Uri.EscapeDataString(userId)}");
        if (endpoint is null) return null;
        using var response = await httpClient.GetAsync(endpoint, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<RatingSummary>(cancellationToken: cancellationToken);
    }

    public async Task<RatingSummary> RateAsync(string baseUrl, string gameId, string userId, int score, CancellationToken cancellationToken = default)
    {
        var endpoint = BuildEndpoint(baseUrl, $"games/{Uri.EscapeDataString(gameId)}/ratings")
            ?? throw new InvalidOperationException("Community ratings are not connected yet. SauceBoyz needs to configure the NexaPlay community service URL.");
        using var response = await httpClient.PostAsJsonAsync(endpoint, new { userId, score = Math.Clamp(score, 1, 5) }, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<RatingSummary>(cancellationToken: cancellationToken)
            ?? throw new InvalidDataException("The community service returned an invalid rating response.");
    }

    public async Task SubmitReportAsync(string baseUrl, string gameId, string message, string version, CancellationToken cancellationToken = default)
    {
        var endpoint = BuildEndpoint(baseUrl, $"games/{Uri.EscapeDataString(gameId)}/reports")
            ?? throw new InvalidOperationException("Problem reports are not connected yet. SauceBoyz needs to configure the NexaPlay Community service URL.");
        using var response = await httpClient.PostAsJsonAsync(endpoint, new { message = message.Trim(), version }, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static Uri? BuildEndpoint(string baseUrl, string relative)
    {
        if (!Uri.TryCreate(baseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var root) || root.Scheme != Uri.UriSchemeHttps) return null;
        return new Uri(root, "api/" + relative);
    }
}

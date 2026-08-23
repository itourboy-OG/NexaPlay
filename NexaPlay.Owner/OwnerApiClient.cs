using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Http;
using System.IO;

namespace NexaPlay.Owner;

public sealed class OwnerApiClient(HttpClient httpClient)
{
    public async Task<NexaPlay.CatalogDocument> GetCatalogAsync(string baseUrl)
    {
        var endpoint = Endpoint(baseUrl, "api/catalog");
        return await httpClient.GetFromJsonAsync<NexaPlay.CatalogDocument>(endpoint) ?? throw new InvalidDataException("The server returned an invalid catalog.");
    }
    public async Task PublishAsync(string baseUrl, string adminKey, NexaPlay.CatalogDocument catalog)
    {
        catalog.CommunityApiUrl = baseUrl.TrimEnd('/');
        using var request = new HttpRequestMessage(HttpMethod.Put, Endpoint(baseUrl, "api/admin/catalog")) { Content = JsonContent.Create(catalog) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminKey);
        using var response = await httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }
    public async Task<List<CommunityReport>> GetReportsAsync(string baseUrl, string adminKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint(baseUrl, "api/admin/reports"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminKey);
        using var response = await httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ReportResponse>())?.Reports ?? [];
    }
    private static Uri Endpoint(string baseUrl, string relative)
    {
        if (!Uri.TryCreate(baseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var root) || (root.Scheme != Uri.UriSchemeHttps && !root.IsLoopback)) throw new InvalidOperationException("Enter the HTTPS NexaPlay Community URL.");
        return new Uri(root, relative);
    }
}

public sealed record CommunityReport(string Id = "", string GameId = "", string GameTitle = "", string PlayerName = "", string Message = "", string Version = "", DateTime CreatedUtc = default)
{
    public string CreatedLabel => CreatedUtc.ToLocalTime().ToString("MMM d, yyyy  h:mm tt");
    public string GameLabel => string.IsNullOrWhiteSpace(GameTitle) ? GameId : GameTitle;
    public string PlayerLabel => string.IsNullOrWhiteSpace(PlayerName) ? "Player" : PlayerName;
}
public sealed record ReportResponse(List<CommunityReport> Reports);

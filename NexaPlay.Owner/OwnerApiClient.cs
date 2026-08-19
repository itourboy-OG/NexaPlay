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
    private static Uri Endpoint(string baseUrl, string relative)
    {
        if (!Uri.TryCreate(baseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var root) || (root.Scheme != Uri.UriSchemeHttps && !root.IsLoopback)) throw new InvalidOperationException("Enter the HTTPS NexaPlay Community URL.");
        return new Uri(root, relative);
    }
}

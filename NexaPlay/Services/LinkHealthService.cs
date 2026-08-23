using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace NexaPlay.Services;

public enum LinkHealthState
{
    NotChecked,
    Checking,
    Available,
    Broken,
    Unreachable
}

public sealed record LinkHealthResult(LinkHealthState State, string Message, int? StatusCode = null, long? SizeBytes = null)
{
    public static LinkHealthResult NotConfigured { get; } = new(LinkHealthState.NotChecked, "Not configured");
    public static LinkHealthResult Checking { get; } = new(LinkHealthState.Checking, "Checking…");
}

public sealed record GameLinkHealthSnapshot(
    LinkHealthResult Game,
    LinkHealthResult Update,
    LinkHealthResult OnlineFix,
    LinkHealthResult Custom);

public sealed class LinkHealthService(HttpClient httpClient)
{
    public async Task<GameLinkHealthSnapshot> CheckGameAsync(GameEntry game, CancellationToken cancellationToken = default)
    {
        var gameTask = CheckAsync(game.DownloadUrl, cancellationToken);
        var updateTask = CheckOptionalAsync(game.UpdateDownloadUrl, cancellationToken);
        var fixTask = CheckOptionalAsync(game.OnlineFixDownloadUrl, cancellationToken);
        var customTask = CheckOptionalAsync(game.CustomPackageDownloadUrl, cancellationToken);
        await Task.WhenAll(gameTask, updateTask, fixTask, customTask);
        return new(await gameTask, await updateTask, await fixTask, await customTask);
    }

    public async Task<LinkHealthResult> CheckAsync(string value, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            return new(LinkHealthState.Broken, "Invalid or missing URL");

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Range = new RangeHeaderValue(0, 0);
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            return Classify(response);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(LinkHealthState.Unreachable, "Timed out — try again");
        }
        catch (HttpRequestException ex)
        {
            return new(LinkHealthState.Unreachable, $"Could not reach host — {CleanMessage(ex.Message)}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new(LinkHealthState.Unreachable, $"Could not verify — {CleanMessage(ex.Message)}");
        }
    }

    private async Task<LinkHealthResult> CheckOptionalAsync(string value, CancellationToken cancellationToken) =>
        string.IsNullOrWhiteSpace(value) ? LinkHealthResult.NotConfigured : await CheckAsync(value, cancellationToken);

    private static LinkHealthResult Classify(HttpResponseMessage response)
    {
        var code = (int)response.StatusCode;
        var reason = string.IsNullOrWhiteSpace(response.ReasonPhrase) ? response.StatusCode.ToString() : response.ReasonPhrase;
        var mediaType = response.Content.Headers.ContentType?.MediaType ?? "";
        var size = response.Content.Headers.ContentRange?.Length ?? response.Content.Headers.ContentLength;

        if (response.IsSuccessStatusCode)
        {
            if (mediaType.Contains("html", StringComparison.OrdinalIgnoreCase))
                return new(LinkHealthState.Broken, "Link opens a webpage, not a game file", code, size);
            return new(LinkHealthState.Available, $"Working · {code} {reason}", code, size);
        }

        if (response.StatusCode == HttpStatusCode.Gone)
            return new(LinkHealthState.Broken, "Expired · 410 Gone", code);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return new(LinkHealthState.Broken, "Missing · 404 Not Found", code);
        if (response.StatusCode == HttpStatusCode.TooManyRequests || code >= 500)
            return new(LinkHealthState.Unreachable, $"Host unavailable · {code} {reason}", code);
        if (code >= 400 && code < 500)
            return new(LinkHealthState.Broken, $"Blocked · {code} {reason}", code);
        return new(LinkHealthState.Unreachable, $"Could not verify · {code} {reason}", code);
    }

    private static string CleanMessage(string value)
    {
        var firstLine = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return firstLine.Length <= 110 ? firstLine : firstLine[..107] + "…";
    }
}

using NexaPlay.Services;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace NexaPlay.Owner;

public sealed class GitHubCatalogPublisher
{
    public const string Repository = "itourboy-OG/NexaPlay";
    public const string CatalogPath = "catalog/nexaplay-catalog.json";
    public const string CatalogUrl = "https://raw.githubusercontent.com/itourboy-OG/NexaPlay/main/catalog/nexaplay-catalog.json";

    public async Task PublishAsync(CatalogDocument catalog, CancellationToken cancellationToken = default)
    {
        CatalogService.Validate(catalog);
        catalog.UpdatedUtc = DateTime.UtcNow;
        var endpoint = $"repos/{Repository}/contents/{CatalogPath}";
        var current = await RunGhAsync(["api", endpoint, "--jq", ".sha"], null, cancellationToken, allowFailure: true);
        var content = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(catalog, JsonFile.Options)));
        var payload = new Dictionary<string, object?>
        {
            ["message"] = $"Update game catalog {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC",
            ["content"] = content,
            ["branch"] = "main"
        };
        if (current.ExitCode == 0 && !string.IsNullOrWhiteSpace(current.Output)) payload["sha"] = current.Output.Trim();
        var result = await RunGhAsync(["api", endpoint, "--method", "PUT", "--input", "-"], JsonSerializer.Serialize(payload), cancellationToken);
        if (result.ExitCode != 0) throw new InvalidOperationException($"GitHub catalog publishing failed. {result.Error.Trim()}");
    }

    public async Task EnsureReadyAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunGhAsync(["auth", "status"], null, cancellationToken, allowFailure: true);
        if (result.ExitCode != 0) throw new InvalidOperationException("GitHub CLI is not signed in. Run 'gh auth login' once on the owner PC, then reopen Owner Studio.");
    }

    private static async Task<CommandResult> RunGhAsync(IReadOnlyList<string> arguments, string? input, CancellationToken cancellationToken, bool allowFailure = false)
    {
        var start = new ProcessStartInfo("gh") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = input is not null, CreateNoWindow = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("GitHub CLI could not start. Install GitHub CLI on the owner PC first.");
        if (input is not null) { await process.StandardInput.WriteAsync(input); process.StandardInput.Close(); }
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var result = new CommandResult(process.ExitCode, await outputTask, await errorTask);
        if (!allowFailure && result.ExitCode != 0) throw new InvalidOperationException(result.Error.Trim().Length > 0 ? result.Error.Trim() : "GitHub CLI returned an error.");
        return result;
    }

    private sealed record CommandResult(int ExitCode, string Output, string Error);
}

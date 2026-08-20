namespace NexaPlay.Services;

public sealed class DownloadHistoryService
{
    private const int MaximumEntries = 250;
    private readonly string _historyFile;

    public DownloadHistoryService(string? historyFile = null) => _historyFile = historyFile ?? AppPaths.DownloadHistoryFile;

    public async Task<IReadOnlyList<DownloadHistoryItem>> LoadAsync()
    {
        AppPaths.EnsureCreated();
        var items = await JsonFile.ReadAsync<List<DownloadHistoryItem>>(_historyFile) ?? [];
        return items
            .OrderByDescending(item => item.CompletedUtc)
            .Take(MaximumEntries)
            .ToList();
    }

    public Task SaveAsync(IEnumerable<DownloadHistoryItem> items) =>
        JsonFile.WriteAtomicAsync(
            _historyFile,
            items.OrderByDescending(item => item.CompletedUtc).Take(MaximumEntries).ToList());

    public Task ClearAsync() => JsonFile.WriteAtomicAsync(_historyFile, Array.Empty<DownloadHistoryItem>());
}

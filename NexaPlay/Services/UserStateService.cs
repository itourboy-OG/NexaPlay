namespace NexaPlay.Services;

public sealed class UserStateService
{
    private UserLibraryState _state = new();

    public async Task LoadAsync()
    {
        AppPaths.EnsureCreated();
        _state = await JsonFile.ReadAsync<UserLibraryState>(AppPaths.UserStateFile) ?? new UserLibraryState();
        _state.FavoriteGameIds = new HashSet<string>(_state.FavoriteGameIds, StringComparer.OrdinalIgnoreCase);
        _state.LocalRatings = new Dictionary<string, int>(_state.LocalRatings, StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(_state.CommunityUserId)) _state.CommunityUserId = Guid.NewGuid().ToString("N");
    }

    public bool IsFavorite(string gameId) => _state.FavoriteGameIds.Contains(gameId);

    public async Task SetFavoriteAsync(string gameId, bool favorite)
    {
        if (favorite) _state.FavoriteGameIds.Add(gameId);
        else _state.FavoriteGameIds.Remove(gameId);
        await JsonFile.WriteAtomicAsync(AppPaths.UserStateFile, _state);
    }

    public string CommunityUserId => _state.CommunityUserId;
    public int? GetLocalRating(string gameId) => _state.LocalRatings.TryGetValue(gameId, out var score) ? score : null;
    public async Task SetLocalRatingAsync(string gameId, int score)
    {
        _state.LocalRatings[gameId] = Math.Clamp(score, 1, 5);
        await JsonFile.WriteAtomicAsync(AppPaths.UserStateFile, _state);
    }
}

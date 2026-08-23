using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace NexaPlay;

public sealed class GameEntry : INotifyPropertyChanged
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "Untitled game";
    public string Description { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public string UpdateDownloadUrl { get; set; } = "";
    public string OnlineFixDownloadUrl { get; set; } = "";
    public string CustomPackageName { get; set; } = "";
    public string CustomPackageDownloadUrl { get; set; } = "";
    public string Version { get; set; } = "1.0";
    public string Developer { get; set; } = "";
    public string Publisher { get; set; } = "";
    public string ReleaseDate { get; set; } = "";
    public List<string> Genres { get; set; } = [];
    public List<string> Tags { get; set; } = [];
    public bool IsMultiplayer { get; set; }
    public double CommunityRating { get; set; }
    public int RatingCount { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public int? SteamAppId { get; set; }
    public int? SteamGridDbId { get; set; }
    public string CoverUrl { get; set; } = "";
    public string HeroUrl { get; set; } = "";
    public string ArchivePassword { get; set; } = "";
    public string UpdateArchivePassword { get; set; } = "";
    public string OnlineFixArchivePassword { get; set; } = "";
    public string CustomPackageArchivePassword { get; set; } = "";
    public string ExecutablePath { get; set; } = "";
    public string InstallFolderName { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public string UpdateSha256 { get; set; } = "";
    public string OnlineFixSha256 { get; set; } = "";
    public string CustomPackageSha256 { get; set; } = "";
    public long? DownloadSizeBytes { get; set; }
    public long? UpdateSizeBytes { get; set; }
    public long? OnlineFixSizeBytes { get; set; }
    public long? CustomPackageSizeBytes { get; set; }
    public string InstallationGuide { get; set; } = "";
    public string ImportantNotes { get; set; } = "";
    public string MinimumRequirements { get; set; } = "";
    public string RecommendedRequirements { get; set; } = "";
    public double? MinimumRamGb { get; set; }
    public double? RequiredStorageGb { get; set; }
    public string TrailerUrl { get; set; } = "";
    public string GameplayUrl { get; set; } = "";
    public List<string> ScreenshotUrls { get; set; } = [];
    public string ReportUrl { get; set; } = "";
    public string SupportEmail { get; set; } = "";
    public bool Featured { get; set; }

    [JsonIgnore] public string InstallPath { get; set; } = "";
    [JsonIgnore] public bool IsInstalled { get; set; }
    [JsonIgnore] public bool HasIncompleteInstall { get; set; }
    [JsonIgnore] public bool IsBusy { get; set; }
    [JsonIgnore] public bool IsFavorite { get; set; }
    [JsonIgnore] public string InstalledVersion { get; set; } = "";
    [JsonIgnore] public double Progress { get; set; }
    [JsonIgnore] public string Activity { get; set; } = "";
    [JsonIgnore] public int? UserRating { get; set; }
    [JsonIgnore] public double LiveCommunityRating { get; set; }
    [JsonIgnore] public int LiveCommunityRatingCount { get; set; }
    [JsonIgnore] public string ActionLabel => IsBusy ? "Working…" : IsInstalled ? "Play" : "View game";
    [JsonIgnore] public string GenreLine => Genres.Count == 0 ? "PC GAME" : string.Join("  •  ", Genres.Take(3)).ToUpperInvariant();
    [JsonIgnore] public string TagLine => Tags.Count == 0 ? GenreLine : string.Join("  •  ", Tags.Take(4)).ToUpperInvariant();
    [JsonIgnore] public string SizeLabel => DownloadSizeBytes is > 0 ? FormatBytes(DownloadSizeBytes.Value) : "Size not listed";
    [JsonIgnore] public string UpdateSizeLabel => UpdateSizeBytes is > 0 ? FormatBytes(UpdateSizeBytes.Value) : "Size not listed";
    [JsonIgnore] public string OnlineFixSizeLabel => OnlineFixSizeBytes is > 0 ? FormatBytes(OnlineFixSizeBytes.Value) : "Size not listed";
    [JsonIgnore] public string CustomPackageSizeLabel => CustomPackageSizeBytes is > 0 ? FormatBytes(CustomPackageSizeBytes.Value) : "Size not listed";
    [JsonIgnore] public string CustomPackageLabel => string.IsNullOrWhiteSpace(CustomPackageName) ? "EXTRA PACKAGE" : CustomPackageName.Trim();
    [JsonIgnore] public bool HasUpdate => IsInstalled && UpdateDownloadUrl.Length > 0 && CompareVersions(Version, InstalledVersion) > 0;
    [JsonIgnore] public string MultiplayerLabel => IsMultiplayer ? "MULTIPLAYER" : "SINGLE-PLAYER";
    [JsonIgnore] public string RatingLabel => CommunityRating > 0 ? $"STEAM {Math.Clamp(CommunityRating, 0, 5):0.0} / 5  ({RatingCount:N0} REVIEWS)" : "NO STEAM SCORE";
    [JsonIgnore] public string CommunityRatingLabel => LiveCommunityRatingCount > 0 ? $"NEXAPLAY {LiveCommunityRating:0.0} / 5  ({LiveCommunityRatingCount:N0} VOTES)" : "NO NEXAPLAY VOTES YET";
    [JsonIgnore] public string Stars => CommunityRating > 0 ? new string('★', Math.Clamp((int)Math.Round(CommunityRating), 1, 5)) + new string('☆', 5 - Math.Clamp((int)Math.Round(CommunityRating), 1, 5)) : "☆☆☆☆☆";
    [JsonIgnore] public string LastUpdatedLabel => UpdatedUtc == default ? "Last updated — not provided" : $"Last updated {HumanizeAge(UpdatedUtc)}";
    [JsonIgnore] public string InstallationGuideDisplay => CleanSectionText(InstallationGuide, "Installation Guide", "Step-by-step setup process");
    [JsonIgnore] public string ImportantNotesDisplay => CleanSectionText(ImportantNotes, "Important Notes", "Please review these important details before installation");
    [JsonIgnore] public string MinimumRequirementsDisplay => CleanSectionText(MinimumRequirements, "Minimum Requirements", "Minimum");
    [JsonIgnore] public string RecommendedRequirementsDisplay => CleanSectionText(RecommendedRequirements, "Recommended Requirements", "Recommended");

    public event PropertyChangedEventHandler? PropertyChanged;

    public void NotifyRuntimeChanged()
    {
        OnPropertyChanged(nameof(IsInstalled));
        OnPropertyChanged(nameof(HasIncompleteInstall));
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(Activity));
        OnPropertyChanged(nameof(ActionLabel));
        OnPropertyChanged(nameof(IsFavorite));
        OnPropertyChanged(nameof(InstalledVersion));
        OnPropertyChanged(nameof(HasUpdate));
        OnPropertyChanged(nameof(UserRating));
    }

    public void SetCommunityRating(double average, int count, int? userScore = null)
    {
        LiveCommunityRating = Math.Clamp(average, 0, 5);
        LiveCommunityRatingCount = Math.Max(0, count);
        UserRating = userScore;
        OnPropertyChanged(nameof(LiveCommunityRating));
        OnPropertyChanged(nameof(LiveCommunityRatingCount));
        OnPropertyChanged(nameof(CommunityRatingLabel));
        OnPropertyChanged(nameof(UserRating));
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public GameEntry Clone() => new()
    {
        Id = Id, Title = Title, Description = Description, DownloadUrl = DownloadUrl,
        UpdateDownloadUrl = UpdateDownloadUrl, OnlineFixDownloadUrl = OnlineFixDownloadUrl,
        CustomPackageName = CustomPackageName, CustomPackageDownloadUrl = CustomPackageDownloadUrl,
        Version = Version, Developer = Developer, Publisher = Publisher, ReleaseDate = ReleaseDate,
        Genres = [.. Genres], Tags = [.. Tags], IsMultiplayer = IsMultiplayer,
        CommunityRating = CommunityRating, RatingCount = RatingCount, UpdatedUtc = UpdatedUtc,
        SteamAppId = SteamAppId, SteamGridDbId = SteamGridDbId,
        CoverUrl = CoverUrl, HeroUrl = HeroUrl, ArchivePassword = ArchivePassword,
        UpdateArchivePassword = UpdateArchivePassword, OnlineFixArchivePassword = OnlineFixArchivePassword, CustomPackageArchivePassword = CustomPackageArchivePassword,
        ExecutablePath = ExecutablePath, InstallFolderName = InstallFolderName, Sha256 = Sha256,
        UpdateSha256 = UpdateSha256, OnlineFixSha256 = OnlineFixSha256, CustomPackageSha256 = CustomPackageSha256,
        DownloadSizeBytes = DownloadSizeBytes, UpdateSizeBytes = UpdateSizeBytes, OnlineFixSizeBytes = OnlineFixSizeBytes, CustomPackageSizeBytes = CustomPackageSizeBytes,
        InstallationGuide = InstallationGuide, ImportantNotes = ImportantNotes,
        MinimumRequirements = MinimumRequirements, RecommendedRequirements = RecommendedRequirements,
        MinimumRamGb = MinimumRamGb, RequiredStorageGb = RequiredStorageGb,
        TrailerUrl = TrailerUrl, GameplayUrl = GameplayUrl, ScreenshotUrls = [.. ScreenshotUrls],
        ReportUrl = ReportUrl, SupportEmail = SupportEmail, Featured = Featured
    };

    private static int CompareVersions(string available, string installed)
    {
        if (System.Version.TryParse(available.TrimStart('v', 'V'), out var a) &&
            System.Version.TryParse(installed.TrimStart('v', 'V'), out var b)) return a.CompareTo(b);
        return string.Equals(available, installed, StringComparison.OrdinalIgnoreCase) ? 0 : 1;
    }

    private static string HumanizeAge(DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
        var age = DateTime.UtcNow - utc;
        if (age.TotalMinutes < 1) return "just now";
        if (age.TotalHours < 1) return $"{Math.Max(1, (int)age.TotalMinutes)} min ago";
        if (age.TotalDays < 1) return $"{Math.Max(1, (int)age.TotalHours)} hour{((int)age.TotalHours == 1 ? "" : "s")} ago";
        if (age.TotalDays < 30) return $"{Math.Max(1, (int)age.TotalDays)} day{((int)age.TotalDays == 1 ? "" : "s")} ago";
        return utc.ToLocalTime().ToString("MMM d, yyyy");
    }

    private static string FormatBytes(long value)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = value;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return $"{size:0.#} {units[unit]}";
    }

    private static string CleanSectionText(string value, params string[] headings)
    {
        var lines = value.Replace("\r\n", "\n").Split('\n').Select(line => line.Trim()).ToList();
        while (lines.Count > 0 && (lines[0].Length == 0 || headings.Any(heading => lines[0].Equals(heading, StringComparison.OrdinalIgnoreCase)))) lines.RemoveAt(0);
        return string.Join(Environment.NewLine, lines).Trim();
    }
}

public sealed class CatalogDocument
{
    public int SchemaVersion { get; set; } = 1;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    public string CommunityApiUrl { get; set; } = "";
    public List<GameEntry> Games { get; set; } = [];
}

public sealed class AppSettings
{
    public string LibraryFolder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NexaPlay Library");
    public string RemoteCatalogUrl { get; set; } = "";
    public string ProtectedSteamGridDbKey { get; set; } = "";
    public bool AutoSyncCatalog { get; set; }
    public bool KeepArchives { get; set; }
    public string ProfileCpu { get; set; } = "";
    public string ProfileGpu { get; set; } = "";
    public double? ProfileRamGb { get; set; }
    public string ProfileOs { get; set; } = "";
    public bool HasCompletedSetup { get; set; }
    public string PlayerName { get; set; } = "Player";
    public string ProfileImagePath { get; set; } = "";

    public AppSettings Clone() => new()
    {
        LibraryFolder = LibraryFolder,
        RemoteCatalogUrl = RemoteCatalogUrl,
        ProtectedSteamGridDbKey = ProtectedSteamGridDbKey,
        AutoSyncCatalog = AutoSyncCatalog,
        KeepArchives = KeepArchives,
        ProfileCpu = ProfileCpu,
        ProfileGpu = ProfileGpu,
        ProfileRamGb = ProfileRamGb,
        ProfileOs = ProfileOs,
        HasCompletedSetup = HasCompletedSetup,
        PlayerName = PlayerName,
        ProfileImagePath = ProfileImagePath
    };
}

public sealed record UpdateChannel(string ManifestUrl = "", string CatalogUrl = "");

public sealed record UpdateRelease(string Version, string InstallerUrl, string Sha256, string Notes = "", DateTime? PublishedUtc = null);

public sealed class UserLibraryState
{
    public HashSet<string> FavoriteGameIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string CommunityUserId { get; set; } = Guid.NewGuid().ToString("N");
    public Dictionary<string, int> LocalRatings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed record SteamMetadata(
    string Title,
    string Description,
    string Developer,
    string Publisher,
    string ReleaseDate,
    List<string> Genres,
    List<string> Tags,
    bool IsMultiplayer,
    double CommunityRating,
    int RatingCount,
    string MinimumRequirements,
    string RecommendedRequirements,
    double? MinimumRamGb,
    double? RequiredStorageGb,
    string CoverUrl,
    string HeroUrl,
    List<string> ScreenshotUrls,
    string TrailerUrl,
    string GameplayUrl);

public sealed record SteamMatch(int AppId, SteamMetadata Metadata);

public sealed record ArtworkResult(int SteamGridDbId, string CoverUrl, string HeroUrl);

public sealed record DownloadProgress(
    double Percent,
    long BytesReceived,
    long? TotalBytes,
    string Message,
    double BytesPerSecond = 0,
    TimeSpan? Eta = null,
    DateTime? EstimatedFinishLocal = null);

public sealed class DownloadTaskItem : INotifyPropertyChanged
{
    public const string NexaPlayIconUri = "pack://application:,,,/NexaPlay;component/Assets/NexaPlay-512.png";
    private double _percent;
    private long _bytesReceived;
    private long? _totalBytes;
    private double _bytesPerSecond;
    private TimeSpan? _eta;
    private DateTime? _estimatedFinishLocal;
    private string _status = "Queued";
    private string _phase = "Preparing";
    private bool _isActive = true;
    private bool _isFailed;
    private bool _isQueued;

    public required string Key { get; init; }
    public required string GameId { get; init; }
    public required string GameTitle { get; init; }
    public required string CoverUrl { get; init; }
    public required DownloadPackageKind Kind { get; init; }
    public string CustomPackageLabel { get; init; } = "";
    public string PackageLabel => Kind switch { DownloadPackageKind.Game => "FULL GAME", DownloadPackageKind.Update => "GAME UPDATE", DownloadPackageKind.OnlineFix => "ONLINE FIX", DownloadPackageKind.Custom => string.IsNullOrWhiteSpace(CustomPackageLabel) ? "EXTRA PACKAGE" : CustomPackageLabel.ToUpperInvariant(), _ => "NEXAPLAY UPDATE" };
    public double Percent { get => _percent; set => Set(ref _percent, value); }
    public long BytesReceived { get => _bytesReceived; set { if (Set(ref _bytesReceived, value)) OnPropertyChanged(nameof(TransferredLabel)); } }
    public long? TotalBytes { get => _totalBytes; set { if (Set(ref _totalBytes, value)) OnPropertyChanged(nameof(TransferredLabel)); } }
    public double BytesPerSecond { get => _bytesPerSecond; set { if (Set(ref _bytesPerSecond, value)) OnPropertyChanged(nameof(SpeedLabel)); } }
    public TimeSpan? Eta { get => _eta; set { if (Set(ref _eta, value)) OnPropertyChanged(nameof(EtaLabel)); } }
    public DateTime? EstimatedFinishLocal { get => _estimatedFinishLocal; set { if (Set(ref _estimatedFinishLocal, value)) OnPropertyChanged(nameof(FinishLabel)); } }
    public string Status { get => _status; set => Set(ref _status, value); }
    public string Phase { get => _phase; set => Set(ref _phase, value); }
    public bool IsActive { get => _isActive; set => Set(ref _isActive, value); }
    public bool IsFailed { get => _isFailed; set => Set(ref _isFailed, value); }
    public bool IsQueued { get => _isQueued; set => Set(ref _isQueued, value); }
    public string SpeedLabel => BytesPerSecond > 0 ? $"{FormatBytes((long)BytesPerSecond)}/s" : "—";
    public string EtaLabel => Eta is { } eta && eta.TotalSeconds >= 0 ? FormatDuration(eta) : "—";
    public string FinishLabel => EstimatedFinishLocal is { } finish ? finish.ToString("h:mm tt") : "—";
    public string TransferredLabel => TotalBytes is > 0 ? $"{FormatBytes(BytesReceived)} / {FormatBytes(TotalBytes.Value)}" : FormatBytes(BytesReceived);

    public event PropertyChangedEventHandler? PropertyChanged;
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value; OnPropertyChanged(propertyName); return true;
    }
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    private static string FormatBytes(long value)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = Math.Max(0, value); var unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return $"{size:0.0} {units[unit]}";
    }
    private static string FormatDuration(TimeSpan value)
    {
        if (value.TotalHours >= 1) return $"{(int)value.TotalHours}h {value.Minutes}m";
        if (value.TotalMinutes >= 1) return $"{Math.Max(1, (int)value.TotalMinutes)}m {value.Seconds}s";
        return $"{Math.Max(1, value.Seconds)}s";
    }
}

public sealed class DownloadHistoryItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string GameTitle { get; set; } = "NexaPlay download";
    public string CoverUrl { get; set; } = "";
    public DownloadPackageKind Kind { get; set; }
    public string CustomPackageLabel { get; set; } = "";
    public string Outcome { get; set; } = "Complete";
    public string Status { get; set; } = "Downloaded";
    public long BytesTransferred { get; set; }
    public DateTime CompletedUtc { get; set; } = DateTime.UtcNow;

    [JsonIgnore]
    public string PackageLabel => Kind switch { DownloadPackageKind.Game => "FULL GAME", DownloadPackageKind.Update => "GAME UPDATE", DownloadPackageKind.OnlineFix => "ONLINE FIX", DownloadPackageKind.Custom => string.IsNullOrWhiteSpace(CustomPackageLabel) ? "EXTRA PACKAGE" : CustomPackageLabel.ToUpperInvariant(), _ => "NEXAPLAY UPDATE" };
    [JsonIgnore]
    public string CompletedLabel => CompletedUtc.ToLocalTime().ToString("MMM d, yyyy · h:mm tt");
    [JsonIgnore]
    public string SizeLabel => BytesTransferred > 0 ? FormatBytes(BytesTransferred) : "Size unavailable";

    private static string FormatBytes(long value)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = Math.Max(0, value); var unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return $"{size:0.0} {units[unit]}";
    }
}

public sealed record RatingSummary(double Average, int Count, int? UserScore = null);

public sealed record InstallManifest(string GameId, string Title, string Version, string ExecutablePath, DateTime InstalledUtc, DateTime? UpdatedUtc = null);

public enum DownloadPackageKind
{
    Game,
    Update,
    OnlineFix,
    Custom,
    AppUpdate
}

public sealed record PackageInfo(string Url, string Sha256, string Password, long? SizeBytes, string DisplayName);

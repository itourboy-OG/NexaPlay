using NexaPlay.Services;
using System.Globalization;
using System.Net.Http;
using System.Windows;

namespace NexaPlay;

public partial class GameEditorWindow : Window
{
    public GameEntry Game { get; private set; }
    private readonly AppSettings _settings;
    private readonly SettingsService _settingsService;
    private readonly SteamMetadataService _steam;
    private readonly SteamGridDbService _artwork;
    private readonly HttpClient _http;

    private const string DefaultGuide = "NexaPlay downloads and extracts this game automatically.\n\n1. Wait for the installation to finish.\n2. Open the game page and select Play.\n3. NexaPlay launches Run Me!.bat from the installed game folder.";
    private const string DefaultNotes = "Only install games from sources you trust and are authorized to use.\n\nKeep enough free storage for both the downloaded archive and the extracted game.\n\nIf the game does not start, check the game folder for included redistributables or support instructions.";

    public GameEditorWindow(GameEntry game, AppSettings settings, SettingsService settingsService, HttpClient http)
    {
        InitializeComponent();
        Game = game; _settings = settings; _settingsService = settingsService;
        _http = http; _steam = new SteamMetadataService(http); _artwork = new SteamGridDbService(http);
        Fill();
    }

    private void Fill()
    {
        TitleBox.Text = Game.Title == "Untitled game" ? "" : Game.Title;
        SteamIdBox.Text = Game.SteamAppId?.ToString() ?? "";
        DescriptionBox.Text = Game.Description; DeveloperBox.Text = Game.Developer; PublisherBox.Text = Game.Publisher;
        ReleaseBox.Text = Game.ReleaseDate; VersionBox.Text = Game.Version; GenresBox.Text = string.Join(", ", Game.Genres); TagsBox.Text = string.Join(", ", Game.Tags);
        RatingBox.Text = Game.CommunityRating > 0 ? Game.CommunityRating.ToString("0.0", CultureInfo.InvariantCulture) : "";
        RatingCountBox.Text = Game.RatingCount > 0 ? Game.RatingCount.ToString(CultureInfo.InvariantCulture) : "";
        MultiplayerBox.IsChecked = Game.IsMultiplayer; UpdatedBox.Text = (Game.UpdatedUtc == default ? DateTime.Now : Game.UpdatedUtc.ToLocalTime()).ToString("yyyy-MM-dd HH:mm");
        DownloadBox.Text = Game.DownloadUrl; ShaBox.Text = Game.Sha256; PasswordBox.Text = Game.ArchivePassword; SizeBox.Text = Game.DownloadSizeBytes?.ToString() ?? "";
        UpdateUrlBox.Text = Game.UpdateDownloadUrl; UpdateShaBox.Text = Game.UpdateSha256; UpdatePasswordBox.Text = Game.UpdateArchivePassword; UpdateSizeBox.Text = Game.UpdateSizeBytes?.ToString() ?? "";
        OnlineFixUrlBox.Text = Game.OnlineFixDownloadUrl; OnlineFixShaBox.Text = Game.OnlineFixSha256; OnlineFixPasswordBox.Text = Game.OnlineFixArchivePassword; OnlineFixSizeBox.Text = Game.OnlineFixSizeBytes?.ToString() ?? "";
        ExecutableBox.Text = Game.ExecutablePath; FolderBox.Text = Game.InstallFolderName;
        GuideBox.Text = string.IsNullOrWhiteSpace(Game.InstallationGuide) ? DefaultGuide : Game.InstallationGuide;
        NotesBox.Text = string.IsNullOrWhiteSpace(Game.ImportantNotes) ? DefaultNotes : Game.ImportantNotes;
        ReportUrlBox.Text = Game.ReportUrl; SupportEmailBox.Text = Game.SupportEmail; FeaturedBox.IsChecked = Game.Featured;
        MinimumBox.Text = Game.MinimumRequirements; RecommendedBox.Text = Game.RecommendedRequirements;
        MinRamBox.Text = Game.MinimumRamGb?.ToString(CultureInfo.InvariantCulture) ?? ""; StorageBox.Text = Game.RequiredStorageGb?.ToString(CultureInfo.InvariantCulture) ?? "";
        CoverBox.Text = Game.CoverUrl; HeroBox.Text = Game.HeroUrl; SgdbIdBox.Text = Game.SteamGridDbId?.ToString() ?? "";
        ScreenshotsBox.Text = string.Join(Environment.NewLine, Game.ScreenshotUrls); TrailerBox.Text = Game.TrailerUrl; GameplayBox.Text = Game.GameplayUrl;
    }

    private async void AutoFill_Click(object sender, RoutedEventArgs e)
    {
        var requestedTitle = TitleBox.Text.Trim();
        if (requestedTitle.Length == 0) { MessageBox.Show("Type the game title first.", "Auto-fill game"); return; }
        AutoFillButton.IsEnabled = false;
        try
        {
            using var metadataTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            SteamMetadata metadata;
            if (int.TryParse(SteamIdBox.Text.Trim(), out var specifiedId))
            {
                StatusText.Text = "Fetching the Steam game page…";
                metadata = await _steam.FetchAsync(specifiedId, metadataTimeout.Token);
            }
            else
            {
                StatusText.Text = $"Searching Steam for {requestedTitle}…";
                var match = await _steam.SearchAsync(requestedTitle, metadataTimeout.Token);
                specifiedId = match.AppId;
                SteamIdBox.Text = specifiedId.ToString(CultureInfo.InvariantCulture);
                metadata = match.Metadata;
            }

            ApplyMetadata(metadata);
            if (string.IsNullOrWhiteSpace(VersionBox.Text)) VersionBox.Text = "1.0";
            UpdatedBox.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

            StatusText.Text = "Checking download sizes and artwork…";
            var fullSizeTask = TryGetRemoteSizeAsync(DownloadBox.Text);
            var updateSizeTask = TryGetRemoteSizeAsync(UpdateUrlBox.Text);
            var fixSizeTask = TryGetRemoteSizeAsync(OnlineFixUrlBox.Text);
            await Task.WhenAll(fullSizeTask, updateSizeTask, fixSizeTask);
            if (await fullSizeTask is long fullSize) SizeBox.Text = fullSize.ToString(CultureInfo.InvariantCulture);
            if (await updateSizeTask is long updateSize) UpdateSizeBox.Text = updateSize.ToString(CultureInfo.InvariantCulture);
            if (await fixSizeTask is long fixSize) OnlineFixSizeBox.Text = fixSize.ToString(CultureInfo.InvariantCulture);

            var artworkKey = _settingsService.GetSteamGridDbKey(_settings);
            if (!string.IsNullOrWhiteSpace(artworkKey))
            {
                try
                {
                    using var artworkTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    var artwork = await _artwork.FindArtworkAsync(TitleBox.Text.Trim(), artworkKey, specifiedId, artworkTimeout.Token);
                    SgdbIdBox.Text = artwork.SteamGridDbId.ToString(CultureInfo.InvariantCulture);
                    if (artwork.CoverUrl.Length > 0) CoverBox.Text = artwork.CoverUrl;
                    if (artwork.HeroUrl.Length > 0) HeroBox.Text = artwork.HeroUrl;
                }
                catch { StatusText.Text = $"Matched {metadata.Title} on Steam. SteamGridDB artwork was unavailable, so Steam artwork was kept."; return; }
            }
            StatusText.Text = $"Done — matched {metadata.Title} (Steam App {specifiedId}) and filled the game page.";
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Auto-fill failed", MessageBoxButton.OK, MessageBoxImage.Error); StatusText.Text = "You can correct the title or enter the Steam App ID override and try again."; }
        finally { AutoFillButton.IsEnabled = true; }
    }

    private async void FetchSteam_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(SteamIdBox.Text, out var id)) { MessageBox.Show("Enter the numeric Steam App ID first.", "Steam metadata"); return; }
        try { StatusText.Text = "Fetching Steam metadata…"; ApplyMetadata(await _steam.FetchAsync(id)); StatusText.Text = "Steam metadata added. Review it before saving."; }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Steam metadata failed", MessageBoxButton.OK, MessageBoxImage.Error); StatusText.Text = ""; }
    }

    private void ApplyMetadata(SteamMetadata data)
    {
        TitleBox.Text = data.Title; DescriptionBox.Text = data.Description; DeveloperBox.Text = data.Developer; PublisherBox.Text = data.Publisher;
        ReleaseBox.Text = data.ReleaseDate; GenresBox.Text = string.Join(", ", data.Genres); TagsBox.Text = string.Join(", ", data.Tags);
        MultiplayerBox.IsChecked = data.IsMultiplayer; RatingBox.Text = data.CommunityRating > 0 ? data.CommunityRating.ToString("0.0", CultureInfo.InvariantCulture) : "";
        RatingCountBox.Text = data.RatingCount > 0 ? data.RatingCount.ToString(CultureInfo.InvariantCulture) : "";
        MinimumBox.Text = data.MinimumRequirements; RecommendedBox.Text = data.RecommendedRequirements;
        MinRamBox.Text = data.MinimumRamGb?.ToString(CultureInfo.InvariantCulture) ?? ""; StorageBox.Text = data.RequiredStorageGb?.ToString(CultureInfo.InvariantCulture) ?? "";
        CoverBox.Text = data.CoverUrl; HeroBox.Text = data.HeroUrl;
        ScreenshotsBox.Text = string.Join(Environment.NewLine, data.ScreenshotUrls); if (data.TrailerUrl.Length > 0) TrailerBox.Text = data.TrailerUrl;
    }

    private async Task<long?> TryGetRemoteSizeAsync(string value)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != "https" && uri.Scheme != "http")) return null;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, uri);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.IsSuccessStatusCode && response.Content.Headers.ContentLength is > 0) return response.Content.Headers.ContentLength;
        }
        catch { }
        return null;
    }

    private async void FetchArtwork_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TitleBox.Text)) { MessageBox.Show("Enter a game title first.", "SteamGridDB artwork"); return; }
        try
        {
            StatusText.Text = "Searching SteamGridDB…";
            var steamId = int.TryParse(SteamIdBox.Text.Trim(), out var parsedSteamId) ? parsedSteamId : (int?)null;
            var result = await _artwork.FindArtworkAsync(TitleBox.Text.Trim(), _settingsService.GetSteamGridDbKey(_settings), steamId);
            SgdbIdBox.Text = result.SteamGridDbId.ToString(); if (result.CoverUrl.Length > 0) CoverBox.Text = result.CoverUrl; if (result.HeroUrl.Length > 0) HeroBox.Text = result.HeroUrl;
            StatusText.Text = "SteamGridDB artwork added. Review the match before saving.";
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Artwork search failed", MessageBoxButton.OK, MessageBoxImage.Error); StatusText.Text = ""; }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var title = TitleBox.Text.Trim();
        if (title.Length == 0) { MessageBox.Show("Game title is required.", "Missing title"); return; }
        if (!ValidateUrl(DownloadBox.Text, true, "game download") || !ValidateUrl(UpdateUrlBox.Text, false, "update") || !ValidateUrl(OnlineFixUrlBox.Text, false, "online fix") ||
            !ValidateUrl(ReportUrlBox.Text, false, "report") || !ValidateUrl(TrailerBox.Text, false, "trailer") || !ValidateUrl(GameplayBox.Text, false, "gameplay")) return;
        if (!ValidateHash(ShaBox.Text, "game") || !ValidateHash(UpdateShaBox.Text, "update") || !ValidateHash(OnlineFixShaBox.Text, "online fix")) return;
        if (double.TryParse(RatingBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var rating) && rating is < 0 or > 5) { MessageBox.Show("Rating must be between 0 and 5.", "Invalid rating"); return; }

        Game.Title = title; Game.DownloadUrl = DownloadBox.Text.Trim(); Game.UpdateDownloadUrl = UpdateUrlBox.Text.Trim(); Game.OnlineFixDownloadUrl = OnlineFixUrlBox.Text.Trim();
        Game.Description = DescriptionBox.Text.Trim(); Game.Developer = DeveloperBox.Text.Trim(); Game.Publisher = PublisherBox.Text.Trim(); Game.ReleaseDate = ReleaseBox.Text.Trim(); Game.Version = VersionBox.Text.Trim();
        Game.Genres = SplitList(GenresBox.Text, ','); Game.Tags = SplitList(TagsBox.Text, ','); Game.IsMultiplayer = MultiplayerBox.IsChecked == true;
        Game.CommunityRating = double.TryParse(RatingBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out rating) ? rating : 0;
        Game.RatingCount = int.TryParse(RatingCountBox.Text, out var count) && count > 0 ? count : 0;
        Game.UpdatedUtc = DateTime.TryParse(UpdatedBox.Text.Trim(), out var updated) ? updated.ToUniversalTime() : DateTime.UtcNow;
        Game.SteamAppId = int.TryParse(SteamIdBox.Text, out var steamId) ? steamId : null; Game.SteamGridDbId = int.TryParse(SgdbIdBox.Text, out var sgdbId) ? sgdbId : null;
        Game.Sha256 = NormalizeHash(ShaBox.Text); Game.UpdateSha256 = NormalizeHash(UpdateShaBox.Text); Game.OnlineFixSha256 = NormalizeHash(OnlineFixShaBox.Text);
        Game.ArchivePassword = PasswordBox.Text; Game.UpdateArchivePassword = UpdatePasswordBox.Text; Game.OnlineFixArchivePassword = OnlineFixPasswordBox.Text;
        Game.DownloadSizeBytes = PositiveLong(SizeBox.Text); Game.UpdateSizeBytes = PositiveLong(UpdateSizeBox.Text); Game.OnlineFixSizeBytes = PositiveLong(OnlineFixSizeBox.Text);
        Game.ExecutablePath = ExecutableBox.Text.Trim(); Game.InstallFolderName = FolderBox.Text.Trim();
        Game.InstallationGuide = string.IsNullOrWhiteSpace(GuideBox.Text) ? DefaultGuide : GuideBox.Text.Trim();
        Game.ImportantNotes = string.IsNullOrWhiteSpace(NotesBox.Text) ? DefaultNotes : NotesBox.Text.Trim();
        Game.ReportUrl = ReportUrlBox.Text.Trim(); Game.SupportEmail = SupportEmailBox.Text.Trim(); Game.Featured = FeaturedBox.IsChecked == true;
        Game.MinimumRequirements = MinimumBox.Text.Trim(); Game.RecommendedRequirements = RecommendedBox.Text.Trim();
        Game.MinimumRamGb = PositiveDouble(MinRamBox.Text); Game.RequiredStorageGb = PositiveDouble(StorageBox.Text);
        Game.CoverUrl = CoverBox.Text.Trim(); Game.HeroUrl = HeroBox.Text.Trim(); Game.ScreenshotUrls = SplitLines(ScreenshotsBox.Text);
        Game.TrailerUrl = TrailerBox.Text.Trim(); Game.GameplayUrl = GameplayBox.Text.Trim();
        DialogResult = true; Close();
    }

    private bool ValidateUrl(string value, bool required, string label)
    {
        value = value.Trim();
        if (value.Length == 0) { if (!required) return true; MessageBox.Show($"Enter the {label} URL.", "Missing link"); return false; }
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && uri.Scheme != "http")) { MessageBox.Show($"Enter a valid HTTP or HTTPS {label} URL.", "Invalid link"); return false; }
        return true;
    }

    private static string NormalizeHash(string value) => value.Trim().Replace("-", "").Replace(" ", "").ToUpperInvariant();
    private bool ValidateHash(string value, string label) { var hash = NormalizeHash(value); if (hash.Length == 0 || (hash.Length == 64 && hash.All(Uri.IsHexDigit))) return true; MessageBox.Show($"The {label} SHA-256 must be 64 hexadecimal characters.", "Invalid SHA-256"); return false; }
    private static List<string> SplitList(string value, char separator) => value.Split(separator, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    private static List<string> SplitLines(string value) => value.Split(['\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    private static long? PositiveLong(string value) => long.TryParse(value, out var number) && number > 0 ? number : null;
    private static double? PositiveDouble(string value) => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && number > 0 ? number : null;
    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
}

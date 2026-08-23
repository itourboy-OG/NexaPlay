using Microsoft.Win32;
using NexaPlay.Services;
using System.Net.Http;
using System.Windows;
using System.Windows.Input;

namespace NexaPlay;

public partial class CreatorWindow : Window
{
    private CatalogDocument _catalog;
    private readonly CatalogService _catalogService;
    private readonly AppSettings _settings;
    private readonly SettingsService _settingsService;
    private readonly HttpClient _http;
    private readonly LinkHealthService _linkHealthService;
    private readonly HashSet<string> _brokenArtworkIds = new(StringComparer.OrdinalIgnoreCase);
    private Task<List<GameEntry>>? _artworkScanTask;
    private string _modeStatus = "Local owner mode — changes are saved on this PC.";
    public CatalogDocument Catalog => _catalog;
    public Func<CatalogDocument, Task>? PublishAction { get; set; }
    public Func<Task>? ReportsAction { get; set; }
    public Action? ConfigureArtworkKeyAction { get; set; }
    public Func<string>? CreateDesktopShortcutAction { get; set; }

    public CreatorWindow(CatalogDocument catalog, CatalogService catalogService, AppSettings settings, SettingsService settingsService, HttpClient http)
    {
        InitializeComponent();
        _catalog = catalog;
        _catalogService = catalogService;
        _settings = settings;
        _settingsService = settingsService;
        _http = http;
        _linkHealthService = new LinkHealthService(http);
        RefreshList();
        Loaded += async (_, _) =>
        {
            var artworkScan = ScanArtworkHealthAsync();
            var linkScan = ScanDownloadLinksAsync();
            try { await artworkScan; }
            catch { MissingArtworkOnlyBox.Content = "Missing artwork only (scan unavailable)"; }
            try { await linkScan; }
            catch { BrokenLinksOnlyBox.Content = "Broken links only (scan unavailable)"; }
        };
    }

    private void RefreshList()
    {
        var selectedId = (GamesList.SelectedItem as GameEntry)?.Id;
        var query = GameSearchBox.Text.Trim();
        var games = _catalog.Games.Where(game =>
                (query.Length == 0 || MatchesSearch(game, query)) &&
                (MissingArtworkOnlyBox.IsChecked != true || IsArtworkIncomplete(game)) &&
                (BrokenLinksOnlyBox.IsChecked != true || game.HasBrokenDownloadLink))
            .OrderBy(game => game.Title)
            .ToList();
        GamesList.ItemsSource = null;
        GamesList.ItemsSource = games;
        if (selectedId is not null) GamesList.SelectedItem = games.FirstOrDefault(game => game.Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase));
        if (GamesList.SelectedItem is null && games.Count == 1) GamesList.SelectedIndex = 0;
        StatusText.Text = $"Showing {games.Count} of {_catalog.Games.Count} games  •  {_modeStatus}";
    }

    private static bool MatchesSearch(GameEntry game, string query) =>
        game.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        game.Developer.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        game.Publisher.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        (game.SteamAppId?.ToString().Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
        game.Genres.Concat(game.Tags).Any(value => value.Contains(query, StringComparison.OrdinalIgnoreCase));

    private bool IsArtworkIncomplete(GameEntry game) =>
        string.IsNullOrWhiteSpace(game.CoverUrl) || string.IsNullOrWhiteSpace(game.HeroUrl) || game.ScreenshotUrls.Count == 0 || _brokenArtworkIds.Contains(game.Id);

    private void GameSearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => RefreshList();
    private void Search_Click(object sender, RoutedEventArgs e) { RefreshList(); if (GamesList.Items.Count > 0 && GamesList.SelectedItem is null) GamesList.SelectedIndex = 0; GamesList.Focus(); }
    private void ClearSearch_Click(object sender, RoutedEventArgs e) { GameSearchBox.Clear(); MissingArtworkOnlyBox.IsChecked = false; BrokenLinksOnlyBox.IsChecked = false; GameSearchBox.Focus(); }
    private async void MissingArtworkFilter_Changed(object sender, RoutedEventArgs e)
    {
        if (MissingArtworkOnlyBox.IsChecked == true)
        {
            try { await ScanArtworkHealthAsync(); }
            catch (Exception ex) { ShowNotification($"Artwork scan failed: {ex.Message}", true); }
        }
        RefreshList();
    }
    private void BrokenLinksFilter_Changed(object sender, RoutedEventArgs e) => RefreshList();
    private async void CheckLinks_Click(object sender, RoutedEventArgs e) => await ScanDownloadLinksAsync(true);

    private async Task ScanDownloadLinksAsync(bool announce = false)
    {
        if (!CheckLinksButton.IsEnabled) return;
        CheckLinksButton.IsEnabled = false;
        BrokenLinksOnlyBox.Content = "Broken links only (scanning…)";
        foreach (var game in _catalog.Games) game.BeginLinkHealthCheck();
        RefreshList();

        var completed = 0;
        var gate = new SemaphoreSlim(6, 6);
        try
        {
            var checks = _catalog.Games.Select(async game =>
            {
                await gate.WaitAsync();
                try
                {
                    var snapshot = await _linkHealthService.CheckGameAsync(game);
                    await Dispatcher.InvokeAsync(() =>
                    {
                        game.ApplyLinkHealth(snapshot);
                        completed++;
                        StatusText.Text = $"Checking download links…  {completed}/{_catalog.Games.Count}";
                    });
                }
                finally { gate.Release(); }
            });
            await Task.WhenAll(checks);
            var broken = _catalog.Games.Count(game => game.HasBrokenDownloadLink);
            var uncertain = _catalog.Games.Count(game => game.OverallLinkHealthState == LinkHealthState.Unreachable);
            BrokenLinksOnlyBox.Content = $"Broken links only ({broken})";
            RefreshList();
            if (broken > 0)
            {
                BrokenLinksOnlyBox.IsChecked = true;
                ShowNotification($"Found {broken} game{(broken == 1 ? "" : "s")} with expired, missing, blocked, or invalid download links. The list now shows only those games.", true);
            }
            else if (announce)
                ShowNotification(uncertain > 0 ? $"No broken links found, but {uncertain} game{(uncertain == 1 ? "" : "s")} could not be verified right now." : "All configured download links are working.", uncertain > 0);
        }
        finally { CheckLinksButton.IsEnabled = true; }
    }
    private async void GameSearchBox_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { RefreshList(); if (GamesList.Items.Count > 0 && GamesList.SelectedItem is null) GamesList.SelectedIndex = 0; await EditSelectedAsync(); } }

    public void SetOwnerMode(bool connected, string serverUrl = "")
    {
        ModeBadgeText.Text = connected ? "ONLINE PUBLISHING" : "LOCAL OWNER MODE";
        ModeBadge.Background = connected ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(38, 31, 72)) : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(23, 59, 52));
        ModeBadge.BorderBrush = connected ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(112, 91, 200)) : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(46, 138, 112));
        ModeBadgeText.Foreground = connected ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(203, 190, 255)) : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(117, 232, 191));
        ModeHelpText.Text = connected ? $"Publish sends catalog changes to {serverUrl}. The private key stays encrypted on this Windows account." : "Changes save automatically on this PC.";
        _modeStatus = connected ? "Online publishing is connected." : "Local owner mode — export the catalog or build a new installer to share changes.";
        RefreshList();
    }

    public void SetGitHubMode(string repository)
    {
        ModeBadgeText.Text = "GITHUB PUBLISHING";
        ModeBadge.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(38, 31, 72));
        ModeBadge.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(112, 91, 200));
        ModeBadgeText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(203, 190, 255));
        ModeHelpText.Text = $"Every edit saves automatically. Publish sends the catalog to {repository}; Done saves and closes.";
        _modeStatus = "GitHub publishing is connected — friends sync changes automatically.";
        RefreshList();
    }

    private void ArtworkKey_Click(object sender, RoutedEventArgs e) => ConfigureArtworkKeyAction?.Invoke();

    public void SetArtworkKeyStatus(bool configured) => ArtworkKeyButton.Header = configured ? "SteamGridDB key ✓" : "SteamGridDB key…";
    public void SetReportsAvailable(bool available) => ReportsButton.Visibility = available ? Visibility.Visible : Visibility.Collapsed;

    private void MoreTools_Click(object sender, RoutedEventArgs e)
    {
        MoreToolsButton.ContextMenu.PlacementTarget = MoreToolsButton;
        MoreToolsButton.ContextMenu.IsOpen = true;
    }
    private async void Reports_Click(object sender, RoutedEventArgs e) { if (ReportsAction is not null) await ReportsAction(); }

    private void CreateDesktopShortcut_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = CreateDesktopShortcutAction?.Invoke();
            ShowNotification(string.IsNullOrWhiteSpace(path) ? "Desktop shortcut is unavailable." : "Creator Studio desktop shortcut is ready.", string.IsNullOrWhiteSpace(path));
        }
        catch (Exception ex) { ShowNotification($"Could not create the desktop shortcut: {ex.Message}", true); }
    }

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        var game = new GameEntry { UpdatedUtc = DateTime.UtcNow };
        var editor = new GameEditorWindow(game, _settings, _settingsService, _http) { Owner = this };
        if (editor.ShowDialog() == true)
        {
            _catalog.Games.Add(editor.Game); await SaveAsync($"{editor.Game.Title} was added and saved automatically.");
            _ = CheckSingleGameLinksAsync(editor.Game);
        }
    }

    private async void Edit_Click(object sender, RoutedEventArgs e) => await EditSelectedAsync();

    private async void RefreshAll_Click(object sender, RoutedEventArgs e)
    {
        if (_catalog.Games.Count == 0) return;
        if (MessageBox.Show("Refresh Steam metadata and artwork for every catalog game?\n\nDownload links, versions, guides, notes, and package settings will not be changed.", "Refresh all metadata", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        RefreshAllButton.IsEnabled = false;
        var steam = new SteamMetadataService(_http); var artwork = new SteamGridDbService(_http);
        var artworkKey = _settingsService.GetSteamGridDbKey(_settings);
        var updated = 0; var failed = new List<string>();
        foreach (var game in _catalog.Games)
        {
            try
            {
                StatusText.Text = $"Refreshing {game.Title}…  ({updated + failed.Count + 1}/{_catalog.Games.Count})";
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                SteamMetadata metadata;
                if (game.SteamAppId is int appId) metadata = await steam.FetchAsync(appId, timeout.Token);
                else { var match = await steam.SearchAsync(game.Title, timeout.Token); game.SteamAppId = match.AppId; metadata = match.Metadata; }
                ApplyMetadata(game, metadata);
                if (!string.IsNullOrWhiteSpace(artworkKey))
                {
                    try
                    {
                        using var artTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                        var art = await artwork.FindArtworkAsync(game.Title, artworkKey, game.SteamAppId, artTimeout.Token);
                        game.SteamGridDbId = art.SteamGridDbId; if (art.CoverUrl.Length > 0) game.CoverUrl = art.CoverUrl; if (art.HeroUrl.Length > 0) game.HeroUrl = art.HeroUrl;
                    }
                    catch { }
                }
                updated++;
            }
            catch { failed.Add(game.Title); }
        }
        await SaveAsync(); RefreshAllButton.IsEnabled = true;
        StatusText.Text = failed.Count == 0 ? $"Refreshed all {updated} games automatically." : $"Refreshed {updated} games. Could not refresh: {string.Join(", ", failed)}";
    }

    private async void RepairSelectedArtwork_Click(object sender, RoutedEventArgs e)
    {
        if (GamesList.SelectedItem is not GameEntry game) { ShowNotification("Search for and select a game first.", true); return; }
        SetArtworkActionsEnabled(false);
        try
        {
            StatusText.Text = $"Repairing official artwork for {game.Title}…";
            await RepairArtworkAsync(game);
            _brokenArtworkIds.Remove(game.Id);
            await SaveAsync($"{game.Title} artwork was repaired and saved. Publish when it looks correct.");
        }
        catch (Exception ex) { ShowNotification($"Could not repair {game.Title}: {ex.Message}", true); }
        finally { SetArtworkActionsEnabled(true); }
    }

    private async void RepairMissingArtwork_Click(object sender, RoutedEventArgs e)
    {
        SetArtworkActionsEnabled(false);
        try
        {
            StatusText.Text = "Scanning the catalog for blank or broken cover artwork…";
            var missing = await ScanArtworkHealthAsync(true);
            if (missing.Count == 0) { ShowNotification("All catalog games have reachable cover artwork.", false); return; }

            var repaired = 0; var failed = new List<string>();
            foreach (var game in missing)
            {
                try
                {
                    StatusText.Text = $"Repairing {game.Title}…  ({repaired + failed.Count + 1}/{missing.Count})";
                    await RepairArtworkAsync(game);
                    _brokenArtworkIds.Remove(game.Id);
                    repaired++;
                }
                catch { failed.Add(game.Title); }
            }
            await SaveAsync();
            MissingArtworkOnlyBox.IsChecked = failed.Count > 0;
            ShowNotification(failed.Count == 0
                ? $"Repaired artwork for {repaired} game{(repaired == 1 ? "" : "s")}. Publish to send it to Player."
                : $"Repaired {repaired}. Still needs attention: {string.Join(", ", failed)}", failed.Count > 0);
        }
        catch (Exception ex) { ShowNotification($"Artwork scan failed: {ex.Message}", true); }
        finally { SetArtworkActionsEnabled(true); }
    }

    private async Task<List<GameEntry>> FindMissingArtworkAsync()
    {
        var steam = new SteamMetadataService(_http);
        var gate = new SemaphoreSlim(8, 8);
        var checks = _catalog.Games.Select(async game =>
        {
            if (string.IsNullOrWhiteSpace(game.CoverUrl) || string.IsNullOrWhiteSpace(game.HeroUrl) || game.ScreenshotUrls.Count == 0) return game;
            await gate.WaitAsync();
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
                var coverTask = steam.IsImageUrlAvailableAsync(game.CoverUrl, timeout.Token);
                var heroTask = steam.IsImageUrlAvailableAsync(game.HeroUrl, timeout.Token);
                var screenshotTask = steam.IsImageUrlAvailableAsync(game.ScreenshotUrls[0], timeout.Token);
                await Task.WhenAll(coverTask, heroTask, screenshotTask);
                return await coverTask && await heroTask && await screenshotTask ? null : game;
            }
            catch { return game; }
            finally { gate.Release(); }
        });
        return (await Task.WhenAll(checks)).Where(game => game is not null).Cast<GameEntry>().ToList();
    }

    private async Task<List<GameEntry>> ScanArtworkHealthAsync(bool force = false)
    {
        if (_artworkScanTask is null || force) _artworkScanTask = FindMissingArtworkAsync();
        MissingArtworkOnlyBox.Content = "Missing artwork only (scanning…)";
        var missing = await _artworkScanTask;
        _brokenArtworkIds.Clear();
        _brokenArtworkIds.UnionWith(missing.Select(game => game.Id));
        MissingArtworkOnlyBox.Content = $"Missing artwork only ({missing.Count})";
        RefreshList();
        return missing;
    }

    private async Task RepairArtworkAsync(GameEntry game)
    {
        var steam = new SteamMetadataService(_http);
        SteamMetadata metadata; int appId;
        if (game.SteamAppId is int existingId)
        {
            metadata = await steam.FetchAsync(existingId);
            if (SteamMetadataService.TitlesLikelyMatch(game.Title, metadata.Title)) appId = existingId;
            else { var match = await steam.SearchAsync(game.Title); appId = match.AppId; metadata = match.Metadata; }
        }
        else { var match = await steam.SearchAsync(game.Title); appId = match.AppId; metadata = match.Metadata; }

        game.SteamAppId = appId;
        game.SteamGridDbId = null;
        game.CoverUrl = metadata.CoverUrl;
        game.HeroUrl = metadata.HeroUrl;
        game.ScreenshotUrls = [.. metadata.ScreenshotUrls];
        if (metadata.TrailerUrl.Length > 0) game.TrailerUrl = metadata.TrailerUrl;
        if (metadata.GameplayUrl.Length > 0) game.GameplayUrl = metadata.GameplayUrl;

        var artworkKey = _settingsService.GetSteamGridDbKey(_settings);
        if (!string.IsNullOrWhiteSpace(artworkKey))
        {
            try
            {
                var art = await new SteamGridDbService(_http).FindArtworkAsync(game.Title, artworkKey, appId);
                game.SteamGridDbId = art.SteamGridDbId;
                if (art.CoverUrl.Length > 0) game.CoverUrl = art.CoverUrl;
                if (art.HeroUrl.Length > 0) game.HeroUrl = art.HeroUrl;
            }
            catch { }
        }
        game.UpdatedUtc = DateTime.UtcNow;
    }

    private void SetArtworkActionsEnabled(bool enabled)
    {
        RepairSelectedArtworkButton.IsEnabled = enabled;
        RepairMissingArtworkButton.IsEnabled = enabled;
        RefreshAllButton.IsEnabled = enabled;
    }

    private static void ApplyMetadata(GameEntry game, SteamMetadata data)
    {
        game.Title = data.Title; game.Description = data.Description; game.Developer = data.Developer; game.Publisher = data.Publisher;
        game.ReleaseDate = data.ReleaseDate; game.Genres = [.. data.Genres]; game.Tags = [.. data.Tags]; game.IsMultiplayer = data.IsMultiplayer;
        game.CommunityRating = data.CommunityRating; game.RatingCount = data.RatingCount; game.MinimumRequirements = data.MinimumRequirements;
        game.RecommendedRequirements = data.RecommendedRequirements; game.MinimumRamGb = data.MinimumRamGb; game.RequiredStorageGb = data.RequiredStorageGb;
        game.CoverUrl = data.CoverUrl; game.HeroUrl = data.HeroUrl; game.ScreenshotUrls = [.. data.ScreenshotUrls];
        if (data.TrailerUrl.Length > 0) game.TrailerUrl = data.TrailerUrl;
        if (data.GameplayUrl.Length > 0) game.GameplayUrl = data.GameplayUrl;
        game.UpdatedUtc = DateTime.UtcNow;
    }
    private async void GamesList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => await EditSelectedAsync();
    private async Task EditSelectedAsync()
    {
        if (GamesList.SelectedItem is not GameEntry selected) return;
        var editor = new GameEditorWindow(selected.Clone(), _settings, _settingsService, _http) { Owner = this };
        if (editor.ShowDialog() == true)
        {
            var index = _catalog.Games.IndexOf(selected);
            _catalog.Games[index] = editor.Game;
            await SaveAsync($"{editor.Game.Title} was updated and saved automatically.");
            _ = CheckSingleGameLinksAsync(editor.Game);
        }
    }

    private async Task CheckSingleGameLinksAsync(GameEntry game)
    {
        game.BeginLinkHealthCheck();
        game.ApplyLinkHealth(await _linkHealthService.CheckGameAsync(game));
        RefreshList();
        BrokenLinksOnlyBox.Content = $"Broken links only ({_catalog.Games.Count(item => item.HasBrokenDownloadLink)})";
    }

    private async void Duplicate_Click(object sender, RoutedEventArgs e)
    {
        if (GamesList.SelectedItem is not GameEntry selected) return;
        var copy = selected.Clone(); copy.Id = Guid.NewGuid().ToString("N"); copy.Title += " Copy"; copy.Featured = false;
        _catalog.Games.Add(copy); await SaveAsync($"{copy.Title} was created and saved automatically.");
    }

    private async void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (GamesList.SelectedItem is not GameEntry selected) return;
        if (MessageBox.Show($"Remove {selected.Title} from the catalog?\n\nInstalled game files will not be deleted.", "Remove catalog entry", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _catalog.Games.Remove(selected); await SaveAsync($"{selected.Title} was removed and the catalog was saved.");
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "NexaPlay catalog (*.json)|*.json|All files (*.*)|*.*" };
        if (dialog.ShowDialog(this) != true) return;
        try { _catalog = await _catalogService.ImportAsync(dialog.FileName); RefreshList(); ShowNotification("Catalog imported and saved automatically.", false); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Catalog import failed", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "NexaPlay catalog (*.json)|*.json", FileName = "nexaplay-catalog.json", AddExtension = true };
        if (dialog.ShowDialog(this) != true) return;
        try { await _catalogService.ExportAsync(_catalog, dialog.FileName); StatusText.Text = $"Exported to {dialog.FileName}"; }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Catalog export failed", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async Task<bool> SaveAsync(string message = "All catalog changes are saved on this PC.")
    {
        try
        {
            await _catalogService.SaveAsync(_catalog);
            RefreshList();
            ShowNotification(message, false);
            return true;
        }
        catch (Exception ex)
        {
            ShowNotification("Save failed — Studio is staying open so your work is not lost.", true);
            MessageBox.Show(ex.Message, "Catalog save failed", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private void ShowNotification(string message, bool isError, bool isPublished = false)
    {
        NotificationText.Text = message;
        NotificationBanner.Visibility = Visibility.Visible;
        NotificationBanner.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(isError ? (byte)66 : isPublished ? (byte)38 : (byte)23, isError ? (byte)25 : isPublished ? (byte)31 : (byte)59, isError ? (byte)32 : isPublished ? (byte)72 : (byte)52));
        NotificationBanner.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(isError ? (byte)190 : isPublished ? (byte)112 : (byte)46, isError ? (byte)65 : isPublished ? (byte)91 : (byte)138, isError ? (byte)78 : isPublished ? (byte)200 : (byte)112));
        NotificationText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(isError ? (byte)255 : isPublished ? (byte)203 : (byte)117, isError ? (byte)158 : isPublished ? (byte)190 : (byte)232, isError ? (byte)169 : isPublished ? (byte)255 : (byte)191));
    }

    private async void Save_Click(object sender, RoutedEventArgs e) => await SaveAsync();

    private async void Publish_Click(object sender, RoutedEventArgs e)
    {
        if (PublishAction is null)
        {
            ShowNotification("Publishing is not configured on this Owner Studio build.", true);
            return;
        }
        if (!await SaveAsync("Saved locally. Publishing to GitHub…")) return;
        SetActionsEnabled(false);
        try
        {
            await PublishAction(_catalog);
            ShowNotification($"Published successfully at {DateTime.Now:t}. Player libraries will refresh automatically.", false, true);
        }
        catch (Exception ex)
        {
            ShowNotification("Publishing failed, but your catalog is safely saved on this PC.", true);
            MessageBox.Show(ex.Message, "Catalog publish failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { SetActionsEnabled(true); }
    }

    private void SetActionsEnabled(bool enabled)
    {
        SaveButton.IsEnabled = enabled;
        PublishButton.IsEnabled = enabled;
        DoneButton.IsEnabled = enabled;
    }

    private async void Done_Click(object sender, RoutedEventArgs e)
    {
        if (!await SaveAsync("All changes saved. Closing Creator Studio…")) return;
        DialogResult = true;
        Close();
    }
}

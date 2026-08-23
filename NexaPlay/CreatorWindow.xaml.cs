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
    private string _modeStatus = "Local owner mode — changes are saved on this PC.";
    public CatalogDocument Catalog => _catalog;
    public Func<CatalogDocument, Task>? PublishAction { get; set; }
    public Func<Task>? ReportsAction { get; set; }
    public Action? ConfigureArtworkKeyAction { get; set; }

    public CreatorWindow(CatalogDocument catalog, CatalogService catalogService, AppSettings settings, SettingsService settingsService, HttpClient http)
    {
        InitializeComponent();
        _catalog = catalog;
        _catalogService = catalogService;
        _settings = settings;
        _settingsService = settingsService;
        _http = http;
        RefreshList();
    }

    private void RefreshList()
    {
        GamesList.ItemsSource = null;
        GamesList.ItemsSource = _catalog.Games.OrderBy(g => g.Title).ToList();
        StatusText.Text = $"{_catalog.Games.Count} game{(_catalog.Games.Count == 1 ? "" : "s")}  •  {_modeStatus}";
    }

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

    public void SetArtworkKeyStatus(bool configured) => ArtworkKeyButton.Content = configured ? "SteamGridDB key ✓" : "SteamGridDB key…";
    public void SetReportsAvailable(bool available) => ReportsButton.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
    private async void Reports_Click(object sender, RoutedEventArgs e) { if (ReportsAction is not null) await ReportsAction(); }

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        var game = new GameEntry { UpdatedUtc = DateTime.UtcNow };
        var editor = new GameEditorWindow(game, _settings, _settingsService, _http) { Owner = this };
        if (editor.ShowDialog() == true)
        {
            _catalog.Games.Add(editor.Game); await SaveAsync($"{editor.Game.Title} was added and saved automatically.");
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
        }
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

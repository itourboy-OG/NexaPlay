using NexaPlay.Services;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace NexaPlay;

public partial class MainWindow : Window
{
    private readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = true }) { Timeout = Timeout.InfiniteTimeSpan };
    private readonly CatalogService _catalogService;
    private readonly CommunityService _communityService;
    private readonly UpdateService _updateService;
    private readonly SettingsService _settingsService = new();
    private readonly UserStateService _userStateService = new();
    private readonly DownloadHistoryService _downloadHistoryService = new();
    private readonly DownloadService _downloadService;
    private readonly InstallService _installService = new();
    private readonly Dictionary<string, CancellationTokenSource> _downloads = [];
    private readonly ObservableCollection<GameEntry> _visibleGames = [];
    private readonly ObservableCollection<DownloadTaskItem> _downloadItems = [];
    private readonly ObservableCollection<DownloadTaskItem> _queuedDownloadItems = [];
    private readonly ObservableCollection<DownloadHistoryItem> _downloadHistoryItems = [];
    private readonly SemaphoreSlim _downloadGate = new(1, 1);
    private double _peakDownloadSpeed;
    private CatalogDocument _catalog = new();
    private AppSettings _settings = new();
    private bool _installedOnly;
    private bool _favoritesOnly;
    private string _quickFilter = "ALL";
    private FrameworkElement? _currentPage;
    private UpdateRelease? _availableUpdate;
    private readonly DispatcherTimer _catalogRefreshTimer;
    private string? _openGameId;
    private int _catalogSyncInProgress;
    private bool _initialized;
    private DateTime _lastCatalogSyncAttemptUtc = DateTime.MinValue;

    public MainWindow()
    {
        InitializeComponent();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("NexaPlay/1.9.4 (+Windows game library)");
        _catalogService = new CatalogService(_http);
        _communityService = new CommunityService(_http);
        _updateService = new UpdateService(_http);
        _downloadService = new DownloadService(_http);
        GamesList.ItemsSource = _visibleGames;
        DownloadsList.ItemsSource = _downloadItems;
        QueuedDownloadsList.ItemsSource = _queuedDownloadItems;
        DownloadHistoryList.ItemsSource = _downloadHistoryItems;
        DetailsPage.PackageAction = ActOnGameAsync;
        DetailsPage.PlayAction = PlayGameAsync;
        DetailsPage.FavoriteAction = SetFavoriteAsync;
        DetailsPage.RateAction = RateGameAsync;
        DetailsPage.CancelAction = CancelGameDownload;
        DetailsPage.BackAction = ShowLibraryPage;
        DetailsPage.OpenDownloadsAction = ShowDownloadsPage;
        _currentPage = LibraryPage;
        SetSelectedNav(LibraryNav);
        UpdateQuickFilterButtons();
        _catalogRefreshTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(30) };
        _catalogRefreshTimer.Tick += async (_, _) => await SyncCatalogAsync(false);
        Loaded += async (_, _) => await InitializeAsync();
        Activated += async (_, _) =>
        {
            if (_initialized && DateTime.UtcNow - _lastCatalogSyncAttemptUtc >= TimeSpan.FromSeconds(10))
                await SyncCatalogAsync(false);
        };
        Closed += (_, _) => _catalogRefreshTimer.Stop();
    }

    private async Task InitializeAsync()
    {
        try
        {
            StatusText.Text = "Loading settings…";
            _settings = await _settingsService.LoadAsync();
            if (!_settings.HasCompletedSetup)
            {
                var setup = new FirstRunSetupWindow(_settings, _settingsService) { Owner = this };
                if (setup.ShowDialog() == true) _settings = setup.Settings;
            }
            UpdateProfileUi();
            var distribution = await UpdateService.LoadChannelAsync() ?? new UpdateChannel();
            if (string.IsNullOrWhiteSpace(_settings.RemoteCatalogUrl) && !string.IsNullOrWhiteSpace(distribution.CatalogUrl))
            {
                _settings.RemoteCatalogUrl = distribution.CatalogUrl;
                _settings.AutoSyncCatalog = true;
                await _settingsService.SaveAsync(_settings);
            }
            await _userStateService.LoadAsync();
            foreach (var item in await _downloadHistoryService.LoadAsync()) _downloadHistoryItems.Add(item);
            UpdateDownloadsUi();
            Directory.CreateDirectory(_settings.LibraryFolder);
            StatusText.Text = "Loading game catalog…";
            _catalog = await _catalogService.LoadAsync();
            if (_settings.AutoSyncCatalog && !string.IsNullOrWhiteSpace(_settings.RemoteCatalogUrl))
            {
                StatusText.Text = "Syncing catalog…";
                try { _lastCatalogSyncAttemptUtc = DateTime.UtcNow; _catalog = await _catalogService.SyncAsync(_settings.RemoteCatalogUrl, preferGitHubApi: true); }
                catch (Exception ex) { StatusText.Text = $"Catalog sync skipped: {ex.Message}"; }
            }
            RefreshInstallStates();
            ApplyFilter();
            _ = RefreshCommunityRatingsAsync();
            StatusText.Text = $"Ready  •  Library: {_settings.LibraryFolder}";
            _initialized = true;
            if (_settings.AutoSyncCatalog) _catalogRefreshTimer.Start();
            _ = CheckForUpdatesAsync(false);
        }
        catch (Exception ex) { ShowError("NexaPlay could not start", ex); }
    }

    private void RefreshInstallStates()
    {
        foreach (var game in _catalog.Games)
        {
            game.IsFavorite = _userStateService.IsFavorite(game.Id);
            game.UserRating = _userStateService.GetLocalRating(game.Id);
            InstallService.RefreshInstallState(game, _settings.LibraryFolder);
            game.NotifyRuntimeChanged();
        }
    }

    private async Task RefreshCommunityRatingsAsync()
    {
        if (string.IsNullOrWhiteSpace(_catalog.CommunityApiUrl)) return;
        foreach (var game in _catalog.Games)
        {
            try
            {
                var rating = await _communityService.GetRatingAsync(_catalog.CommunityApiUrl, game.Id, _userStateService.CommunityUserId);
                if (rating is not null) game.SetCommunityRating(rating.Average, rating.Count, rating.UserScore ?? game.UserRating);
            }
            catch { }
        }
    }

    private void ApplyFilter()
    {
        var query = SearchBox.Text.Trim();
        var games = _catalog.Games.Where(g => (!_installedOnly || g.IsInstalled) && (!_favoritesOnly || g.IsFavorite) &&
            MatchesQuickFilter(g) &&
            (query.Length == 0 || g.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             g.Genres.Any(x => x.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
             g.Tags.Any(x => x.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
             g.Developer.Contains(query, StringComparison.OrdinalIgnoreCase))).ToList();
        _visibleGames.Clear();
        foreach (var game in games) _visibleGames.Add(game);
        GameCount.Text = $"{games.Count} GAME{(games.Count == 1 ? "" : "S")}";
        LibraryStatsText.Text = $"{_catalog.Games.Count(g => g.IsInstalled)} INSTALLED  ·  {_catalog.Games.Count(g => g.IsFavorite)} FAVORITES";
        EmptyState.Visibility = games.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateFeatured(games.FirstOrDefault(g => g.Featured) ?? games.FirstOrDefault());
    }

    private bool MatchesQuickFilter(GameEntry game) => _quickFilter switch
    {
        "MULTIPLAYER" => game.IsMultiplayer,
        "COOP" => game.Tags.Concat(game.Genres).Any(value => value.Contains("co-op", StringComparison.OrdinalIgnoreCase) || value.Contains("coop", StringComparison.OrdinalIgnoreCase)),
        "RECENT" => game.UpdatedUtc != default && game.UpdatedUtc.ToUniversalTime() >= DateTime.UtcNow.AddDays(-30),
        _ => true
    };

    private void UpdateFeatured(GameEntry? game)
    {
        if (game is null)
        {
            FeaturedPanel.DataContext = null;
            FeaturedHero.Source = null;
            FeaturedTitle.Text = "No games found";
            FeaturedDescription.Text = "Sync the SauceBoyz catalog or try a different search.";
            FeaturedAction.Visibility = Visibility.Collapsed;
            FeaturedAction.Tag = null;
            return;
        }
        FeaturedPanel.DataContext = game;
        FeaturedAction.Visibility = Visibility.Visible;
        FeaturedTitle.Text = game.Title;
        FeaturedDescription.Text = string.IsNullOrWhiteSpace(game.Description) ? "Ready when you are." : game.Description;
        FeaturedAction.Content = game.IsInstalled ? "PLAY" : "VIEW GAME";
        FeaturedAction.Tag = game;
        FeaturedHero.Source = TryImage(game.HeroUrl.Length > 0 ? game.HeroUrl : game.CoverUrl);
    }

    private static BitmapImage? TryImage(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        try { return new BitmapImage(uri); } catch { return null; }
    }

    private async void GameAction_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not GameEntry game) return;
        if (game.IsInstalled) await PlayGameAsync(game); else OpenGameDetails(game);
    }

    private void GameCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<Button>(e.OriginalSource as DependencyObject) is not null) return;
        if ((sender as FrameworkElement)?.Tag is GameEntry game) OpenGameDetails(game);
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null) { if (current is T match) return match; current = VisualTreeHelper.GetParent(current); }
        return null;
    }

    private async void FeaturedAction_Click(object sender, RoutedEventArgs e)
    {
        if (FeaturedAction.Tag is not GameEntry game) return;
        if (game.IsInstalled) await PlayGameAsync(game); else OpenGameDetails(game);
    }

    private void OpenGameDetails(GameEntry game)
    {
        _openGameId = game.Id;
        DetailsPage.ShowGame(game, _settings, !string.IsNullOrWhiteSpace(_catalog.CommunityApiUrl));
        NavigateTo(DetailsPage, null);
        StatusText.Text = $"Viewing {game.Title}";
    }

    private async Task PlayGameAsync(GameEntry game)
    {
        try { await InstallService.LaunchAsync(game); StatusText.Text = $"Launched {game.Title}."; }
        catch (Exception ex) { ShowError("Could not launch game", ex); }
    }

    private async Task SetFavoriteAsync(GameEntry game, bool favorite)
    {
        await _userStateService.SetFavoriteAsync(game.Id, favorite);
        game.IsFavorite = favorite; game.NotifyRuntimeChanged(); ApplyFilter();
        StatusText.Text = favorite ? $"Added {game.Title} to favorites." : $"Removed {game.Title} from favorites.";
    }

    private async Task RateGameAsync(GameEntry game, int score)
    {
        score = Math.Clamp(score, 1, 5);
        await _userStateService.SetLocalRatingAsync(game.Id, score);
        game.UserRating = score;
        if (string.IsNullOrWhiteSpace(_catalog.CommunityApiUrl))
        {
            game.SetCommunityRating(game.CommunityRating, game.RatingCount, score);
            MessageBox.Show("Your rating was saved on this PC. Shared ratings will turn on for everyone after SauceBoyz connects the NexaPlay Community service.", "Rating saved locally", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            var rating = await _communityService.RateAsync(_catalog.CommunityApiUrl, game.Id, _userStateService.CommunityUserId, score);
            game.SetCommunityRating(rating.Average, rating.Count, rating.UserScore ?? score);
            StatusText.Text = $"Rated {game.Title} {score}/5.";
        }
        catch (Exception ex) { ShowError("Could not share rating", ex); }
    }

    private void CancelGameDownload(GameEntry game)
    {
        if (_downloads.TryGetValue(game.Id, out var cancellation)) cancellation.Cancel();
    }

    private async Task ActOnGameAsync(GameEntry game, DownloadPackageKind kind)
    {
        string? externalTargetFolder = null;
        if (kind != DownloadPackageKind.Game && !game.IsInstalled)
        {
            var packageName = kind == DownloadPackageKind.Update ? "game update" : "Online Fix";
            var picker = new OpenFolderDialog
            {
                Title = $"Choose the existing {game.Title} folder for the {packageName}",
                InitialDirectory = Directory.Exists(_settings.LibraryFolder) ? _settings.LibraryFolder : null
            };
            if (picker.ShowDialog(this) != true) return;
            externalTargetFolder = picker.FolderName;
            if (MessageBox.Show($"Apply the {packageName} inside this folder?\n\n{externalTargetFolder}\n\nMatching files from the package may be replaced.", "Confirm game folder", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        }
        if (_downloads.ContainsKey(game.Id)) return;
        var cancellation = new CancellationTokenSource();
        _downloads[game.Id] = cancellation;
        var taskItem = new DownloadTaskItem { Key = game.Id, GameId = game.Id, GameTitle = game.Title, CoverUrl = game.CoverUrl, Kind = kind, Status = "Waiting for the current download", Phase = "Queued", IsActive = true, IsQueued = true };
        _queuedDownloadItems.Add(taskItem);
        UpdateDownloadsUi();
        game.IsBusy = true; game.Progress = 0; game.Activity = "Queued"; game.NotifyRuntimeChanged();
        var ownsDownloadGate = false;
        try
        {
            await _downloadGate.WaitAsync(cancellation.Token);
            ownsDownloadGate = true;
            _queuedDownloadItems.Remove(taskItem);
            _downloadItems.Insert(0, taskItem);
            taskItem.IsQueued = false; taskItem.Phase = "Preparing"; taskItem.Status = "Starting…";
            game.Activity = "Starting…"; game.NotifyRuntimeChanged();
            UpdateDownloadsUi();
            var progress = new Progress<DownloadProgress>(p =>
            {
                game.Progress = p.Percent; game.Activity = p.Message; game.NotifyRuntimeChanged();
                taskItem.Percent = p.Percent; taskItem.BytesReceived = p.BytesReceived; taskItem.TotalBytes = p.TotalBytes;
                taskItem.BytesPerSecond = p.BytesPerSecond; taskItem.Eta = p.Eta; taskItem.EstimatedFinishLocal = p.EstimatedFinishLocal;
                taskItem.Status = p.Message; taskItem.Phase = p.Message.StartsWith("Extracting", StringComparison.OrdinalIgnoreCase) ? "Installing files" : p.Message.StartsWith("Verifying", StringComparison.OrdinalIgnoreCase) ? "Verifying archive" : "Downloading";
                StatusText.Text = $"{game.Title}  •  {p.Message}";
                UpdateDownloadsUi();
            });
            var archive = await _downloadService.DownloadAsync(game, kind, _settings.LibraryFolder, progress, cancellation.Token);
            taskItem.Phase = "Installing files"; taskItem.Status = "Preparing archive…"; taskItem.BytesPerSecond = 0; taskItem.Eta = null; taskItem.EstimatedFinishLocal = null;
            game.Activity = "Preparing archive…"; game.NotifyRuntimeChanged();
            if (kind == DownloadPackageKind.Game)
            {
                await _installService.ExtractAsync(game, archive, _settings.LibraryFolder, progress, cancellation.Token);
                if (!_settings.KeepArchives && File.Exists(archive)) File.Delete(archive);
            }
            else if (externalTargetFolder is not null) await _installService.ApplyPackageToFolderAsync(game, kind, archive, externalTargetFolder, progress, cancellation.Token);
            else await _installService.ApplyPackageAsync(game, kind, archive, progress, cancellation.Token);
            InstallService.RefreshInstallState(game, _settings.LibraryFolder); game.NotifyRuntimeChanged();
            taskItem.Percent = 100; taskItem.IsActive = false; taskItem.Phase = "Complete";
            taskItem.Status = kind switch
            {
                DownloadPackageKind.Game => "Installed and ready to play",
                DownloadPackageKind.Update when externalTargetFolder is not null => $"Update applied to selected folder · v{game.Version}",
                DownloadPackageKind.Update => $"Updated to {game.Version}",
                DownloadPackageKind.OnlineFix when externalTargetFolder is not null => "Online Fix applied to selected folder",
                _ => "Online Fix applied"
            };
            StatusText.Text = $"{game.Title}  •  {taskItem.Status}";
            ApplyFilter();
        }
        catch (OperationCanceledException)
        {
            taskItem.IsActive = false; taskItem.Phase = "Canceled"; taskItem.Status = "Partial download kept for resume";
            StatusText.Text = $"Canceled {game.Title}. Partial download kept for resume.";
        }
        catch (Exception ex)
        {
            taskItem.IsActive = false; taskItem.IsFailed = true; taskItem.Phase = "Failed"; taskItem.Status = ex.Message;
            ShowError($"Could not install {game.Title}", ex);
        }
        finally
        {
            game.IsBusy = false; game.Activity = ""; game.NotifyRuntimeChanged();
            _queuedDownloadItems.Remove(taskItem); _downloadItems.Remove(taskItem);
            _downloads.Remove(game.Id); cancellation.Dispose();
            if (ownsDownloadGate) _downloadGate.Release();
            await AddDownloadHistoryAsync(taskItem);
            UpdateDownloadsUi();
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { if ((sender as Button)?.Tag is GameEntry game) CancelGameDownload(game); }
    private void CancelDownloadItem_Click(object sender, RoutedEventArgs e) { if ((sender as Button)?.Tag is string key && _downloads.TryGetValue(key, out var cancellation)) cancellation.Cancel(); }
    private async void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _downloadHistoryService.ClearAsync();
            _downloadHistoryItems.Clear();
            StatusText.Text = "Download history cleared.";
            UpdateDownloadsUi();
        }
        catch (Exception ex) { ShowError("Could not clear download history", ex); }
    }

    private void UpdateDownloadsUi()
    {
        var active = _downloadItems.Count(item => item.IsActive);
        var queued = _queuedDownloadItems.Count;
        var pending = active + queued;
        DownloadCountText.Text = pending.ToString();
        DownloadCountBadge.Visibility = pending > 0 ? Visibility.Visible : Visibility.Collapsed;
        DownloadsEmpty.Visibility = active == 0 ? Visibility.Visible : Visibility.Collapsed;
        QueueEmpty.Visibility = queued == 0 ? Visibility.Visible : Visibility.Collapsed;
        HistoryEmpty.Visibility = _downloadHistoryItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        QueueCountText.Text = $"UP NEXT ({queued})";
        HistoryCountText.Text = $"DOWNLOAD HISTORY ({_downloadHistoryItems.Count})";
        var networkSpeed = _downloadItems.Where(item => item.IsActive).Sum(item => item.BytesPerSecond);
        _peakDownloadSpeed = Math.Max(_peakDownloadSpeed, networkSpeed);
        NetworkSpeedText.Text = FormatTransferRate(networkSpeed);
        PeakSpeedText.Text = FormatTransferRate(_peakDownloadSpeed);
        var diskBusy = _downloadItems.Any(item => item.Phase.Contains("Installing", StringComparison.OrdinalIgnoreCase) || item.Phase.Contains("Verifying", StringComparison.OrdinalIgnoreCase));
        DiskActivityText.Text = diskBusy ? "INSTALLING" : active > 0 ? "DOWNLOADING" : "IDLE";
    }

    private async Task AddDownloadHistoryAsync(DownloadTaskItem item)
    {
        var historyItem = new DownloadHistoryItem
        {
            GameTitle = item.GameTitle,
            CoverUrl = item.CoverUrl,
            Kind = item.Kind,
            Outcome = item.Phase,
            Status = item.Status,
            BytesTransferred = item.BytesReceived,
            CompletedUtc = DateTime.UtcNow
        };
        _downloadHistoryItems.Insert(0, historyItem);
        while (_downloadHistoryItems.Count > 250) _downloadHistoryItems.RemoveAt(_downloadHistoryItems.Count - 1);
        try { await _downloadHistoryService.SaveAsync(_downloadHistoryItems); }
        catch (Exception ex) { StatusText.Text = $"Download finished, but history could not be saved: {ex.Message}"; }
    }

    private static string FormatTransferRate(double bytesPerSecond)
    {
        if (bytesPerSecond <= 0) return "0 B/s";
        string[] units = ["B/s", "KB/s", "MB/s", "GB/s"];
        var value = bytesPerSecond; var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.0} {units[unit]}";
    }

    private void Library_Click(object sender, RoutedEventArgs e) { _installedOnly = false; _favoritesOnly = false; SetQuickFilter("ALL", false); PageTitle.Text = "DISCOVER"; PageSubtitle.Text = "Your next game starts here"; ApplyFilter(); ShowLibraryPage(); }
    private void Installed_Click(object sender, RoutedEventArgs e) { _installedOnly = true; _favoritesOnly = false; SetQuickFilter("ALL", false); PageTitle.Text = "INSTALLED"; PageSubtitle.Text = "Ready to launch"; ApplyFilter(); NavigateTo(LibraryPage, InstalledNav); }
    private void Favorites_Click(object sender, RoutedEventArgs e) { _installedOnly = false; _favoritesOnly = true; SetQuickFilter("ALL", false); PageTitle.Text = "FAVORITES"; PageSubtitle.Text = "Games you saved"; ApplyFilter(); NavigateTo(LibraryPage, FavoritesNav); }
    private void Downloads_Click(object sender, RoutedEventArgs e) => ShowDownloadsPage();
    private void Profile_Click(object sender, RoutedEventArgs e)
    {
        var window = new PlayerProfileWindow(_settings, _settingsService) { Owner = this };
        if (window.ShowDialog() != true) return;
        _settings = window.Settings; UpdateProfileUi(); StatusText.Text = "Player profile saved.";
    }
    private void OpenGettingStarted()
    {
        var window = new FirstRunSetupWindow(_settings, _settingsService, false) { Owner = this };
        if (window.ShowDialog() == true) { _settings = window.Settings; UpdateProfileUi(); }
    }
    private void ShowLibraryPage() { NavigateTo(LibraryPage, _installedOnly ? InstalledNav : _favoritesOnly ? FavoritesNav : LibraryNav); StatusText.Text = $"Ready  •  Library: {_settings.LibraryFolder}"; }
    private void ShowDownloadsPage() { UpdateDownloadsUi(); NavigateTo(DownloadsPage, DownloadsNav); StatusText.Text = "Downloads and install activity"; }
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) { if (IsLoaded) ApplyFilter(); }

    private void QuickFilter_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is string filter) SetQuickFilter(filter, true);
    }

    private void SetQuickFilter(string filter, bool apply)
    {
        _quickFilter = filter;
        UpdateQuickFilterButtons();
        if (apply)
        {
            ApplyFilter();
            LibraryScroll.ScrollToTop();
            StatusText.Text = filter == "ALL" ? "Showing the complete catalog." : $"Filter active: {(filter == "COOP" ? "CO-OP" : filter)}";
        }
    }

    private void UpdateQuickFilterButtons()
    {
        foreach (var button in new[] { FilterAll, FilterMultiplayer, FilterCoop, FilterRecent })
        {
            var selected = string.Equals(button.Tag as string, _quickFilter, StringComparison.OrdinalIgnoreCase);
            button.Background = new SolidColorBrush(selected ? Color.FromRgb(47, 38, 85) : Color.FromRgb(16, 21, 33));
            button.BorderBrush = new SolidColorBrush(selected ? Color.FromRgb(124, 92, 250) : Color.FromRgb(40, 48, 70));
            button.Foreground = new SolidColorBrush(selected ? Color.FromRgb(225, 219, 255) : Color.FromRgb(135, 146, 169));
        }
    }

    private void NavigateTo(FrameworkElement page, Button? navButton)
    {
        if (_currentPage == page) { SetSelectedNav(navButton); return; }
        LibraryPage.Visibility = page == LibraryPage ? Visibility.Visible : Visibility.Collapsed;
        DetailsPage.Visibility = page == DetailsPage ? Visibility.Visible : Visibility.Collapsed;
        DownloadsPage.Visibility = page == DownloadsPage ? Visibility.Visible : Visibility.Collapsed;
        _currentPage = page; SetSelectedNav(navButton);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        PageHost.Opacity = 0;
        if (PageHost.RenderTransform is TranslateTransform translate)
        {
            translate.X = 18;
            translate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(18, 0, TimeSpan.FromMilliseconds(190)) { EasingFunction = ease });
        }
        PageHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(170)) { EasingFunction = ease });
    }

    private void SetSelectedNav(Button? selected)
    {
        foreach (var button in new[] { LibraryNav, InstalledNav, FavoritesNav, DownloadsNav, SettingsNav })
        {
            button.Background = Brushes.Transparent; button.BorderBrush = Brushes.Transparent; button.Foreground = new SolidColorBrush(Color.FromRgb(137, 148, 170));
        }
        if (selected is null) return;
        selected.Background = new SolidColorBrush(Color.FromRgb(20, 27, 42)); selected.BorderBrush = new SolidColorBrush(Color.FromRgb(69, 221, 242)); selected.Foreground = Brushes.White;
    }

    private void UpdateProfileUi()
    {
        var name = string.IsNullOrWhiteSpace(_settings.PlayerName) ? "Player" : _settings.PlayerName.Trim();
        ProfileNameText.Text = name.ToUpperInvariant();
        ProfileInitialsText.Text = name[..1].ToUpperInvariant();
        ProfileAvatarImage.Source = null; ProfileAvatarImage.Visibility = Visibility.Collapsed;
        if (string.IsNullOrWhiteSpace(_settings.ProfileImagePath) || !File.Exists(_settings.ProfileImagePath)) return;
        try { ProfileAvatarImage.Source = PlayerProfileWindow.LoadImage(_settings.ProfileImagePath); ProfileAvatarImage.Visibility = Visibility.Visible; }
        catch { }
    }

    private async Task SyncCatalogAsync(bool userInitiated)
    {
        if (!_settings.AutoSyncCatalog && !userInitiated) return;
        if (string.IsNullOrWhiteSpace(_settings.RemoteCatalogUrl)) return;
        if (Interlocked.Exchange(ref _catalogSyncInProgress, 1) != 0) return;
        _lastCatalogSyncAttemptUtc = DateTime.UtcNow;
        try
        {
            if (userInitiated) StatusText.Text = "Syncing the latest catalog…";
            var previousUpdatedUtc = _catalog.UpdatedUtc;
            var refreshed = await _catalogService.SyncAsync(_settings.RemoteCatalogUrl, preferGitHubApi: userInitiated);
            if (!userInitiated && refreshed.UpdatedUtc <= previousUpdatedUtc) return;
            _catalog = refreshed;
            RefreshInstallStates();
            ApplyFilter();

            if (_currentPage == DetailsPage && !string.IsNullOrWhiteSpace(_openGameId))
            {
                var refreshedGame = _catalog.Games.FirstOrDefault(game => game.Id.Equals(_openGameId, StringComparison.OrdinalIgnoreCase));
                if (refreshedGame is null) ShowLibraryPage();
                else DetailsPage.ShowGame(refreshedGame, _settings, !string.IsNullOrWhiteSpace(_catalog.CommunityApiUrl));
            }

            _ = RefreshCommunityRatingsAsync();
            StatusText.Text = $"Catalog synced at {DateTime.Now:t}  •  {_catalog.Games.Count} games";
        }
        catch (Exception ex)
        {
            if (userInitiated) ShowError("Catalog sync failed", ex);
        }
        finally { Interlocked.Exchange(ref _catalogSyncInProgress, 0); }
    }

    private async void CheckForUpdates_Click(object sender, RoutedEventArgs e)
    {
        if (_availableUpdate is null) await CheckForUpdatesAsync(true);
        if (_availableUpdate is not null) await DownloadAppUpdateAsync(_availableUpdate);
    }

    private async Task CheckForUpdatesAsync(bool userInitiated)
    {
        try
        {
            if (userInitiated) StatusText.Text = "Checking for NexaPlay updates…";
            _availableUpdate = await _updateService.CheckAsync();
            if (_availableUpdate is not null)
            {
                UpdateBannerButton.Content = $"UPDATE v{_availableUpdate.Version} READY";
                UpdateBannerButton.IsEnabled = true;
                UpdateBannerButton.Visibility = Visibility.Visible;
                UpdateBannerButton.Background = new SolidColorBrush(Color.FromRgb(43, 31, 80));
                UpdateBannerButton.BorderBrush = new SolidColorBrush(Color.FromRgb(126, 96, 224));
                UpdateBannerButton.Foreground = new SolidColorBrush(Color.FromRgb(235, 229, 255));
                StatusText.Text = $"NexaPlay v{_availableUpdate.Version} is ready to install.";
            }
            else
            {
                UpdateBannerButton.Visibility = Visibility.Collapsed;
                var message = _updateService.IsConfigured ? $"You already have the latest NexaPlay version ({_updateService.CurrentVersion.ToString(3)})." : "The automatic update channel has not been published yet. Your app is working normally.";
                if (userInitiated) MessageBox.Show(message, "NexaPlay updates", MessageBoxButton.OK, MessageBoxImage.Information);
                if (userInitiated) StatusText.Text = message;
            }
        }
        catch (Exception ex)
        {
            UpdateBannerButton.Visibility = Visibility.Collapsed;
            if (userInitiated) ShowError("Could not check for updates", ex);
        }
    }

    private async Task DownloadAppUpdateAsync(UpdateRelease release)
    {
        const string key = "__nexaplay_app_update__";
        if (_downloads.ContainsKey(key)) { ShowDownloadsPage(); return; }
        var notes = string.IsNullOrWhiteSpace(release.Notes) ? "" : $"\n\n{release.Notes}";
        if (MessageBox.Show($"Download NexaPlay v{release.Version}?{notes}\n\nThe installer will be verified before it can run.", "NexaPlay update", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        var cancellation = new CancellationTokenSource();
        _downloads[key] = cancellation;
        var item = new DownloadTaskItem { Key = key, GameId = key, GameTitle = $"NexaPlay v{release.Version}", CoverUrl = DownloadTaskItem.NexaPlayIconUri, Kind = DownloadPackageKind.AppUpdate, Status = "Waiting for the current download", Phase = "Queued", IsActive = true, IsQueued = true };
        _queuedDownloadItems.Add(item); UpdateDownloadsUi(); ShowDownloadsPage();
        var ownsDownloadGate = false;
        try
        {
            await _downloadGate.WaitAsync(cancellation.Token);
            ownsDownloadGate = true;
            _queuedDownloadItems.Remove(item); _downloadItems.Insert(0, item);
            item.IsQueued = false; item.Phase = "Downloading app update"; item.Status = "Starting…";
            UpdateDownloadsUi();
            var progress = new Progress<DownloadProgress>(p =>
            {
                item.Percent = p.Percent; item.BytesReceived = p.BytesReceived; item.TotalBytes = p.TotalBytes; item.BytesPerSecond = p.BytesPerSecond; item.Eta = p.Eta; item.EstimatedFinishLocal = p.EstimatedFinishLocal; item.Status = p.Message;
                UpdateDownloadsUi();
            });
            var installer = await _updateService.DownloadInstallerAsync(release, progress, cancellation.Token);
            item.Percent = 100; item.IsActive = false; item.Phase = "Verified and ready"; item.Status = "SHA-256 verified";
            if (MessageBox.Show("The update is downloaded and verified. Install it now? NexaPlay will close after the installer starts.", "Update ready", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
            {
                UpdateService.LaunchInstaller(installer);
                Close();
            }
        }
        catch (OperationCanceledException) { item.IsActive = false; item.Phase = "Canceled"; item.Status = "Update download canceled"; }
        catch (Exception ex) { item.IsActive = false; item.IsFailed = true; item.Phase = "Failed"; item.Status = ex.Message; ShowError("Could not download update", ex); }
        finally
        {
            _queuedDownloadItems.Remove(item); _downloadItems.Remove(item);
            _downloads.Remove(key); cancellation.Dispose();
            if (ownsDownloadGate) _downloadGate.Release();
            await AddDownloadHistoryAsync(item);
            UpdateDownloadsUi();
        }
    }

    private async void Settings_Click(object sender, RoutedEventArgs e) => await OpenSettingsAsync();
    private async Task OpenSettingsAsync()
    {
        var window = new SettingsWindow(_settings, _settingsService, _catalogService) { Owner = this };
        var saved = window.ShowDialog() == true;
        if (saved)
        {
            _settings = window.Settings; Directory.CreateDirectory(_settings.LibraryFolder); RefreshInstallStates(); ApplyFilter();
            if (_settings.AutoSyncCatalog) _catalogRefreshTimer.Start(); else _catalogRefreshTimer.Stop();
            StatusText.Text = "Settings saved.";
        }
        switch (window.RequestedAction)
        {
            case SettingsAction.SyncCatalog:
                if (string.IsNullOrWhiteSpace(_settings.RemoteCatalogUrl)) { MessageBox.Show("Add a remote catalog URL in Settings first.", "NexaPlay catalog", MessageBoxButton.OK, MessageBoxImage.Information); return; }
                await SyncCatalogAsync(true);
                break;
            case SettingsAction.CheckForUpdates:
                await CheckForUpdatesAsync(true);
                break;
            case SettingsAction.GettingStarted:
                OpenGettingStarted();
                break;
        }
    }

    private static void ShowError(string title, Exception ex) => MessageBox.Show(ex.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ClickCount == 2) Maximize_Click(sender, e); else DragMove(); }
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

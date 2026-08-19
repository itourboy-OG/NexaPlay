using NexaPlay.Services;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace NexaPlay;

public partial class MainWindow : Window
{
    private readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = true }) { Timeout = Timeout.InfiniteTimeSpan };
    private readonly CatalogService _catalogService;
    private readonly CommunityService _communityService;
    private readonly UpdateService _updateService;
    private readonly SettingsService _settingsService = new();
    private readonly UserStateService _userStateService = new();
    private readonly DownloadService _downloadService;
    private readonly InstallService _installService = new();
    private readonly Dictionary<string, CancellationTokenSource> _downloads = [];
    private readonly ObservableCollection<GameEntry> _visibleGames = [];
    private readonly ObservableCollection<DownloadTaskItem> _downloadItems = [];
    private CatalogDocument _catalog = new();
    private AppSettings _settings = new();
    private bool _installedOnly;
    private bool _favoritesOnly;
    private FrameworkElement? _currentPage;
    private UpdateRelease? _availableUpdate;

    public MainWindow()
    {
        InitializeComponent();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("NexaPlay/1.5.0 (+Windows game library)");
        _catalogService = new CatalogService(_http);
        _communityService = new CommunityService(_http);
        _updateService = new UpdateService(_http);
        _downloadService = new DownloadService(_http);
        GamesList.ItemsSource = _visibleGames;
        DownloadsList.ItemsSource = _downloadItems;
        DetailsPage.PackageAction = ActOnGameAsync;
        DetailsPage.PlayAction = PlayGameAsync;
        DetailsPage.FavoriteAction = SetFavoriteAsync;
        DetailsPage.RateAction = RateGameAsync;
        DetailsPage.CancelAction = CancelGameDownload;
        DetailsPage.BackAction = ShowLibraryPage;
        DetailsPage.OpenDownloadsAction = ShowDownloadsPage;
        _currentPage = LibraryPage;
        SetSelectedNav(LibraryNav);
        Loaded += async (_, _) => await InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            StatusText.Text = "Loading settings…";
            _settings = await _settingsService.LoadAsync();
            var distribution = await UpdateService.LoadChannelAsync() ?? new UpdateChannel();
            if (string.IsNullOrWhiteSpace(_settings.RemoteCatalogUrl) && !string.IsNullOrWhiteSpace(distribution.CatalogUrl))
            {
                _settings.RemoteCatalogUrl = distribution.CatalogUrl;
                _settings.AutoSyncCatalog = true;
                await _settingsService.SaveAsync(_settings);
            }
            await _userStateService.LoadAsync();
            Directory.CreateDirectory(_settings.LibraryFolder);
            StatusText.Text = "Loading game catalog…";
            _catalog = await _catalogService.LoadAsync();
            if (_settings.AutoSyncCatalog && !string.IsNullOrWhiteSpace(_settings.RemoteCatalogUrl))
            {
                StatusText.Text = "Syncing catalog…";
                try { _catalog = await _catalogService.SyncAsync(_settings.RemoteCatalogUrl); }
                catch (Exception ex) { StatusText.Text = $"Catalog sync skipped: {ex.Message}"; }
            }
            RefreshInstallStates();
            ApplyFilter();
            _ = RefreshCommunityRatingsAsync();
            StatusText.Text = $"Ready  •  Library: {_settings.LibraryFolder}";
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
            (query.Length == 0 || g.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             g.Genres.Any(x => x.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
             g.Tags.Any(x => x.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
             g.Developer.Contains(query, StringComparison.OrdinalIgnoreCase))).ToList();
        _visibleGames.Clear();
        foreach (var game in games) _visibleGames.Add(game);
        GameCount.Text = $"{games.Count} GAME{(games.Count == 1 ? "" : "S")}";
        EmptyState.Visibility = games.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateFeatured(games.FirstOrDefault(g => g.Featured) ?? games.FirstOrDefault());
    }

    private void UpdateFeatured(GameEntry? game)
    {
        if (game is null)
        {
            FeaturedHero.Source = null;
            FeaturedTitle.Text = "No games found";
            FeaturedDescription.Text = "Sync the SauceBoyz catalog or try a different search.";
            FeaturedAction.Visibility = Visibility.Collapsed;
            FeaturedAction.Tag = null;
            return;
        }
        FeaturedAction.Visibility = Visibility.Visible;
        FeaturedTitle.Text = game.Title;
        FeaturedDescription.Text = string.IsNullOrWhiteSpace(game.Description) ? "Ready when you are." : game.Description;
        FeaturedAction.Content = game.IsInstalled ? "Play" : "View game";
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
        if (kind != DownloadPackageKind.Game && !game.IsInstalled) { ShowError("Install the base game first", new InvalidOperationException("Updates and the Online Fix are applied inside the installed game folder.")); return; }
        if (_downloads.ContainsKey(game.Id)) return;
        var cancellation = new CancellationTokenSource();
        _downloads[game.Id] = cancellation;
        var taskItem = new DownloadTaskItem { Key = game.Id, GameId = game.Id, GameTitle = game.Title, CoverUrl = game.CoverUrl, Kind = kind, Status = "Starting…", Phase = "Preparing", IsActive = true };
        _downloadItems.Insert(0, taskItem);
        UpdateDownloadsUi();
        game.IsBusy = true; game.Progress = 0; game.Activity = "Starting…"; game.NotifyRuntimeChanged();
        try
        {
            var progress = new Progress<DownloadProgress>(p =>
            {
                game.Progress = p.Percent; game.Activity = p.Message; game.NotifyRuntimeChanged();
                taskItem.Percent = p.Percent; taskItem.BytesReceived = p.BytesReceived; taskItem.TotalBytes = p.TotalBytes;
                taskItem.BytesPerSecond = p.BytesPerSecond; taskItem.Eta = p.Eta; taskItem.EstimatedFinishLocal = p.EstimatedFinishLocal;
                taskItem.Status = p.Message; taskItem.Phase = p.Message.StartsWith("Extracting", StringComparison.OrdinalIgnoreCase) ? "Installing files" : p.Message.StartsWith("Verifying", StringComparison.OrdinalIgnoreCase) ? "Verifying archive" : "Downloading";
                StatusText.Text = $"{game.Title}  •  {p.Message}";
            });
            var archive = await _downloadService.DownloadAsync(game, kind, _settings.LibraryFolder, progress, cancellation.Token);
            taskItem.Phase = "Installing files"; taskItem.Status = "Preparing archive…"; taskItem.BytesPerSecond = 0; taskItem.Eta = null; taskItem.EstimatedFinishLocal = null;
            game.Activity = "Preparing archive…"; game.NotifyRuntimeChanged();
            if (kind == DownloadPackageKind.Game)
            {
                await _installService.ExtractAsync(game, archive, _settings.LibraryFolder, progress, cancellation.Token);
                if (!_settings.KeepArchives && File.Exists(archive)) File.Delete(archive);
            }
            else await _installService.ApplyPackageAsync(game, kind, archive, progress, cancellation.Token);
            InstallService.RefreshInstallState(game, _settings.LibraryFolder); game.NotifyRuntimeChanged();
            taskItem.Percent = 100; taskItem.IsActive = false; taskItem.Phase = "Complete";
            taskItem.Status = kind switch { DownloadPackageKind.Game => "Installed and ready to play", DownloadPackageKind.Update => $"Updated to {game.Version}", _ => "Online Fix applied" };
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
            _downloads.Remove(game.Id); cancellation.Dispose(); UpdateDownloadsUi();
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { if ((sender as Button)?.Tag is GameEntry game) CancelGameDownload(game); }
    private void CancelDownloadItem_Click(object sender, RoutedEventArgs e) { if ((sender as Button)?.Tag is string key && _downloads.TryGetValue(key, out var cancellation)) cancellation.Cancel(); }
    private void ClearFinished_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _downloadItems.Where(item => !item.IsActive).ToList()) _downloadItems.Remove(item);
        UpdateDownloadsUi();
    }

    private void UpdateDownloadsUi()
    {
        var active = _downloadItems.Count(item => item.IsActive);
        DownloadCountText.Text = active.ToString();
        DownloadCountBadge.Visibility = active > 0 ? Visibility.Visible : Visibility.Collapsed;
        DownloadsEmpty.Visibility = _downloadItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Library_Click(object sender, RoutedEventArgs e) { _installedOnly = false; _favoritesOnly = false; PageTitle.Text = "DISCOVER"; PageSubtitle.Text = "Find your next game"; ApplyFilter(); ShowLibraryPage(); }
    private void Installed_Click(object sender, RoutedEventArgs e) { _installedOnly = true; _favoritesOnly = false; PageTitle.Text = "INSTALLED"; PageSubtitle.Text = "Ready to launch"; ApplyFilter(); NavigateTo(LibraryPage, InstalledNav); }
    private void Favorites_Click(object sender, RoutedEventArgs e) { _installedOnly = false; _favoritesOnly = true; PageTitle.Text = "FAVORITES"; PageSubtitle.Text = "Games you saved"; ApplyFilter(); NavigateTo(LibraryPage, FavoritesNav); }
    private void Downloads_Click(object sender, RoutedEventArgs e) => ShowDownloadsPage();
    private void ShowLibraryPage() { NavigateTo(LibraryPage, _installedOnly ? InstalledNav : _favoritesOnly ? FavoritesNav : LibraryNav); StatusText.Text = $"Ready  •  Library: {_settings.LibraryFolder}"; }
    private void ShowDownloadsPage() { UpdateDownloadsUi(); NavigateTo(DownloadsPage, DownloadsNav); StatusText.Text = "Downloads and install activity"; }
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) { if (IsLoaded) ApplyFilter(); }

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
        foreach (var button in new[] { LibraryNav, InstalledNav, FavoritesNav, DownloadsNav, SyncNav, UpdateNav, SettingsNav })
        {
            button.Background = Brushes.Transparent; button.BorderBrush = Brushes.Transparent; button.Foreground = new SolidColorBrush(Color.FromRgb(170, 179, 199));
        }
        if (selected is null) return;
        selected.Background = new SolidColorBrush(Color.FromRgb(24, 30, 45)); selected.BorderBrush = new SolidColorBrush(Color.FromRgb(34, 211, 238)); selected.Foreground = Brushes.White;
    }

    private async void Sync_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_settings.RemoteCatalogUrl)) { OpenSettings(); return; }
        try
        {
            StatusText.Text = "Syncing catalog…"; _catalog = await _catalogService.SyncAsync(_settings.RemoteCatalogUrl);
            RefreshInstallStates(); ApplyFilter(); _ = RefreshCommunityRatingsAsync(); StatusText.Text = $"Catalog synced at {DateTime.Now:t}.";
        }
        catch (Exception ex) { ShowError("Catalog sync failed", ex); }
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
                UpdateNav.Content = $"↑     Update v{_availableUpdate.Version} ready";
                UpdateNav.Foreground = new SolidColorBrush(Color.FromRgb(117, 232, 191));
                StatusText.Text = $"NexaPlay v{_availableUpdate.Version} is ready to install.";
            }
            else if (userInitiated)
            {
                var message = _updateService.IsConfigured ? $"You already have the latest NexaPlay version ({_updateService.CurrentVersion.ToString(3)})." : "The automatic update channel has not been published yet. Your app is working normally.";
                MessageBox.Show(message, "NexaPlay updates", MessageBoxButton.OK, MessageBoxImage.Information);
                StatusText.Text = message;
            }
        }
        catch (Exception ex)
        {
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
        var item = new DownloadTaskItem { Key = key, GameId = key, GameTitle = $"NexaPlay v{release.Version}", CoverUrl = "", Kind = DownloadPackageKind.AppUpdate, Status = "Starting…", Phase = "Downloading app update", IsActive = true };
        _downloadItems.Insert(0, item); UpdateDownloadsUi(); ShowDownloadsPage();
        try
        {
            var progress = new Progress<DownloadProgress>(p =>
            {
                item.Percent = p.Percent; item.BytesReceived = p.BytesReceived; item.TotalBytes = p.TotalBytes; item.BytesPerSecond = p.BytesPerSecond; item.Eta = p.Eta; item.EstimatedFinishLocal = p.EstimatedFinishLocal; item.Status = p.Message;
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
        finally { _downloads.Remove(key); cancellation.Dispose(); UpdateDownloadsUi(); }
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => OpenSettings();
    private void OpenSettings()
    {
        var window = new SettingsWindow(_settings, _settingsService, _catalogService) { Owner = this };
        if (window.ShowDialog() == true)
        {
            _settings = window.Settings; Directory.CreateDirectory(_settings.LibraryFolder); RefreshInstallStates(); ApplyFilter(); StatusText.Text = "Settings saved.";
        }
    }

    private static void ShowError(string title, Exception ex) => MessageBox.Show(ex.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ClickCount == 2) Maximize_Click(sender, e); else DragMove(); }
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

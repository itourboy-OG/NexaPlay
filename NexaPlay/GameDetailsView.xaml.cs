using NexaPlay.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NexaPlay;

public partial class GameDetailsView : UserControl
{
    private GameEntry? _game;
    private AppSettings _settings = new();
    private bool _webViewConfigured;
    private string _activeMediaUrl = "";
    private readonly YouTubeVideoResolver _youtubeResolver = new();
    public Func<GameEntry, DownloadPackageKind, Task>? PackageAction { get; set; }
    public Func<GameEntry, Task>? PlayAction { get; set; }
    public Func<GameEntry, Task>? UninstallAction { get; set; }
    public Func<GameEntry, bool, Task>? FavoriteAction { get; set; }
    public Func<GameEntry, Task>? ReportAction { get; set; }
    public Func<GameEntry, Task>? CompatibilityAction { get; set; }
    public Func<GameEntry, Task>? LinkHealthAction { get; set; }
    public Action<GameEntry>? CancelAction { get; set; }
    public Action? BackAction { get; set; }
    public Action? OpenDownloadsAction { get; set; }

    public GameDetailsView()
    {
        InitializeComponent();
        YouTubeWebView.CreationProperties = new CoreWebView2CreationProperties
        {
            UserDataFolder = Path.Combine(AppPaths.DataRoot, "WebView2")
        };
    }

    public void ShowGame(GameEntry game, AppSettings settings, bool communityConnected)
    {
        if (_game is not null) _game.PropertyChanged -= Game_PropertyChanged;
        CloseVideo();
        _game = game; _settings = settings; DataContext = game; ScreenshotsList.ItemsSource = game.ScreenshotUrls;
        ReportMenuItem.Visibility = communityConnected ? Visibility.Visible : Visibility.Collapsed;
        game.PropertyChanged += Game_PropertyChanged;
        DetailsScroll.ScrollToTop();
        RefreshUi();
        _ = RefreshLinkHealthAsync(game);
    }

    private void Game_PropertyChanged(object? sender, PropertyChangedEventArgs e) => Dispatcher.Invoke(RefreshUi);

    private void RefreshUi()
    {
        if (_game is null) return;
        PlayButton.Visibility = _game.IsInstalled ? Visibility.Visible : Visibility.Collapsed;
        PlayButton.Content = "▶  PLAY";
        UninstallMenuItem.Visibility = _game.IsInstalled ? Visibility.Visible : Visibility.Collapsed;
        UninstallMenuItem.IsEnabled = !_game.IsBusy;
        FavoriteButton.Content = _game.IsFavorite ? "♥  FAVORITED" : "♡  FAVORITE";
        GameDownloadButton.Content = _game.HasIncompleteInstall ? "Resume install" : _game.IsInstalled ? "Reinstall" : "Download";
        GameDownloadButton.IsEnabled = !_game.IsBusy && _game.GameLinkHealth.State != LinkHealthState.Broken;
        UpdateDownloadButton.IsEnabled = !_game.IsBusy && _game.UpdateDownloadUrl.Length > 0 && _game.UpdateLinkHealth.State != LinkHealthState.Broken;
        FixDownloadButton.IsEnabled = !_game.IsBusy && _game.OnlineFixDownloadUrl.Length > 0 && _game.OnlineFixLinkHealth.State != LinkHealthState.Broken;
        CustomPackageDownloadButton.IsEnabled = !_game.IsBusy && _game.CustomPackageDownloadUrl.Length > 0 && _game.CustomLinkHealth.State != LinkHealthState.Broken;
        UpdatePackageCard.Visibility = _game.UpdateDownloadUrl.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        OnlineFixPackageCard.Visibility = _game.OnlineFixDownloadUrl.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        CustomPackageCard.Visibility = _game.CustomPackageDownloadUrl.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        DownloadCenterDescription.Text = _game.UpdateDownloadUrl.Length == 0 && _game.OnlineFixDownloadUrl.Length == 0 && _game.CustomPackageDownloadUrl.Length == 0 ? "Download and install the full game." : "Every package works separately. For an existing game outside NexaPlay, choose its folder before applying an update, Online Fix, or extra package.";
        UpdateDownloadButton.Content = _game.HasUpdate ? "Install update" : _game.IsInstalled ? "Apply update" : "Choose folder";
        FixDownloadButton.Content = _game.IsInstalled ? "Apply fix" : "Choose folder";
        CustomPackageDownloadButton.Content = _game.IsInstalled ? "Apply package" : "Choose folder";
        CustomPackageTitle.Text = _game.CustomPackageLabel.ToUpperInvariant();
        ProgressPanel.Visibility = _game.IsBusy ? Visibility.Visible : Visibility.Collapsed;
        DownloadProgress.Value = _game.Progress; ActivityText.Text = _game.Activity;
        TrailerButton.IsEnabled = _game.TrailerUrl.Length > 0; GameplayButton.IsEnabled = _game.GameplayUrl.Length > 0;
        MediaSection.Visibility = _game.ScreenshotUrls.Count > 0 || _game.TrailerUrl.Length > 0 || _game.GameplayUrl.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        GuideSection.Visibility = _game.InstallationGuide.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        NotesSection.Visibility = _game.ImportantNotes.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        RequirementsGrid.Visibility = _game.MinimumRequirements.Length > 0 || _game.RecommendedRequirements.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        GameDownloadInfo.Text = $"v{_game.Version}  •  Download {_game.SizeLabel}";
        UpdateDownloadInfo.Text = $"v{_game.Version}  •  {_game.UpdateSizeLabel}";
        FixDownloadInfo.Text = _game.OnlineFixSizeLabel;
        CustomPackageDownloadInfo.Text = _game.CustomPackageSizeLabel;
        SetLinkStatus(GameLinkStatusText, _game.GameLinkHealth);
        SetLinkStatus(UpdateLinkStatusText, _game.UpdateLinkHealth);
        SetLinkStatus(FixLinkStatusText, _game.OnlineFixLinkHealth);
        SetLinkStatus(CustomLinkStatusText, _game.CustomLinkHealth);
        UpdateLinkHealthBanner();
        var info = new List<string> { $"Compressed game download: {_game.SizeLabel}" };
        if (_game.RequiredStorageGb is > 0)
            info.Add($"Storage required after extraction: about {_game.RequiredStorageGb:0.#} GB (listed requirement)");
        else
            info.Add("Installed size: determined after extraction");
        info.Add("ZIP/RAR archives can expand to a larger installed size.");
        if (_game.UpdateDownloadUrl.Length > 0) info.Add($"Update download: {_game.UpdateSizeLabel}");
        if (_game.OnlineFixDownloadUrl.Length > 0) info.Add($"Online Fix download: {_game.OnlineFixSizeLabel}");
        if (_game.CustomPackageDownloadUrl.Length > 0) info.Add($"{_game.CustomPackageLabel} download: {_game.CustomPackageSizeLabel}");
        if (_game.ArchivePassword.Length > 0) info.Add($"Archive password: {_game.ArchivePassword}");
        if (_game.HasIncompleteInstall) info.Add("Recovery: saved archive will be reused.");
        info.Add(""); info.Add($"Available version: {_game.Version}");
        DownloadInfoText.Text = string.Join(Environment.NewLine, info);
        InstalledStatusText.Text = _game.IsInstalled ? $"INSTALLED  •  v{_game.InstalledVersion}" : "NOT INSTALLED IN NEXAPLAY LIBRARY";
        InstalledStatusCard.Background = new SolidColorBrush(_game.IsInstalled ? Color.FromRgb(18, 48, 39) : Color.FromRgb(40, 23, 29));
        InstalledStatusCard.BorderBrush = new SolidColorBrush(_game.IsInstalled ? Color.FromRgb(48, 123, 91) : Color.FromRgb(113, 50, 70));
        InstalledStatusDot.Background = new SolidColorBrush(_game.IsInstalled ? Color.FromRgb(82, 230, 173) : Color.FromRgb(255, 116, 138));
        InstalledStatusText.Foreground = new SolidColorBrush(_game.IsInstalled ? Color.FromRgb(142, 244, 202) : Color.FromRgb(255, 154, 170));
    }

    private void Back_Click(object sender, RoutedEventArgs e) => BackAction?.Invoke();
    private async void Play_Click(object sender, RoutedEventArgs e) { if (_game is not null && PlayAction is not null) await PlayAction(_game); }
    private async void Uninstall_Click(object sender, RoutedEventArgs e) { if (_game is not null && UninstallAction is not null) await UninstallAction(_game); RefreshUi(); }
    private async void DownloadGame_Click(object sender, RoutedEventArgs e) => await RunPackageAsync(DownloadPackageKind.Game);
    private async void DownloadUpdate_Click(object sender, RoutedEventArgs e) => await RunPackageAsync(DownloadPackageKind.Update);
    private async void DownloadFix_Click(object sender, RoutedEventArgs e) => await RunPackageAsync(DownloadPackageKind.OnlineFix);
    private async void DownloadCustom_Click(object sender, RoutedEventArgs e) => await RunPackageAsync(DownloadPackageKind.Custom);
    private async void CheckLinks_Click(object sender, RoutedEventArgs e) { if (_game is not null) await RefreshLinkHealthAsync(_game); }
    private async Task RefreshLinkHealthAsync(GameEntry game)
    {
        if (LinkHealthAction is null) return;
        try { await LinkHealthAction(game); }
        catch { }
        if (ReferenceEquals(_game, game)) RefreshUi();
    }
    private async Task RunPackageAsync(DownloadPackageKind kind) { if (_game is not null && PackageAction is not null) await PackageAction(_game, kind); RefreshUi(); }
    private void More_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } button) return;
        menu.PlacementTarget = button;
        menu.IsOpen = true;
    }
    private void OpenDownloads_Click(object sender, RoutedEventArgs e) => OpenDownloadsAction?.Invoke();
    private async void Favorite_Click(object sender, RoutedEventArgs e) { if (_game is not null && FavoriteAction is not null) await FavoriteAction(_game, !_game.IsFavorite); }
    private async void Trailer_Click(object sender, RoutedEventArgs e) { if (_game is not null) await PlayMediaAsync(_game.TrailerUrl, $"{_game.Title} — Trailer"); }
    private async void Gameplay_Click(object sender, RoutedEventArgs e) { if (_game is not null) await PlayMediaAsync(_game.GameplayUrl, $"{_game.Title} — Gameplay"); }
    private async Task PlayMediaAsync(string url, string title)
    {
        if (!TryWebUri(url, out var uri)) return;
        CloseVideo();
        _activeMediaUrl = uri.AbsoluteUri;
        VideoTitle.Text = title;
        VideoPanel.Visibility = Visibility.Visible;
        DetailsScroll.ScrollToVerticalOffset(Math.Max(0, MediaSection.TransformToAncestor(DetailsScroll).Transform(new Point(0, 0)).Y - 60));

        if (IsDirectVideo(uri))
        {
            VideoStatus.Text = "Playing inside NexaPlay";
            DirectVideoPlayer.Visibility = Visibility.Visible;
            DirectVideoPlayer.Source = uri;
            DirectVideoPlayer.Play();
            return;
        }

        if (!IsYouTubeUri(uri))
        {
            ShowVideoFallback("This video provider cannot be embedded safely. Use Open in browser instead.");
            return;
        }

        try
        {
            VideoStatus.Text = "Finding the actual video…";
            VideoFallbackText.Text = "Finding the actual video…";
            VideoFallbackText.Visibility = Visibility.Visible;
            var playerUri = await _youtubeResolver.ResolvePlayerUriAsync(uri);
            if (playerUri is null)
            {
                ShowVideoFallback("NexaPlay could not find a playable video for this link. Use Open in browser instead.");
                return;
            }
            _activeMediaUrl = playerUri.AbsoluteUri;
            VideoStatus.Text = "Playing muted  •  Use the player for volume and fullscreen";
            YouTubeWebView.Visibility = Visibility.Visible;
            await YouTubeWebView.EnsureCoreWebView2Async();
            ConfigureWebView();
            NavigateYouTubePlayer(playerUri);
        }
        catch (Exception ex)
        {
            ShowVideoFallback($"The in-app player is unavailable ({ex.Message}). Use Open in browser instead.");
        }
    }

    private void ConfigureWebView()
    {
        if (_webViewConfigured || YouTubeWebView.CoreWebView2 is null) return;
        _webViewConfigured = true;
        var settings = YouTubeWebView.CoreWebView2.Settings;
        settings.AreDevToolsEnabled = false;
        settings.AreDefaultContextMenusEnabled = false;
        settings.IsStatusBarEnabled = false;
        YouTubeWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            "nexaplay.local",
            Path.Combine(AppContext.BaseDirectory, "Assets"),
            CoreWebView2HostResourceAccessKind.DenyCors);
        YouTubeWebView.CoreWebView2.NavigationCompleted += (_, _) => VideoFallbackText.Visibility = Visibility.Collapsed;
        YouTubeWebView.CoreWebView2.NavigationStarting += (_, args) =>
        {
            if (args.Uri.Equals("about:blank", StringComparison.OrdinalIgnoreCase)) return;
            if (!TryWebUri(args.Uri, out var target) || (!IsYouTubeUri(target) && !IsPlayerShellUri(target))) args.Cancel = true;
        };
        YouTubeWebView.CoreWebView2.NewWindowRequested += (_, args) =>
        {
            args.Handled = true;
            if (TryWebUri(args.Uri, out var target) && IsYouTubeUri(target))
                YouTubeWebView.CoreWebView2.Navigate(target.AbsoluteUri);
        };
    }

    private void NavigateYouTubePlayer(Uri playerUri)
    {
        if (YouTubeWebView.CoreWebView2 is null) return;
        var videoId = YouTubeVideoResolver.TryGetVideoId(playerUri);
        if (videoId.Length == 0) return;
        YouTubeWebView.CoreWebView2.Navigate($"https://nexaplay.local/youtube-player.html?video={Uri.EscapeDataString(videoId)}");
    }

    private void OpenVideoInBrowser_Click(object sender, RoutedEventArgs e) => OpenUrl(_activeMediaUrl);
    private void CloseVideo_Click(object sender, RoutedEventArgs e) => CloseVideo();
    private void CloseVideo()
    {
        DirectVideoPlayer.Stop();
        DirectVideoPlayer.Source = null;
        if (YouTubeWebView.CoreWebView2 is not null) YouTubeWebView.CoreWebView2.Navigate("about:blank");
        DirectVideoPlayer.Visibility = Visibility.Collapsed;
        YouTubeWebView.Visibility = Visibility.Collapsed;
        VideoFallbackText.Visibility = Visibility.Collapsed;
        VideoPanel.Visibility = Visibility.Collapsed;
        _activeMediaUrl = "";
    }

    private void ShowVideoFallback(string message)
    {
        YouTubeWebView.Visibility = Visibility.Collapsed;
        DirectVideoPlayer.Visibility = Visibility.Collapsed;
        VideoStatus.Text = "Open externally if needed";
        VideoFallbackText.Text = message;
        VideoFallbackText.Visibility = Visibility.Visible;
    }

    private void DirectVideoPlayer_MediaFailed(object sender, ExceptionRoutedEventArgs e) =>
        ShowVideoFallback($"The video could not be decoded ({e.ErrorException?.Message ?? "unknown media error"}). Use Open in browser instead.");
    private void DirectVideoPlayer_MediaEnded(object sender, RoutedEventArgs e) => DirectVideoPlayer.Position = TimeSpan.Zero;
    private static bool TryWebUri(string url, out Uri uri) =>
        Uri.TryCreate(url, UriKind.Absolute, out uri!) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
    private static bool IsDirectVideo(Uri uri) => new[] { ".mp4", ".webm", ".m4v", ".mov" }.Contains(Path.GetExtension(uri.AbsolutePath), StringComparer.OrdinalIgnoreCase);
    private static bool IsYouTubeUri(Uri uri) => uri.Host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase) || uri.Host.Equals("youtube.com", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".youtube.com", StringComparison.OrdinalIgnoreCase);
    private static bool IsPlayerShellUri(Uri uri) => uri.Host.Equals("nexaplay.local", StringComparison.OrdinalIgnoreCase);
    private static void OpenUrl(string url) { if (Uri.TryCreate(url, UriKind.Absolute, out var uri)) Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
    private async void Report_Click(object sender, RoutedEventArgs e) { if (_game is not null && ReportAction is not null) await ReportAction(_game); }
    private async void CanIRun_Click(object sender, RoutedEventArgs e) { if (_game is not null && CompatibilityAction is not null) await CompatibilityAction(_game); }
    private void TechnicalCity_Click(object sender, RoutedEventArgs e)
    {
        if (_game is not null) OpenUrl(SystemRequirementsService.GetTechnicalCityGameUrl(_game.Title));
    }

    private void UpdateLinkHealthBanner()
    {
        if (_game is null) return;
        var (text, background, border, foreground) = _game.OverallLinkHealthState switch
        {
            LinkHealthState.Available => ("ALL DOWNLOAD LINKS ARE WORKING", Color.FromRgb(18, 48, 39), Color.FromRgb(48, 123, 91), Color.FromRgb(142, 244, 202)),
            LinkHealthState.Broken => ($"LINK PROBLEM FOUND  •  {_game.LinkHealthDetails}", Color.FromRgb(57, 22, 29), Color.FromRgb(151, 54, 72), Color.FromRgb(255, 153, 169)),
            LinkHealthState.Unreachable => ($"COULD NOT VERIFY EVERY LINK  •  {_game.LinkHealthDetails}", Color.FromRgb(55, 43, 20), Color.FromRgb(143, 104, 38), Color.FromRgb(255, 207, 116)),
            LinkHealthState.Checking => ("CHECKING DOWNLOAD LINKS…", Color.FromRgb(23, 29, 43), Color.FromRgb(56, 67, 94), Color.FromRgb(200, 208, 223)),
            _ => ("DOWNLOAD LINKS HAVE NOT BEEN CHECKED", Color.FromRgb(23, 29, 43), Color.FromRgb(56, 67, 94), Color.FromRgb(200, 208, 223))
        };
        LinkHealthText.Text = text;
        LinkHealthBanner.Background = new SolidColorBrush(background);
        LinkHealthBanner.BorderBrush = new SolidColorBrush(border);
        LinkHealthText.Foreground = new SolidColorBrush(foreground);
        LinkHealthDot.Background = new SolidColorBrush(foreground);
    }

    private static void SetLinkStatus(TextBlock target, LinkHealthResult result)
    {
        target.Text = result.Message;
        target.Foreground = new SolidColorBrush(result.State switch
        {
            LinkHealthState.Available => Color.FromRgb(82, 230, 173),
            LinkHealthState.Broken => Color.FromRgb(255, 116, 138),
            LinkHealthState.Unreachable => Color.FromRgb(255, 196, 92),
            _ => Color.FromRgb(136, 148, 170)
        });
    }
}

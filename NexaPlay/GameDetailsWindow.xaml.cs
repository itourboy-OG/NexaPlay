using NexaPlay.Services;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace NexaPlay;

public partial class GameDetailsWindow : Window
{
    private readonly GameEntry _game;
    private readonly AppSettings _settings;
    private readonly Func<GameEntry, DownloadPackageKind, Task> _packageAction;
    private readonly Func<GameEntry, Task> _playAction;
    private readonly Func<GameEntry, bool, Task> _favoriteAction;
    private readonly Action<GameEntry> _cancelAction;

    public GameDetailsWindow(GameEntry game, AppSettings settings,
        Func<GameEntry, DownloadPackageKind, Task> packageAction,
        Func<GameEntry, Task> playAction,
        Func<GameEntry, bool, Task> favoriteAction,
        Action<GameEntry> cancelAction)
    {
        InitializeComponent();
        _game = game; _settings = settings; _packageAction = packageAction; _playAction = playAction; _favoriteAction = favoriteAction; _cancelAction = cancelAction;
        DataContext = game; Title = $"{game.Title} — NexaPlay"; ScreenshotsList.ItemsSource = game.ScreenshotUrls;
        game.PropertyChanged += Game_PropertyChanged; Closed += (_, _) => game.PropertyChanged -= Game_PropertyChanged;
        RefreshUi();
    }

    private void Game_PropertyChanged(object? sender, PropertyChangedEventArgs e) => Dispatcher.Invoke(RefreshUi);

    private void RefreshUi()
    {
        PlayButton.Visibility = _game.IsInstalled ? Visibility.Visible : Visibility.Collapsed;
        PlayButton.Content = "▶  PLAY WITH RUN ME!.BAT";
        FavoriteButton.Content = _game.IsFavorite ? "♥  FAVORITED" : "♡  FAVORITE";
        GameDownloadButton.Content = _game.HasIncompleteInstall ? "Resume install" : _game.IsInstalled ? "Reinstall" : "Download";
        GameDownloadButton.IsEnabled = !_game.IsBusy;
        UpdateDownloadButton.IsEnabled = !_game.IsBusy && _game.IsInstalled && _game.UpdateDownloadUrl.Length > 0;
        FixDownloadButton.IsEnabled = !_game.IsBusy && _game.IsInstalled && _game.OnlineFixDownloadUrl.Length > 0;
        UpdatePackageCard.Visibility = _game.UpdateDownloadUrl.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        OnlineFixPackageCard.Visibility = _game.OnlineFixDownloadUrl.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        DownloadCenterDescription.Text = _game.UpdateDownloadUrl.Length == 0 && _game.OnlineFixDownloadUrl.Length == 0
            ? "Download and install the full game."
            : "Choose the full game or one of the available add-on packages.";
        UpdateDownloadButton.Content = _game.HasUpdate ? "Install update" : "Apply";
        UpdateAvailableBadge.Visibility = _game.HasUpdate ? Visibility.Visible : Visibility.Collapsed;
        ProgressPanel.Visibility = _game.IsBusy ? Visibility.Visible : Visibility.Collapsed;
        DownloadProgress.Value = _game.Progress; ActivityText.Text = _game.Activity;
        FooterStatus.Text = _game.IsBusy ? _game.Activity : _game.HasIncompleteInstall ? "Interrupted install found — ready to resume" : _game.HasUpdate ? $"Update {_game.Version} is ready" : "Ready";
        TrailerButton.IsEnabled = _game.TrailerUrl.Length > 0; GameplayButton.IsEnabled = _game.GameplayUrl.Length > 0;
        MediaSection.Visibility = _game.ScreenshotUrls.Count > 0 || _game.TrailerUrl.Length > 0 || _game.GameplayUrl.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        GuideSection.Visibility = _game.InstallationGuide.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        NotesSection.Visibility = _game.ImportantNotes.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        RequirementsGrid.Visibility = _game.MinimumRequirements.Length > 0 || _game.RecommendedRequirements.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        GameDownloadInfo.Text = $"v{_game.Version}  •  {_game.SizeLabel}";
        UpdateDownloadInfo.Text = $"v{_game.Version}  •  {_game.UpdateSizeLabel}";
        FixDownloadInfo.Text = _game.OnlineFixSizeLabel;
        var info = new List<string> { $"Full game: {_game.SizeLabel}" };
        if (_game.UpdateDownloadUrl.Length > 0) info.Add($"Update: {_game.UpdateSizeLabel}");
        if (_game.OnlineFixDownloadUrl.Length > 0) info.Add($"Online Fix: {_game.OnlineFixSizeLabel}");
        if (_game.ArchivePassword.Length > 0) info.Add($"Archive password: {_game.ArchivePassword}");
        if (_game.UpdateDownloadUrl.Length > 0 && _game.UpdateArchivePassword.Length > 0) info.Add($"Update password: {_game.UpdateArchivePassword}");
        if (_game.OnlineFixDownloadUrl.Length > 0 && _game.OnlineFixArchivePassword.Length > 0) info.Add($"Online Fix password: {_game.OnlineFixArchivePassword}");
        if (_game.HasIncompleteInstall) info.Add("Recovery: interrupted install found; the saved archive will be reused.");
        info.Add("");
        info.Add($"Installed version: {(_game.IsInstalled ? _game.InstalledVersion : "Not installed")}");
        info.Add($"Catalog version: {_game.Version}");
        DownloadInfoText.Text = string.Join(Environment.NewLine, info);
    }

    private async void Play_Click(object sender, RoutedEventArgs e) => await _playAction(_game);
    private async void DownloadGame_Click(object sender, RoutedEventArgs e) => await RunPackageAsync(DownloadPackageKind.Game);
    private async void DownloadUpdate_Click(object sender, RoutedEventArgs e) => await RunPackageAsync(DownloadPackageKind.Update);
    private async void DownloadFix_Click(object sender, RoutedEventArgs e) => await RunPackageAsync(DownloadPackageKind.OnlineFix);
    private async Task RunPackageAsync(DownloadPackageKind kind) { await _packageAction(_game, kind); RefreshUi(); }
    private void CancelDownload_Click(object sender, RoutedEventArgs e) => _cancelAction(_game);
    private void DownloadJump_Click(object sender, RoutedEventArgs e) { DownloadSection.BringIntoView(); }

    private async void Favorite_Click(object sender, RoutedEventArgs e)
    {
        await _favoriteAction(_game, !_game.IsFavorite); RefreshUi();
    }

    private void Trailer_Click(object sender, RoutedEventArgs e) => OpenUrl(_game.TrailerUrl);
    private void Gameplay_Click(object sender, RoutedEventArgs e) => OpenUrl(_game.GameplayUrl);
    private static void OpenUrl(string url) { if (Uri.TryCreate(url, UriKind.Absolute, out var uri)) Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }

    private void Report_Click(object sender, RoutedEventArgs e)
    {
        var body = $"NexaPlay game report%0D%0A%0D%0AGame: {Uri.EscapeDataString(_game.Title)}%0D%0AVersion: {Uri.EscapeDataString(_game.Version)}%0D%0AInstalled: {Uri.EscapeDataString(_game.InstalledVersion)}%0D%0A%0D%0AProblem: ";
        if (Uri.TryCreate(_game.ReportUrl, UriKind.Absolute, out var reportUri)) OpenUrl(reportUri.AbsoluteUri);
        else if (_game.SupportEmail.Length > 0) OpenUrl($"mailto:{Uri.EscapeDataString(_game.SupportEmail)}?subject={Uri.EscapeDataString("NexaPlay report: " + _game.Title)}&body={body}");
        else
        {
            Clipboard.SetText($"NexaPlay game report\n\nGame: {_game.Title}\nCatalog version: {_game.Version}\nInstalled version: {_game.InstalledVersion}\n\nProblem: ");
            MessageBox.Show("A report template was copied to your clipboard. Add a Report URL or support email in Creator Studio so reports can reach SauceBoyz directly.", "Report copied", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void CanIRun_Click(object sender, RoutedEventArgs e)
    {
        var profile = new HardwareProfileWindow(_game, _settings, new SettingsService()) { Owner = this };
        if (profile.ShowDialog() == true)
            MessageBox.Show(SystemRequirementsService.Check(_game, _settings.LibraryFolder, _settings), $"Can I run {_game.Title}?", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}

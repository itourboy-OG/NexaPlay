using NexaPlay.Services;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NexaPlay;

public partial class GameDetailsView : UserControl
{
    private GameEntry? _game;
    private AppSettings _settings = new();
    private bool _communityConnected;
    public Func<GameEntry, DownloadPackageKind, Task>? PackageAction { get; set; }
    public Func<GameEntry, Task>? PlayAction { get; set; }
    public Func<GameEntry, bool, Task>? FavoriteAction { get; set; }
    public Func<GameEntry, int, Task>? RateAction { get; set; }
    public Action<GameEntry>? CancelAction { get; set; }
    public Action? BackAction { get; set; }
    public Action? OpenDownloadsAction { get; set; }

    public GameDetailsView() => InitializeComponent();

    public void ShowGame(GameEntry game, AppSettings settings, bool communityConnected)
    {
        if (_game is not null) _game.PropertyChanged -= Game_PropertyChanged;
        _game = game; _settings = settings; _communityConnected = communityConnected; DataContext = game; ScreenshotsList.ItemsSource = game.ScreenshotUrls;
        RatingHelperText.Text = communityConnected ? "Your vote updates the community score for everyone." : "Your vote stays on this PC until SauceBoyz connects the Community service.";
        game.PropertyChanged += Game_PropertyChanged;
        DetailsScroll.ScrollToTop();
        RefreshUi();
    }

    private void Game_PropertyChanged(object? sender, PropertyChangedEventArgs e) => Dispatcher.Invoke(RefreshUi);

    private void RefreshUi()
    {
        if (_game is null) return;
        PlayButton.Visibility = _game.IsInstalled ? Visibility.Visible : Visibility.Collapsed;
        PlayButton.Content = "▶  PLAY";
        FavoriteButton.Content = _game.IsFavorite ? "♥  FAVORITED" : "♡  FAVORITE";
        GameDownloadButton.Content = _game.HasIncompleteInstall ? "Resume install" : _game.IsInstalled ? "Reinstall" : "Download";
        GameDownloadButton.IsEnabled = !_game.IsBusy;
        UpdateDownloadButton.IsEnabled = !_game.IsBusy && _game.IsInstalled && _game.UpdateDownloadUrl.Length > 0;
        FixDownloadButton.IsEnabled = !_game.IsBusy && _game.IsInstalled && _game.OnlineFixDownloadUrl.Length > 0;
        UpdatePackageCard.Visibility = _game.UpdateDownloadUrl.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        OnlineFixPackageCard.Visibility = _game.OnlineFixDownloadUrl.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        DownloadCenterDescription.Text = _game.UpdateDownloadUrl.Length == 0 && _game.OnlineFixDownloadUrl.Length == 0 ? "Download and install the full game." : "Choose the full game or an available add-on package.";
        UpdateDownloadButton.Content = _game.HasUpdate ? "Install update" : "Apply";
        ProgressPanel.Visibility = _game.IsBusy ? Visibility.Visible : Visibility.Collapsed;
        DownloadProgress.Value = _game.Progress; ActivityText.Text = _game.Activity;
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
        if (_game.HasIncompleteInstall) info.Add("Recovery: saved archive will be reused.");
        info.Add(""); info.Add($"Installed: {(_game.IsInstalled ? _game.InstalledVersion : "Not installed")}"); info.Add($"Available: {_game.Version}");
        DownloadInfoText.Text = string.Join(Environment.NewLine, info);
        RefreshRatingButtons();
    }

    private void RefreshRatingButtons()
    {
        if (_game is null) return;
        var selected = _game.UserRating ?? 0;
        var buttons = new[] { Star1, Star2, Star3, Star4, Star5 };
        for (var i = 0; i < buttons.Length; i++) buttons[i].Foreground = new SolidColorBrush(i < selected ? Color.FromRgb(255, 214, 107) : Color.FromRgb(96, 106, 130));
        RatingStatus.Text = selected > 0 ? $"You rated this {selected}/5  •  {_game.RatingLabel}" : _communityConnected ? _game.RatingLabel : "COMMUNITY SERVICE NOT CONNECTED";
    }

    private void Back_Click(object sender, RoutedEventArgs e) => BackAction?.Invoke();
    private async void Play_Click(object sender, RoutedEventArgs e) { if (_game is not null && PlayAction is not null) await PlayAction(_game); }
    private async void DownloadGame_Click(object sender, RoutedEventArgs e) => await RunPackageAsync(DownloadPackageKind.Game);
    private async void DownloadUpdate_Click(object sender, RoutedEventArgs e) => await RunPackageAsync(DownloadPackageKind.Update);
    private async void DownloadFix_Click(object sender, RoutedEventArgs e) => await RunPackageAsync(DownloadPackageKind.OnlineFix);
    private async Task RunPackageAsync(DownloadPackageKind kind) { if (_game is not null && PackageAction is not null) await PackageAction(_game, kind); RefreshUi(); }
    private void DownloadJump_Click(object sender, RoutedEventArgs e) => DownloadSection.BringIntoView();
    private void OpenDownloads_Click(object sender, RoutedEventArgs e) => OpenDownloadsAction?.Invoke();
    private async void Favorite_Click(object sender, RoutedEventArgs e) { if (_game is not null && FavoriteAction is not null) await FavoriteAction(_game, !_game.IsFavorite); }
    private async void Rate_Click(object sender, RoutedEventArgs e)
    {
        if (_game is null || RateAction is null || sender is not Button { Tag: string value } || !int.TryParse(value, out var score)) return;
        await RateAction(_game, score); RefreshUi();
    }
    private void Trailer_Click(object sender, RoutedEventArgs e) { if (_game is not null) OpenUrl(_game.TrailerUrl); }
    private void Gameplay_Click(object sender, RoutedEventArgs e) { if (_game is not null) OpenUrl(_game.GameplayUrl); }
    private static void OpenUrl(string url) { if (Uri.TryCreate(url, UriKind.Absolute, out var uri)) Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
    private void Report_Click(object sender, RoutedEventArgs e)
    {
        if (_game is null) return;
        var body = $"NexaPlay game report%0D%0A%0D%0AGame: {Uri.EscapeDataString(_game.Title)}%0D%0AVersion: {Uri.EscapeDataString(_game.Version)}%0D%0AInstalled: {Uri.EscapeDataString(_game.InstalledVersion)}%0D%0A%0D%0AProblem: ";
        if (Uri.TryCreate(_game.ReportUrl, UriKind.Absolute, out var reportUri)) OpenUrl(reportUri.AbsoluteUri);
        else if (_game.SupportEmail.Length > 0) OpenUrl($"mailto:{Uri.EscapeDataString(_game.SupportEmail)}?subject={Uri.EscapeDataString("NexaPlay report: " + _game.Title)}&body={body}");
        else MessageBox.Show("SauceBoyz has not connected a report destination for this game yet.", "Report game", MessageBoxButton.OK, MessageBoxImage.Information);
    }
    private void CanIRun_Click(object sender, RoutedEventArgs e)
    {
        if (_game is null) return;
        var profile = new HardwareProfileWindow(_game, _settings, new SettingsService()) { Owner = Window.GetWindow(this) };
        if (profile.ShowDialog() == true) MessageBox.Show(SystemRequirementsService.Check(_game, _settings.LibraryFolder, _settings), $"Can I run {_game.Title}?", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}

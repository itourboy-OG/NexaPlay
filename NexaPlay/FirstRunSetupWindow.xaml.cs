using Microsoft.Win32;
using NexaPlay.Services;
using System.Windows;

namespace NexaPlay;

public partial class FirstRunSetupWindow : Window
{
    private readonly SettingsService _settingsService;
    private string _selectedImagePath;
    private bool _removeImage;
    public AppSettings Settings { get; private set; }

    public FirstRunSetupWindow(AppSettings settings, SettingsService settingsService, bool firstRun = true)
    {
        InitializeComponent(); _settingsService = settingsService; Settings = settings.Clone();
        _selectedImagePath = Settings.ProfileImagePath;
        UsernameBox.Text = string.IsNullOrWhiteSpace(Settings.PlayerName) ? "Player" : Settings.PlayerName;
        if (!firstRun) { SetupTitle.Text = "NEXAPLAY QUICK START"; SkipButton.Content = "Close"; }
        UpdatePreview();
    }

    private void UsernameBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => UpdateInitial();
    private void ChoosePicture_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Choose a player picture", Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp" };
        if (dialog.ShowDialog(this) != true) return;
        _selectedImagePath = dialog.FileName; _removeImage = false; UpdatePreview();
    }
    private void RemovePicture_Click(object sender, RoutedEventArgs e) { _selectedImagePath = ""; _removeImage = true; UpdatePreview(); }

    private async void Finish_Click(object sender, RoutedEventArgs e)
    {
        var name = UsernameBox.Text.Trim();
        if (name.Length == 0) { MessageBox.Show(this, "Enter a username or choose Skip.", "NexaPlay setup"); return; }
        try
        {
            Settings.PlayerName = name; Settings.HasCompletedSetup = true;
            if (_removeImage) Settings.ProfileImagePath = "";
            else if (!string.IsNullOrWhiteSpace(_selectedImagePath) && !string.Equals(_selectedImagePath, Settings.ProfileImagePath, StringComparison.OrdinalIgnoreCase)) Settings.ProfileImagePath = await ProfileImageService.ImportAsync(_selectedImagePath);
            await _settingsService.SaveAsync(Settings); DialogResult = true; Close();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not finish setup", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void Skip_Click(object sender, RoutedEventArgs e)
    {
        Settings.HasCompletedSetup = true;
        await _settingsService.SaveAsync(Settings);
        DialogResult = true; Close();
    }

    private void UpdatePreview()
    {
        AvatarImage.Source = null; AvatarImage.Visibility = Visibility.Collapsed;
        if (!string.IsNullOrWhiteSpace(_selectedImagePath) && File.Exists(_selectedImagePath))
        {
            try { AvatarImage.Source = PlayerProfileWindow.LoadImage(_selectedImagePath); AvatarImage.Visibility = Visibility.Visible; } catch { }
        }
        RemovePictureButton.IsEnabled = AvatarImage.Visibility == Visibility.Visible; UpdateInitial();
    }
    private void UpdateInitial() { var name = UsernameBox?.Text?.Trim(); InitialsText.Text = string.IsNullOrWhiteSpace(name) ? "P" : name[..1].ToUpperInvariant(); }
}

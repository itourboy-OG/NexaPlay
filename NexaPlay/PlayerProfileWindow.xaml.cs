using Microsoft.Win32;
using NexaPlay.Services;
using System.Windows;
using System.Windows.Media.Imaging;

namespace NexaPlay;

public partial class PlayerProfileWindow : Window
{
    private readonly SettingsService _settingsService;
    private string _selectedImagePath;
    private bool _removeImage;
    public AppSettings Settings { get; private set; }

    public PlayerProfileWindow(AppSettings settings, SettingsService settingsService)
    {
        InitializeComponent();
        _settingsService = settingsService;
        Settings = settings.Clone();
        _selectedImagePath = Settings.ProfileImagePath;
        UsernameBox.Text = string.IsNullOrWhiteSpace(Settings.PlayerName) ? "Player" : Settings.PlayerName;
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

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = UsernameBox.Text.Trim();
        if (name.Length == 0) { MessageBox.Show(this, "Enter a username.", "NexaPlay profile", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        try
        {
            Settings.PlayerName = name;
            if (_removeImage) Settings.ProfileImagePath = "";
            else if (!string.IsNullOrWhiteSpace(_selectedImagePath) && !string.Equals(_selectedImagePath, Settings.ProfileImagePath, StringComparison.OrdinalIgnoreCase))
                Settings.ProfileImagePath = await ProfileImageService.ImportAsync(_selectedImagePath);
            await _settingsService.SaveAsync(Settings);
            DialogResult = true; Close();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not save profile", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void UpdatePreview()
    {
        AvatarImage.Source = null; AvatarImage.Visibility = Visibility.Collapsed;
        if (!string.IsNullOrWhiteSpace(_selectedImagePath) && File.Exists(_selectedImagePath))
        {
            try { AvatarImage.Source = LoadImage(_selectedImagePath); AvatarImage.Visibility = Visibility.Visible; } catch { }
        }
        RemovePictureButton.IsEnabled = AvatarImage.Visibility == Visibility.Visible;
        UpdateInitial();
    }

    private void UpdateInitial()
    {
        var name = UsernameBox?.Text?.Trim();
        InitialsText.Text = string.IsNullOrWhiteSpace(name) ? "P" : name[..1].ToUpperInvariant();
    }

    internal static BitmapImage LoadImage(string path)
    {
        var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.UriSource = new Uri(path, UriKind.Absolute); image.EndInit(); image.Freeze(); return image;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
}

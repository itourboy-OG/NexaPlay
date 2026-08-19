using NexaPlay.Services;
using System.Diagnostics;
using System.Windows;

namespace NexaPlay.Owner;

public partial class SteamGridDbKeyWindow : Window
{
    private readonly AppSettings _settings;
    private readonly SettingsService _settingsService;

    public SteamGridDbKeyWindow(AppSettings settings, SettingsService settingsService)
    {
        InitializeComponent();
        _settings = settings;
        _settingsService = settingsService;
        var existing = _settingsService.GetSteamGridDbKey(_settings);
        if (existing.Length > 0)
        {
            KeyBox.Password = existing;
            KeyStatusText.Text = "A SteamGridDB key is already encrypted on this PC.";
        }
        else KeyStatusText.Text = "No SteamGridDB key is saved yet.";
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        var key = KeyBox.Password.Trim();
        if (key.Length < 20 || key.Any(char.IsWhiteSpace))
        {
            MessageBox.Show("Paste the complete SteamGridDB API key. It should be at least 20 characters with no spaces.", "Invalid SteamGridDB key", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _settingsService.SetSteamGridDbKey(_settings, key);
        await _settingsService.SaveAsync(_settings);
        DialogResult = true;
        Close();
    }

    private async void Remove_Click(object sender, RoutedEventArgs e)
    {
        _settingsService.SetSteamGridDbKey(_settings, "");
        await _settingsService.SaveAsync(_settings);
        DialogResult = true;
        Close();
    }

    private void OpenApiPage_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("https://www.steamgriddb.com/profile/preferences/api") { UseShellExecute = true });

    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
}

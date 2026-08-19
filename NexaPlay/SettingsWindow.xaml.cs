using Microsoft.Win32;
using NexaPlay.Services;
using System.Diagnostics;
using System.Windows;

namespace NexaPlay;

public partial class SettingsWindow : Window
{
    public AppSettings Settings { get; private set; }
    private readonly SettingsService _settingsService;

    public SettingsWindow(AppSettings settings, SettingsService settingsService, CatalogService catalogService)
    {
        InitializeComponent(); _settingsService = settingsService;
        Settings = new AppSettings
        {
            LibraryFolder = settings.LibraryFolder,
            RemoteCatalogUrl = settings.RemoteCatalogUrl,
            ProtectedSteamGridDbKey = settings.ProtectedSteamGridDbKey,
            AutoSyncCatalog = settings.AutoSyncCatalog,
            KeepArchives = settings.KeepArchives,
            ProfileCpu = settings.ProfileCpu,
            ProfileGpu = settings.ProfileGpu,
            ProfileRamGb = settings.ProfileRamGb,
            ProfileOs = settings.ProfileOs
        };
        LibraryBox.Text = Settings.LibraryFolder; CatalogUrlBox.Text = Settings.RemoteCatalogUrl;
        AutoSyncBox.IsChecked = Settings.AutoSyncCatalog; KeepArchivesBox.IsChecked = Settings.KeepArchives;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose the NexaPlay game library", InitialDirectory = Directory.Exists(LibraryBox.Text) ? LibraryBox.Text : null };
        if (dialog.ShowDialog(this) == true) LibraryBox.Text = dialog.FolderName;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        var folder = LibraryBox.Text.Trim();
        if (folder.Length == 0) { MessageBox.Show("Choose a game library folder.", "NexaPlay settings"); return; }
        var url = CatalogUrlBox.Text.Trim();
        if (url.Length > 0 && (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps))
        { MessageBox.Show("Remote catalog URLs must be trusted HTTPS links.", "NexaPlay settings"); return; }
        try { Directory.CreateDirectory(folder); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Cannot use library folder", MessageBoxButton.OK, MessageBoxImage.Error); return; }
        Settings.LibraryFolder = folder; Settings.RemoteCatalogUrl = url; Settings.AutoSyncCatalog = AutoSyncBox.IsChecked == true; Settings.KeepArchives = KeepArchivesBox.IsChecked == true;
        await _settingsService.SaveAsync(Settings); DialogResult = true; Close();
    }

    private void OpenData_Click(object sender, RoutedEventArgs e) { AppPaths.EnsureCreated(); Process.Start(new ProcessStartInfo(AppPaths.DataRoot) { UseShellExecute = true }); }
    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
}

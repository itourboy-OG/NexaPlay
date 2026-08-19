using NexaPlay.Services;
using System.Diagnostics;
using System.Globalization;
using System.Windows;

namespace NexaPlay;

public partial class HardwareProfileWindow : Window
{
    private readonly AppSettings _settings;
    private readonly SettingsService _settingsService;
    private readonly GameEntry _game;

    public HardwareProfileWindow(GameEntry game, AppSettings settings, SettingsService settingsService)
    {
        InitializeComponent(); _game = game; _settings = settings; _settingsService = settingsService;
        CpuBox.Text = string.IsNullOrWhiteSpace(settings.ProfileCpu) ? SystemRequirementsService.DetectCpu() : settings.ProfileCpu;
        GpuBox.Text = string.IsNullOrWhiteSpace(settings.ProfileGpu) || SystemRequirementsService.IsVirtualGpu(settings.ProfileGpu)
            ? SystemRequirementsService.DetectGpu()
            : settings.ProfileGpu;
        RamBox.Text = (settings.ProfileRamGb ?? SystemRequirementsService.DetectRamGb()).ToString("0.#", CultureInfo.InvariantCulture);
        OsBox.Text = string.IsNullOrWhiteSpace(settings.ProfileOs) ? SystemRequirementsService.DetectOs() : settings.ProfileOs;
        MinimumText.Text = string.IsNullOrWhiteSpace(game.MinimumRequirementsDisplay) ? "No minimum requirements were published." : game.MinimumRequirementsDisplay;
        RecommendedText.Text = string.IsNullOrWhiteSpace(game.RecommendedRequirementsDisplay) ? "No recommended requirements were published." : game.RecommendedRequirementsDisplay;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(RamBox.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var ram) || ram <= 0) { MessageBox.Show("Enter the installed RAM in GB.", "PC profile"); return; }
        _settings.ProfileCpu = CpuBox.Text.Trim(); _settings.ProfileGpu = GpuBox.Text.Trim(); _settings.ProfileRamGb = ram; _settings.ProfileOs = OsBox.Text.Trim();
        await _settingsService.SaveAsync(_settings); DialogResult = true; Close();
    }

    private void OpenWebsite_Click(object sender, RoutedEventArgs e) => Process.Start(new ProcessStartInfo(SystemRequirementsService.GetTechnicalCityGameUrl(_game.Title)) { UseShellExecute = true });
    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
}

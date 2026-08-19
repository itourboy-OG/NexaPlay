using NexaPlay.Services;
using System.Net.Http;
using System.Windows;

namespace NexaPlay.Owner;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        try
        {
            var ownerSettingsService = new OwnerSettingsService();
            var ownerSettings = await ownerSettingsService.LoadAsync();
            var adminKey = ownerSettingsService.GetAdminKey(ownerSettings);
            var connected = !string.IsNullOrWhiteSpace(ownerSettings.CommunityUrl) && !string.IsNullOrWhiteSpace(adminKey);

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("NexaPlay-Owner/1.5.0");
            var api = new OwnerApiClient(http);
            var catalogService = new CatalogService(http);
            var settingsService = new SettingsService();
            var appSettings = await settingsService.LoadAsync();
            NexaPlay.CatalogDocument catalog;
            if (connected)
            {
                try { catalog = await api.GetCatalogAsync(ownerSettings.CommunityUrl); await catalogService.SaveAsync(catalog); }
                catch { catalog = await catalogService.LoadAsync(); connected = false; }
            }
            else
            {
                try { catalog = await catalogService.SyncAsync(GitHubCatalogPublisher.CatalogUrl); }
                catch { catalog = await catalogService.LoadAsync(); }
            }
            var github = new GitHubCatalogPublisher();
            var studio = new CreatorWindow(catalog, catalogService, appSettings, settingsService, http);
            studio.SetGitHubMode(GitHubCatalogPublisher.Repository);
            studio.ConfigurePublishingAction = () =>
            {
                var setup = new OwnerSetupWindow(ownerSettings.CommunityUrl) { Owner = studio };
                if (setup.ShowDialog() != true) return;
                ownerSettings.CommunityUrl = setup.CommunityUrl;
                ownerSettingsService.SetAdminKey(ownerSettings, setup.AdminKey);
                ownerSettingsService.SaveAsync(ownerSettings).GetAwaiter().GetResult();
                adminKey = setup.AdminKey;
                connected = true;
                studio.SetOwnerMode(true, ownerSettings.CommunityUrl);
            };
            MainWindow = studio;
            var changed = studio.ShowDialog() == true;
            if (changed)
            {
                try
                {
                    await github.EnsureReadyAsync();
                    await github.PublishAsync(studio.Catalog);
                    if (connected) await api.PublishAsync(ownerSettings.CommunityUrl, adminKey, studio.Catalog);
                    MessageBox.Show("The SauceBoyz catalog was published to GitHub. Friends will receive the changes automatically on their next NexaPlay sync.", "Catalog published", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Your catalog is safely saved on this PC, but GitHub publishing did not finish.\n\n{ex.Message}", "Saved locally — GitHub needs attention", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "NexaPlay Owner Studio", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { Shutdown(); }
    }
}

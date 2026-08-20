using NexaPlay.Services;
using System.IO;
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
            http.DefaultRequestHeaders.UserAgent.ParseAdd("NexaPlay-Owner/1.8.0");
            var api = new OwnerApiClient(http);
            var playerCatalogService = new CatalogService(http);
            var catalogService = new CatalogService(http, AppPaths.OwnerCatalogFile);
            var settingsService = new SettingsService();
            var appSettings = await settingsService.LoadAsync();
            NexaPlay.CatalogDocument catalog;
            if (File.Exists(AppPaths.OwnerCatalogFile))
            {
                catalog = await catalogService.LoadAsync();
            }
            else
            {
                try { catalog = await playerCatalogService.LoadAsync(); await catalogService.SaveAsync(catalog); }
                catch { try { catalog = await catalogService.SyncAsync(GitHubCatalogPublisher.CatalogUrl); } catch { catalog = await catalogService.LoadAsync(); } }
            }
            var github = new GitHubCatalogPublisher();
            var studio = new CreatorWindow(catalog, catalogService, appSettings, settingsService, http);
            studio.SetGitHubMode(GitHubCatalogPublisher.Repository);
            studio.SetArtworkKeyStatus(settingsService.GetSteamGridDbKey(appSettings).Length > 0);
            studio.ConfigureArtworkKeyAction = () =>
            {
                var keyWindow = new SteamGridDbKeyWindow(appSettings, settingsService) { Owner = studio };
                if (keyWindow.ShowDialog() == true) studio.SetArtworkKeyStatus(settingsService.GetSteamGridDbKey(appSettings).Length > 0);
            };
            studio.PublishAction = async publishedCatalog =>
            {
                await github.EnsureReadyAsync();
                await github.PublishAsync(publishedCatalog);
                if (connected) await api.PublishAsync(ownerSettings.CommunityUrl, adminKey, publishedCatalog);
            };
            MainWindow = studio;
            studio.ShowDialog();
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "NexaPlay Owner Studio", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { Shutdown(); }
    }
}

namespace NexaPlay.Services;

public static class AppPaths
{
    public static string DataRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexaPlay");
    public static string SettingsFile => Path.Combine(DataRoot, "settings.json");
    public static string CatalogFile => Path.Combine(DataRoot, "catalog.json");
    public static string OwnerCatalogFile => Path.Combine(DataRoot, "owner-catalog.json");
    public static string UserStateFile => Path.Combine(DataRoot, "library-state.json");
    public static string CacheRoot => Path.Combine(DataRoot, "cache");
    public static string UpdatesRoot => Path.Combine(DataRoot, "updates");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(DataRoot);
        Directory.CreateDirectory(CacheRoot);
        Directory.CreateDirectory(UpdatesRoot);
    }
}

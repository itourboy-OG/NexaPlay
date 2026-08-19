using System.Security.Cryptography;
using System.Text;

namespace NexaPlay.Services;

public sealed class SettingsService
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("NexaPlay.SteamGridDB.v1");

    public async Task<AppSettings> LoadAsync()
    {
        AppPaths.EnsureCreated();
        return await JsonFile.ReadAsync<AppSettings>(AppPaths.SettingsFile) ?? new AppSettings();
    }

    public Task SaveAsync(AppSettings settings) => JsonFile.WriteAtomicAsync(AppPaths.SettingsFile, settings);

    public string GetSteamGridDbKey(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ProtectedSteamGridDbKey)) return "";
        try
        {
            var encrypted = Convert.FromBase64String(settings.ProtectedSteamGridDbKey);
            var clear = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(clear);
        }
        catch { return ""; }
    }

    public void SetSteamGridDbKey(AppSettings settings, string key)
    {
        if (string.IsNullOrWhiteSpace(key)) { settings.ProtectedSteamGridDbKey = ""; return; }
        var clear = Encoding.UTF8.GetBytes(key.Trim());
        settings.ProtectedSteamGridDbKey = Convert.ToBase64String(
            ProtectedData.Protect(clear, Entropy, DataProtectionScope.CurrentUser));
    }
}

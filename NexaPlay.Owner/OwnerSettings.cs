using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.IO;

namespace NexaPlay.Owner;

public sealed class OwnerSettings
{
    public string CommunityUrl { get; set; } = "";
    public string ProtectedAdminKey { get; set; } = "";
}

public sealed class OwnerSettingsService
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("NexaPlay.Owner.Admin.v1");
    private static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexaPlay Owner");
    private static readonly string FilePath = Path.Combine(Folder, "owner-settings.json");

    public async Task<OwnerSettings> LoadAsync()
    {
        try { return JsonSerializer.Deserialize<OwnerSettings>(await File.ReadAllTextAsync(FilePath)) ?? new OwnerSettings(); }
        catch { return new OwnerSettings(); }
    }
    public async Task SaveAsync(OwnerSettings settings)
    {
        Directory.CreateDirectory(Folder);
        var temp = FilePath + ".tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, FilePath, true);
    }
    public string GetAdminKey(OwnerSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ProtectedAdminKey)) return "";
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(settings.ProtectedAdminKey), Entropy, DataProtectionScope.CurrentUser)); }
        catch { return ""; }
    }
    public void SetAdminKey(OwnerSettings settings, string value)
    {
        settings.ProtectedAdminKey = string.IsNullOrWhiteSpace(value) ? "" : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value.Trim()), Entropy, DataProtectionScope.CurrentUser));
    }
}

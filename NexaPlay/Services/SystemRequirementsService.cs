using Microsoft.Win32;
using System.Management;
using System.Runtime.InteropServices;

namespace NexaPlay.Services;

public static class SystemRequirementsService
{
    public static string Check(GameEntry game, string libraryFolder, AppSettings settings)
    {
        var checks = new List<string>(); var passed = true;
        var ramGb = settings.ProfileRamGb ?? DetectRamGb();
        if (game.MinimumRamGb is > 0) { var ok = ramGb >= game.MinimumRamGb.Value; passed &= ok; checks.Add($"{(ok ? "✓" : "✕")} RAM: {ramGb:0.#} GB / {game.MinimumRamGb:0.#} GB minimum"); }
        else checks.Add($"• RAM: {ramGb:0.#} GB (Steam did not publish a numeric minimum)");

        var profileOs = string.IsNullOrWhiteSpace(settings.ProfileOs) ? DetectOs() : settings.ProfileOs;
        var needsWindows11 = game.MinimumRequirements.Contains("Windows 11", StringComparison.OrdinalIgnoreCase);
        var needsWindows10 = game.MinimumRequirements.Contains("Windows 10", StringComparison.OrdinalIgnoreCase);
        var osOk = (!needsWindows11 || profileOs.Contains("Windows 11", StringComparison.OrdinalIgnoreCase)) && (!needsWindows10 || profileOs.Contains("Windows 10", StringComparison.OrdinalIgnoreCase) || profileOs.Contains("Windows 11", StringComparison.OrdinalIgnoreCase));
        passed &= osOk; checks.Add($"{(osOk ? "✓" : "✕")} Windows: {profileOs}");
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(libraryFolder)); var freeGb = root is null ? 0 : new DriveInfo(root).AvailableFreeSpace / 1024d / 1024d / 1024d;
            if (game.RequiredStorageGb is > 0) { var ok = freeGb >= game.RequiredStorageGb.Value; passed &= ok; checks.Add($"{(ok ? "✓" : "✕")} Storage: {freeGb:0.#} GB free / {game.RequiredStorageGb:0.#} GB required"); }
            else checks.Add($"• Storage: {freeGb:0.#} GB free (no numeric minimum published)");
        }
        catch { checks.Add("• Storage: could not read the library drive"); }
        checks.Add($"• CPU: {(string.IsNullOrWhiteSpace(settings.ProfileCpu) ? "Not entered" : settings.ProfileCpu)}");
        checks.Add($"• GPU: {(string.IsNullOrWhiteSpace(settings.ProfileGpu) ? "Not entered" : settings.ProfileGpu)}");
        var headline = passed ? "This PC passes the automatic RAM, Windows, and storage checks." : "This PC does not pass every automatic check.";
        return headline + Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, checks) + Environment.NewLine + Environment.NewLine + "Compare the saved CPU/GPU with the requirements shown on the game page, or use Can You RUN It for a maintained hardware ranking.";
    }

    public static string DetectCpu() { try { return Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0")?.GetValue("ProcessorNameString")?.ToString()?.Trim() ?? ""; } catch { return ""; } }
    public static string DetectGpu()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, PNPDeviceID, Status, VideoProcessor FROM Win32_VideoController");
            using var results = searcher.Get();
            return results.Cast<ManagementObject>()
                .Select(item => new VideoAdapter(
                    item["Name"]?.ToString()?.Trim() ?? "",
                    item["PNPDeviceID"]?.ToString()?.Trim() ?? "",
                    item["Status"]?.ToString()?.Trim() ?? "",
                    item["VideoProcessor"]?.ToString()?.Trim() ?? ""))
                .Where(adapter => adapter.Name.Length > 0 && !IsVirtualGpu(adapter.Name, adapter.PnpDeviceId))
                .OrderByDescending(ScoreAdapter)
                .Select(adapter => adapter.Name)
                .FirstOrDefault() ?? "";
        }
        catch { return ""; }
    }

    public static bool IsVirtualGpu(string name) => IsVirtualGpu(name, "");

    private static bool IsVirtualGpu(string name, string pnpDeviceId)
    {
        string[] virtualMarkers = ["Virtual", "Remote", "Indirect", "Basic Display", "Parsec", "spacedesk", "SudoMaker"];
        return virtualMarkers.Any(marker => name.Contains(marker, StringComparison.OrdinalIgnoreCase))
            || pnpDeviceId.StartsWith(@"ROOT\", StringComparison.OrdinalIgnoreCase)
            || pnpDeviceId.StartsWith(@"SWD\", StringComparison.OrdinalIgnoreCase);
    }

    private static int ScoreAdapter(VideoAdapter adapter)
    {
        var score = 0;
        if (adapter.PnpDeviceId.StartsWith(@"PCI\", StringComparison.OrdinalIgnoreCase)) score += 100;
        if (adapter.Status.Equals("OK", StringComparison.OrdinalIgnoreCase)) score += 30;
        if (adapter.VideoProcessor.Length > 0) score += 10;
        if (adapter.Name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) || adapter.Name.Contains("GeForce", StringComparison.OrdinalIgnoreCase)) score += 25;
        if (adapter.Name.Contains("AMD", StringComparison.OrdinalIgnoreCase) || adapter.Name.Contains("Radeon", StringComparison.OrdinalIgnoreCase)) score += 25;
        if (adapter.Name.Contains("Intel Arc", StringComparison.OrdinalIgnoreCase)) score += 25;
        else if (adapter.Name.Contains("Intel", StringComparison.OrdinalIgnoreCase)) score += 10;
        if (adapter.Name.Contains("RTX", StringComparison.OrdinalIgnoreCase) || adapter.Name.Contains("GTX", StringComparison.OrdinalIgnoreCase) || adapter.Name.Contains(" RX ", StringComparison.OrdinalIgnoreCase)) score += 15;
        return score;
    }

    private sealed record VideoAdapter(string Name, string PnpDeviceId, string Status, string VideoProcessor);
    public static string DetectOs() => Environment.OSVersion.Version.Build >= 22000 ? "Windows 11 (64-bit)" : "Windows 10 (64-bit)";
    public static double DetectRamGb() { var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() }; return GlobalMemoryStatusEx(ref memory) ? memory.TotalPhysical / 1024d / 1024d / 1024d : 0; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatus { public uint Length; public uint MemoryLoad; public ulong TotalPhysical; public ulong AvailablePhysical; public ulong TotalPageFile; public ulong AvailablePageFile; public ulong TotalVirtual; public ulong AvailableVirtual; public ulong AvailableExtendedVirtual; }
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool GlobalMemoryStatusEx(ref MemoryStatus buffer);
}

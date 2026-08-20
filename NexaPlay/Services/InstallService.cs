using SharpCompress.Common;
using SharpCompress.Readers;
using System.Diagnostics;
using RecycleFileSystem = Microsoft.VisualBasic.FileIO.FileSystem;

namespace NexaPlay.Services;

public sealed class InstallService
{
    private const string ManifestFileName = ".nexaplay.json";
    private const string RecoveryFileName = ".nexaplay-installing.json";

    public async Task<string> ExtractAsync(GameEntry game, string archivePath, string libraryFolder, IProgress<DownloadProgress> progress, CancellationToken cancellationToken)
    {
        var folderName = string.IsNullOrWhiteSpace(game.InstallFolderName) ? DownloadService.SafeName(game.Title) : DownloadService.SafeName(game.InstallFolderName);
        var installPath = Path.GetFullPath(Path.Combine(libraryFolder, folderName));
        var libraryRoot = Path.GetFullPath(libraryFolder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!installPath.StartsWith(libraryRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The install folder is outside the NexaPlay library.");
        Directory.CreateDirectory(installPath);

        var recoveryPath = Path.Combine(installPath, RecoveryFileName);
        await JsonFile.WriteAtomicAsync(recoveryPath, new
        {
            game.Id,
            game.Title,
            game.Version,
            Archive = archivePath,
            StartedUtc = DateTime.UtcNow
        }, cancellationToken);

        await ExtractArchiveAsync(archivePath, installPath, game.ArchivePassword, progress, cancellationToken);

        var executable = ResolveLauncher(game, installPath);
        var manifest = new InstallManifest(game.Id, game.Title, game.Version, Path.GetRelativePath(installPath, executable), DateTime.UtcNow, DateTime.UtcNow);
        await JsonFile.WriteAtomicAsync(Path.Combine(installPath, ManifestFileName), manifest, cancellationToken);
        File.Delete(recoveryPath);
        return installPath;
    }

    public async Task ApplyPackageAsync(GameEntry game, DownloadPackageKind kind, string archivePath, IProgress<DownloadProgress> progress, CancellationToken cancellationToken)
    {
        if (kind == DownloadPackageKind.Game) throw new ArgumentException("Use ExtractAsync for the base game package.", nameof(kind));
        if (!game.IsInstalled || !Directory.Exists(game.InstallPath)) throw new InvalidOperationException("Install the base game before applying an update or online fix.");
        await ApplyPackageToFolderAsync(game, kind, archivePath, game.InstallPath, progress, cancellationToken, requireManifest: true);
    }

    public async Task ApplyPackageToFolderAsync(GameEntry game, DownloadPackageKind kind, string archivePath, string targetFolder, IProgress<DownloadProgress> progress, CancellationToken cancellationToken, bool requireManifest = false)
    {
        if (kind == DownloadPackageKind.Game) throw new ArgumentException("Use ExtractAsync for the base game package.", nameof(kind));
        var installPath = Path.GetFullPath(targetFolder);
        if (!Directory.Exists(installPath)) throw new DirectoryNotFoundException("The selected game folder no longer exists.");
        var manifestPath = Path.Combine(installPath, ManifestFileName);
        var current = await JsonFile.ReadAsync<InstallManifest>(manifestPath, cancellationToken);
        if (requireManifest && current is null) throw new InvalidDataException("The NexaPlay install manifest is missing or damaged.");
        var package = DownloadService.GetPackageInfo(game, kind);
        await ExtractArchiveAsync(archivePath, installPath, package.Password, progress, cancellationToken);

        var version = kind == DownloadPackageKind.Update ? game.Version : current?.Version ?? "External install";
        if (current is not null)
            await JsonFile.WriteAtomicAsync(manifestPath, current with { Version = version, UpdatedUtc = DateTime.UtcNow }, cancellationToken);

        var receiptFolder = Path.GetDirectoryName(archivePath)!;
        var receiptName = kind == DownloadPackageKind.Update ? "last-update.json" : "last-online-fix.json";
        await JsonFile.WriteAtomicAsync(Path.Combine(receiptFolder, receiptName), new
        {
            game.Id,
            game.Title,
            Kind = kind.ToString(),
            Version = version,
            TargetFolder = installPath,
            Archive = Path.GetFileName(archivePath),
            AppliedUtc = DateTime.UtcNow
        }, cancellationToken);
    }

    public static void RefreshInstallState(GameEntry game, string libraryFolder)
    {
        var folderName = string.IsNullOrWhiteSpace(game.InstallFolderName) ? DownloadService.SafeName(game.Title) : DownloadService.SafeName(game.InstallFolderName);
        var installPath = Path.Combine(libraryFolder, folderName);
        game.InstallPath = installPath;
        var manifestPath = Path.Combine(installPath, ManifestFileName);
        game.IsInstalled = File.Exists(manifestPath);
        game.HasIncompleteInstall = File.Exists(Path.Combine(installPath, RecoveryFileName));
        game.InstalledVersion = "";
        if (game.IsInstalled)
        {
            try { game.InstalledVersion = JsonFile.Read<InstallManifest>(manifestPath)?.Version ?? ""; }
            catch { game.InstalledVersion = ""; }
        }
        game.NotifyRuntimeChanged();
    }

    public static async Task LaunchAsync(GameEntry game)
    {
        if (!game.IsInstalled) throw new InvalidOperationException("Install the game first.");
        var manifest = await JsonFile.ReadAsync<InstallManifest>(Path.Combine(game.InstallPath, ManifestFileName));
        var launcher = manifest is null ? ResolveLauncher(game, game.InstallPath) : Path.Combine(game.InstallPath, manifest.ExecutablePath);
        if (!File.Exists(launcher)) launcher = ResolveLauncher(game, game.InstallPath);
        Process.Start(new ProcessStartInfo(launcher) { WorkingDirectory = Path.GetDirectoryName(launcher)!, UseShellExecute = true });
    }

    public static async Task DeleteInstallAsync(GameEntry game, string libraryFolder, bool moveToRecycleBin = true, CancellationToken cancellationToken = default)
    {
        var libraryRoot = Path.GetFullPath(libraryFolder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var installPath = Path.GetFullPath(game.InstallPath);
        if (installPath.Length <= libraryRoot.TrimEnd(Path.DirectorySeparatorChar).Length ||
            !installPath.StartsWith(libraryRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("NexaPlay will only uninstall a managed game folder inside its library.");
        if (!Directory.Exists(installPath)) return;

        var manifestPath = Path.Combine(installPath, ManifestFileName);
        var manifest = await JsonFile.ReadAsync<InstallManifest>(manifestPath, cancellationToken);
        if (manifest is null || !string.Equals(manifest.GameId, game.Id, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The install manifest does not match this game, so the folder was not deleted.");

        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (moveToRecycleBin)
                RecycleFileSystem.DeleteDirectory(
                    installPath,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin,
                    Microsoft.VisualBasic.FileIO.UICancelOption.ThrowException);
            else
                Directory.Delete(installPath, recursive: true);
        }, cancellationToken);
    }

    private static async Task ExtractArchiveAsync(string archivePath, string installPath, string password, IProgress<DownloadProgress> progress, CancellationToken cancellationToken)
    {
        await Task.Run(() =>
        {
            var options = new ReaderOptions { Password = string.IsNullOrWhiteSpace(password) ? null : password };
            using var reader = ReaderFactory.OpenReader(archivePath, options);
            while (reader.MoveToNextEntry())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (reader.Entry.IsDirectory) continue;
                var entryName = reader.Entry.Key ?? "";
                var target = Path.GetFullPath(Path.Combine(installPath, entryName.Replace('/', Path.DirectorySeparatorChar)));
                var safeRoot = Path.GetFullPath(installPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!target.StartsWith(safeRoot, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Unsafe archive path blocked: {entryName}");

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using var input = reader.OpenEntryStream();
                using var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024);
                var buffer = new byte[1024 * 1024];
                var entrySize = reader.Entry.Size;
                long extracted = 0;
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    output.Write(buffer, 0, read);
                    extracted += read;
                    var percent = entrySize > 0 ? Math.Min(100, extracted * 100d / entrySize) : 0;
                    var sizeText = entrySize > 0
                        ? $"{FormatBytes(extracted)} / {FormatBytes(entrySize)}"
                        : FormatBytes(extracted);
                    progress.Report(new DownloadProgress(percent, extracted, entrySize > 0 ? entrySize : null, $"Extracting {Path.GetFileName(entryName)} — {sizeText}"));
                }
            }
        }, cancellationToken);
    }

    private static string FormatBytes(long value)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = value;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return $"{size:0.0} {units[unit]}";
    }

    private static string ResolveLauncher(GameEntry game, string installPath)
    {
        var runMe = Directory.EnumerateFiles(installPath, "*.bat", SearchOption.AllDirectories)
            .Where(path => Path.GetFileName(path).Equals("Run Me!.bat", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path.Count(ch => ch == Path.DirectorySeparatorChar))
            .FirstOrDefault();
        if (runMe is not null) return runMe;
        if (!string.IsNullOrWhiteSpace(game.ExecutablePath))
        {
            var explicitPath = Path.GetFullPath(Path.Combine(installPath, game.ExecutablePath.Replace('/', Path.DirectorySeparatorChar)));
            var safeRoot = Path.GetFullPath(installPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!explicitPath.StartsWith(safeRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Executable path must stay inside the game folder.");
            if (!File.Exists(explicitPath)) throw new FileNotFoundException("The configured game executable was not found after extraction.", explicitPath);
            return explicitPath;
        }
        var candidates = Directory.EnumerateFiles(installPath, "*.exe", SearchOption.AllDirectories)
            .Where(path => !Path.GetFileName(path).StartsWith("unins", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path.Count(ch => ch == Path.DirectorySeparatorChar))
            .ThenByDescending(path => new FileInfo(path).Length)
            .ToList();
        if (candidates.Count == 0) throw new FileNotFoundException("No Run Me!.bat or game executable was found. Add Run Me!.bat to the archive or set a relative executable path in Creator Studio.");
        return candidates[0];
    }
}

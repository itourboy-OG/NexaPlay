namespace NexaPlay.Services;

public static class ProfileImageService
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp"
    };

    public static async Task<string> ImportAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("The selected profile picture could not be found.", sourcePath);
        var extension = Path.GetExtension(sourcePath);
        if (!SupportedExtensions.Contains(extension)) throw new InvalidDataException("Choose a PNG, JPG, JPEG, or BMP image.");

        AppPaths.EnsureCreated();
        var destination = Path.Combine(AppPaths.ProfileRoot, $"avatar{extension.ToLowerInvariant()}");
        if (Path.GetFullPath(sourcePath).Equals(Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase)) return destination;

        var temporary = destination + ".tmp";
        await using (var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 256, true))
        await using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 256, true))
            await input.CopyToAsync(output, cancellationToken);
        File.Move(temporary, destination, true);
        return destination;
    }
}

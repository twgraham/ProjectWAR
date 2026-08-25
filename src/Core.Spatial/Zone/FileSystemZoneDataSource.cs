namespace Core.Spatial.Zone;

/// <summary>
/// Loads zone binary files (<c>*.bin</c>) from a filesystem directory.
/// </summary>
public sealed class FileSystemZoneDataSource : IZoneDataSource
{
    private readonly string _basePath;

    public FileSystemZoneDataSource(string basePath)
    {
        _basePath = basePath ?? throw new ArgumentNullException(nameof(basePath));
    }

    /// <inheritdoc />
    public IEnumerable<Stream> OpenAll()
    {
        foreach (var file in Directory.GetFiles(_basePath, "*.bin"))
            yield return File.OpenRead(file);
    }

    /// <inheritdoc />
    public Stream? Open(int zoneId)
    {
        var path = Path.Combine(_basePath, $"{zoneId}.bin");
        return File.Exists(path) ? File.OpenRead(path) : null;
    }
}

namespace Core.Spatial;

/// <summary>
/// Abstracts how zone binary data is obtained, decoupling zone loading from
/// the filesystem. Implementations supply <see cref="Stream"/> instances for the
/// OCC binary format parser.
/// </summary>
/// <remarks>
/// The caller is responsible for disposing every <see cref="Stream"/> returned by
/// <see cref="OpenAll"/> and <see cref="Open"/>.
/// </remarks>
public interface IZoneDataSource
{
    /// <summary>
    /// Opens all available zone data streams for bulk loading.
    /// Each returned stream must be disposed by the caller.
    /// </summary>
    IEnumerable<Stream> OpenAll();

    /// <summary>
    /// Opens a single zone data stream by zone ID.
    /// Returns <c>null</c> if the zone is not available.
    /// The caller must dispose the returned stream.
    /// </summary>
    Stream? Open(int zoneId);
}

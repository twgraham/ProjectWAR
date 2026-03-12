namespace WorldServerV2.Data;

/// <summary>
/// Loads a single domain of static game data from the database and returns
/// an immutable data bundle.
/// <para>
/// Each implementation is responsible for one domain (items, creatures, zones, etc.)
/// and performs any intra-domain cross-linking before returning the result.
/// </para>
/// </summary>
/// <typeparam name="TData">The domain data bundle type (e.g., <c>ItemData</c>).</typeparam>
public interface IDataProvider<out TData>
{
    /// <summary>
    /// Loads and returns the immutable domain data.
    /// </summary>
    TData Load();
}

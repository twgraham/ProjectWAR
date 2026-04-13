using Core.GameWorld.Spatial;
using ServiceScan.SourceGenerator;

namespace WorldServerV2.Extensions;

public class WorldTopologyBuilderExtensions
{
    
}

public static partial class RegionHandlerLoader
{
    [ScanForTypes(AssignableTo = typeof(IRegionEventHandler<>), Handler = nameof(ApplyRegister))]
    public static partial void RegisterHandlers(this WorldTopologyBuilder builder);

    private static void ApplyRegister<T, TEvent>(WorldTopologyBuilder builder)
        where T : class, IRegionEventHandler<TEvent>
    {
        builder.OnEvent<TEvent, T>();
    }
}
using Microsoft.Extensions.DependencyInjection;

namespace Core.Infrastructure.Network;

public interface IServerNetworkingBuilder
{
    IServerNetworkingBuilder WithPacketFramer<T>(ServiceLifetime lifetime = ServiceLifetime.Transient)
        where T : class, IPacketFramer;
    IServerNetworkingBuilder WithPacketFramer(IPacketFramer packetFramer);
    IServerNetworkingBuilder WithPacketSerializer<T>(ServiceLifetime lifetime = ServiceLifetime.Transient) where T : class, IPacketSerializer;
    IServerNetworkingBuilder WithPacketSerializer(IPacketSerializer packetSerializerFactory);
    IServerNetworkingBuilder WithPacketDispatcher<T>() where T : class, IPacketDispatcher;
    IServerNetworkingBuilder WithPacketDispatcher(IPacketDispatcher packetDispatcher);
    IServerNetworkingBuilder AddHandler<THandler>() where THandler : class;
}
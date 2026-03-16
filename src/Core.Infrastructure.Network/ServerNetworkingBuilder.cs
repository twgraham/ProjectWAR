using Microsoft.Extensions.DependencyInjection;

namespace Core.Infrastructure.Network;

internal class ServerNetworkingBuilder : IServerNetworkingBuilder
{
    private readonly IServiceCollection _services;

    internal ServerNetworkingBuilder(IServiceCollection services)
    {
        _services = services;
    }
    
    public IServerNetworkingBuilder WithPacketFramer<T>(ServiceLifetime lifetime = ServiceLifetime.Transient) where T : class, IPacketFramer
    {
        _services.Add(new ServiceDescriptor(typeof(IPacketFramer), typeof(T), lifetime));
        return this;
    }

    public IServerNetworkingBuilder WithPacketFramer(IPacketFramer packetFramer)
    {
        _services.AddSingleton(packetFramer);
        return this;
    }

    public IServerNetworkingBuilder WithPacketSerializer<T>(ServiceLifetime lifetime = ServiceLifetime.Transient)
        where T : class, IPacketSerializer
    {
        _services.Add(new ServiceDescriptor(typeof(IPacketSerializer), typeof(T), lifetime));
        return this;
    }

    public IServerNetworkingBuilder WithPacketSerializer(IPacketSerializer packetSerializerFactory)
    {
        _services.AddSingleton(packetSerializerFactory);
        return this;
    }

    public IServerNetworkingBuilder WithPacketDispatcher<T>() where T : class, IPacketDispatcher
    {
        _services.AddSingleton<IPacketDispatcher, T>();
        return this;
    }

    public IServerNetworkingBuilder WithPacketDispatcher(IPacketDispatcher packetDispatcher)
    {
        _services.AddSingleton(packetDispatcher);
        return this;
    }

    public IServerNetworkingBuilder AddHandler<THandler>() where THandler : class
    {
        _services.AddScoped<THandler>();
        return this;
    }
}

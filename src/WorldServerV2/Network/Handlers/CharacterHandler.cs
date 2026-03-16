using Core.Infrastructure.Network;
using Microsoft.Extensions.Logging;
using WorldServerV2.Network.Dtos;
using WorldServerV2.Services;
using WorldServerV2.World.Entities;
using IPacketHandler = Core.Infrastructure.Network.IPacketHandler;

namespace WorldServerV2.Network.Handlers;

/// <summary>
/// Handles character-related packets: character selection, world entry.
/// This is the modernized equivalent of the legacy CharacterHandlers class.
/// </summary>
public class CharacterHandler : IPacketHandler
{
    private readonly ILogger<CharacterHandler> _logger;

    

    
}

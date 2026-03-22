using WorldServerV2.Network;
using WorldServerV2.Network.Dtos;
using WorldServerV2.World.Entities;

namespace WorldServerV2.Services.PlayerInit;

/// <summary>
/// Sends <c>S_PLAYER_INITTED</c> — identity, position, realm, and career.
/// Requires access to <see cref="RealmInfo"/> for the realm/server name.
/// </summary>
public sealed class PlayerInittedInitStep : IPlayerInitStep
{
    private readonly RealmInfo _realmInfo;

    public PlayerInittedInitStep(RealmInfo realmInfo)
    {
        _realmInfo = realmInfo ?? throw new ArgumentNullException(nameof(realmInfo));
    }

    public void Execute(PlayerEntity player, GameSession session)
    {
        var character = player.Character;
        var charValue = character.Value;

        session.SendPlayerInitted(new PlayerInittedResponse
        {
            Oid = player.ObjectId,
            CharacterId = character.CharacterId,
            WorldZ = (ushort)charValue.WorldZ,
            WorldX = (uint)charValue.WorldX,
            WorldY = (uint)charValue.WorldY,
            WorldO = (ushort)charValue.WorldO,
            Realm = character.Realm,
            RegionId = (ushort)charValue.RegionId,
            Career = character.Career,
            RealmName = _realmInfo.Name,
        });
    }
}

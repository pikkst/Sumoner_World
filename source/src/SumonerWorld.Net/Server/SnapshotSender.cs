using System.Numerics;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.World;
using SumonerWorld.Net.Messages;
using SumonerWorld.Net.Transport;

namespace SumonerWorld.Net.Server;

/// <summary>
/// Builds and sends one player's snapshot (TDD §3.2): their own avatar exactly, then every creature and other player within the
/// interest radius, split into parts that fit one datagram. Priority ordering and delta compression come with M1-18.
/// </summary>
internal sealed class SnapshotSender(CreatureWorld world, ITransport transport, float interestRadius)
{
    private readonly byte[] _buffer = new byte[Protocol.MaxMessageSize];
    private readonly float _radiusSquared = interestRadius * interestRadius;
    private readonly ComponentStore<Position> _positions = world.World.Store<Position>();
    private readonly ComponentStore<Facing> _facings = world.World.Store<Facing>();
    private readonly ComponentStore<PlayerAvatar> _players = world.World.Store<PlayerAvatar>();
    private readonly ComponentStore<StableId> _ids = world.World.Store<StableId>();
    private readonly ComponentStore<CurrentEmote> _emotes = world.World.Store<CurrentEmote>();

    public int PartsSent { get; private set; }

    public void Send(ConnectionId connection, Entity avatar, uint ackedInput, ulong tick)
    {
        Vector3 own = _positions.Get(avatar).Value;
        var writer = new MessageWriter(_buffer);
        int countOffset = Snapshot.WriteHeader(ref writer, tick, ackedInput, own);
        int count = 0;

        ReadOnlySpan<Entity> entities = _positions.Entities;
        Span<Position> positions = _positions.Components;
        for (int i = 0; i < entities.Length; i++)
        {
            Entity entity = entities[i];
            if (entity == avatar || Vector3.DistanceSquared(own, positions[i].Value) > _radiusSquared)
            {
                continue;
            }

            if (count == Snapshot.MaxEntitiesPerPart || writer.Written + Snapshot.MaxEntityBytes > _buffer.Length)
            {
                Flush(connection, ref writer, countOffset, count);
                writer = new MessageWriter(_buffer);
                countOffset = Snapshot.WriteHeader(ref writer, tick, ackedInput, own);
                count = 0;
            }

            Snapshot.WriteEntity(ref writer, Describe(entity, positions[i].Value));
            count++;
        }

        Flush(connection, ref writer, countOffset, count);
    }

    private SnapshotEntity Describe(Entity entity, Vector3 position)
    {
        byte facing = Snapshot.EncodeFacing(_facings.Get(entity).Value);
        return _players.Has(entity)
            ? new SnapshotEntity(SnapshotEntityKind.Player, _players.Get(entity).AccountId, position, facing, 0)
            : new SnapshotEntity(SnapshotEntityKind.Creature, _ids.Get(entity).Value, position, facing, (byte)_emotes.Get(entity).Value);
    }

    private void Flush(ConnectionId connection, ref MessageWriter writer, int countOffset, int count)
    {
        Snapshot.SetEntityCount(_buffer, countOffset, count);
        transport.Send(connection, Channel.Unreliable, writer.WrittenSpan);
        PartsSent++;
    }
}

using System.Numerics;
using SumonerWorld.Core.Combat;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.World;
using SumonerWorld.Net.Messages;
using SumonerWorld.Net.Transport;

namespace SumonerWorld.Net.Server;

/// <summary>
/// Builds and sends one player's snapshot (TDD §3.2): their own avatar exactly, then every creature and other player within the
/// interest radius, split into parts that fit one datagram. Priority ordering and delta compression are deferred until a measurement needs them (M1-18a: full snapshots use under a third of the budget).
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
    private readonly ComponentStore<SpeciesRef> _species = world.World.Store<SpeciesRef>();
    private readonly ComponentStore<Core.Progression.CreatureGrowth> _growth = world.World.Store<Core.Progression.CreatureGrowth>();
    private readonly ComponentStore<Combatant> _combatants = world.World.Store<Combatant>();

    public int PartsSent { get; private set; }

    public void Send(ConnectionId connection, Entity avatar, uint ackedInput, ulong tick)
    {
        Vector3 own = _positions.Get(avatar).Value;
        SnapshotVitals vitals = Vitals(avatar, tick);
        var writer = new MessageWriter(_buffer);
        int countOffset = Snapshot.WriteHeader(ref writer, tick, ackedInput, own, vitals);
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
                countOffset = Snapshot.WriteHeader(ref writer, tick, ackedInput, own, vitals);
                count = 0;
            }

            Snapshot.WriteEntity(ref writer, Describe(entity, positions[i].Value, tick));
            count++;
        }

        Flush(connection, ref writer, countOffset, count);
    }

    private SnapshotEntity Describe(Entity entity, Vector3 position, ulong tick)
    {
        byte facing = Snapshot.EncodeFacing(_facings.Get(entity).Value);
        Combatant c = _combatants.Has(entity) ? _combatants.Get(entity) : default;
        byte health = _combatants.Has(entity) ? Snapshot.Fraction(c.Health, c.MaxHealth) : byte.MaxValue;
        (CombatAction action, int move) = CombatRules.ActionOf(world.World, world.Data, entity, tick);
        return _players.Has(entity)
            ? new SnapshotEntity(SnapshotEntityKind.Player, _players.Get(entity).AccountId, position, facing, 0, 0, health, (byte)action, (byte)(move + 1))
            : new SnapshotEntity(
                SnapshotEntityKind.Creature, _ids.Get(entity).Value, position, facing, (byte)_emotes.Get(entity).Value, (byte)_species.Get(entity).Index,
                health, (byte)action, (byte)(move + 1), (byte)_growth.Get(entity).Stage, (byte)_growth.Get(entity).Level);
    }

    private SnapshotVitals Vitals(Entity avatar, ulong tick)
    {
        Combatant c = _combatants.Get(avatar);
        CombatAction action = CombatRules.ActionOf(world.World, world.Data, avatar, tick).Action;
        Core.Progression.PlayerProgress p = world.World.GetComponent<Core.Progression.PlayerProgress>(avatar);
        int from = world.Growth.XpFor(p.Level);
        int to = world.Growth.XpFor(p.Level + 1);
        byte xp = p.Level >= Core.Progression.GrowthRules.Cap((Core.Progression.Rank)p.Rank) ? byte.MaxValue : Snapshot.Fraction(p.Xp - from, to - from);
        return new SnapshotVitals(
            Snapshot.Fraction(c.Health, c.MaxHealth), Snapshot.Fraction(c.Stamina, c.MaxStamina), Snapshot.Fraction(c.Mana, c.MaxMana), (byte)action,
            (byte)p.Level, (byte)p.Rank, (byte)p.Class, xp);
    }

    private void Flush(ConnectionId connection, ref MessageWriter writer, int countOffset, int count)
    {
        Snapshot.SetEntityCount(_buffer, countOffset, count);
        transport.Send(connection, Channel.Unreliable, writer.WrittenSpan);
        PartsSent++;
    }
}

using SumonerWorld.Core.Bonding;
using SumonerWorld.Core.Progression;
using SumonerWorld.Core.World;
using SumonerWorld.Net.Messages;
using SumonerWorld.Net.Transport;

namespace SumonerWorld.Net.Server;

/// <summary>
/// Sends each player their pacted creatures (<see cref="CompanionList"/>, M2-06) whenever the list changed since the last send to
/// them: a Pact, a summon, a dismissal, a creature leaving. A new or resumed connection gets the list again.
/// </summary>
internal sealed class CompanionListSender(CreatureWorld world, ITransport transport)
{
    private readonly CompanionListCodec _codec = new();
    private readonly byte[] _buffer = new byte[Protocol.MaxMessageSize];
    private readonly List<Companion> _current = [];
    private readonly Dictionary<ulong, Companion[]> _sent = [];

    /// <summary>Sends the account's list on <paramref name="connection"/> if it changed.</summary>
    public void Update(ConnectionId connection, ulong account)
    {
        Companions.Of(world, account, _current);
        if (_sent.TryGetValue(account, out Companion[]? sent) && sent.AsSpan().SequenceEqual(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_current)))
        {
            return;
        }

        _sent[account] = [.. _current];
        var entries = new CompanionEntry[Math.Min(_current.Count, CompanionList.MaxEntries)];
        for (int i = 0; i < entries.Length; i++)
        {
            Companion companion = _current[i];
            entries[i] = new CompanionEntry(
                companion.Id.Value,
                (byte)companion.Species,
                companion.Summoned,
                (byte)companion.Level,
                (byte)companion.Stage,
                (byte)companion.BondLevel,
                (byte)Math.Min(companion.Points, byte.MaxValue),
                companion.Learned,
                BranchAffinities(companion),
                (byte)companion.Combos);
        }

        var writer = new MessageWriter(_buffer);
        _codec.Write(ref writer, new CompanionList(entries));
        transport.Send(connection, Channel.ReliableOrdered, writer.WrittenSpan);
    }

    // How much it likes each branch of its tree now (its traits and the roles it did most).
    private Affinities BranchAffinities(Companion companion)
    {
        if (world.Data.SkillTreeOf(companion.Species) is not Core.Data.Definitions.SkillTreeDefinition tree
            || !world.TryFindCreature(companion.Id, out Core.Ecs.Entity creature))
        {
            return default;
        }

        Core.Cognition.Personality personality = world.Minds.GetPersonality(world.World.GetComponent<MindRef>(creature).Handle);
        CreatureGrowth growth = world.World.GetComponent<CreatureGrowth>(creature);
        Span<int> values = stackalloc int[Core.Data.Definitions.SkillTreeDefinition.MaxBranches];
        int count = Math.Min(tree.Branches.Count, values.Length);
        for (int b = 0; b < count; b++)
        {
            values[b] = SkillRules.Affinity(tree.Branches[b], personality, growth);
        }

        return Affinities.Of(values[..count]);
    }

    /// <summary>Forgets what was sent to the account, so the next <see cref="Update"/> sends the list again.</summary>
    public void Forget(ulong account) => _sent.Remove(account);
}

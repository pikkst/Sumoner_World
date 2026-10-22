using SumonerWorld.Core.Progression;
using SumonerWorld.Core.World;
using SumonerWorld.Net.Messages;
using SumonerWorld.Net.Transport;

namespace SumonerWorld.Net.Server;

/// <summary>
/// Sends each player the profiles of everyone in the world (<see cref="PlayerRoster"/>, M2-20: class, rank, level, experience) when
/// they changed since the last send to them.
/// </summary>
internal sealed class RosterSender(CreatureWorld world, ITransport transport)
{
    private readonly PlayerRosterCodec _codec = new();
    private readonly byte[] _buffer = new byte[Protocol.MaxMessageSize];
    private readonly Dictionary<ulong, RosterEntry[]> _sent = [];

    /// <summary>Sends the roster to the account on <paramref name="connection"/> if it changed.</summary>
    public void Update(ConnectionId connection, ulong account)
    {
        RosterEntry[] roster = Current();
        if (_sent.TryGetValue(account, out RosterEntry[]? sent) && sent.AsSpan().SequenceEqual(roster))
        {
            return;
        }

        _sent[account] = roster;
        var writer = new MessageWriter(_buffer);
        _codec.Write(ref writer, new PlayerRoster(roster));
        transport.Send(connection, Channel.ReliableOrdered, writer.WrittenSpan);
    }

    /// <summary>Forgets what was sent to the account, so the next <see cref="Update"/> sends again.</summary>
    public void Forget(ulong account) => _sent.Remove(account);

    private RosterEntry[] Current()
    {
        var entries = new List<RosterEntry>();
        foreach (Core.Ecs.Entity avatar in world.World.Store<PlayerAvatar>().Entities)
        {
            ulong account = world.World.GetComponent<PlayerAvatar>(avatar).AccountId;
            PlayerProgress progress = world.World.Has<PlayerProgress>(avatar) ? world.World.GetComponent<PlayerProgress>(avatar) : PlayerProgress.New;
            entries.Add(new RosterEntry(account, (byte)progress.Class, (byte)progress.Rank, (byte)progress.Level, progress.Xp));
        }

        entries.Sort((a, b) => a.Account.CompareTo(b.Account));
        return [.. entries.Take(PlayerRoster.MaxEntries)];
    }
}

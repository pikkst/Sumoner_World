using SumonerWorld.Core.Bonding;
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
            entries[i] = new CompanionEntry(companion.Id.Value, (byte)companion.Species, companion.Summoned, (byte)companion.Level, (byte)companion.Stage);
        }

        var writer = new MessageWriter(_buffer);
        _codec.Write(ref writer, new CompanionList(entries));
        transport.Send(connection, Channel.ReliableOrdered, writer.WrittenSpan);
    }

    /// <summary>Forgets what was sent to the account, so the next <see cref="Update"/> sends the list again.</summary>
    public void Forget(ulong account) => _sent.Remove(account);
}

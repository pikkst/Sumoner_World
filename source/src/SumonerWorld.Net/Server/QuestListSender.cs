using SumonerWorld.Core.Quests;
using SumonerWorld.Core.World;
using SumonerWorld.Net.Messages;
using SumonerWorld.Net.Transport;

namespace SumonerWorld.Net.Server;

/// <summary>Sends each player their quests (<see cref="QuestList"/>, M2-19) whenever they changed since the last send to them.</summary>
internal sealed class QuestListSender(CreatureWorld world, ITransport transport)
{
    private readonly QuestListCodec _codec = new();
    private readonly byte[] _buffer = new byte[Protocol.MaxMessageSize];
    private readonly Dictionary<ulong, ulong> _sent = [];

    /// <summary>Sends the account's quests on <paramref name="connection"/> if they changed.</summary>
    public void Update(ConnectionId connection, ulong account)
    {
        PlayerQuests? quests = world.Economy.Quests.Find(account);
        ulong fingerprint = Fingerprint(quests);
        if (_sent.TryGetValue(account, out ulong sent) && sent == fingerprint)
        {
            return;
        }

        _sent[account] = fingerprint;
        var entries = new List<QuestEntry>();
        foreach (QuestProgress progress in quests?.Active ?? [])
        {
            entries.Add(new QuestEntry(
                progress.Quest, (byte)progress.Status, (byte)progress.Step, progress.Counts[0], progress.Counts[1], progress.Counts[2], progress.Counts[3]));
        }

        foreach (int quest in (quests?.Done ?? []).Order())
        {
            entries.Add(new QuestEntry(quest, (byte)QuestStatus.Done, 0));
        }

        var writer = new MessageWriter(_buffer);
        _codec.Write(ref writer, new QuestList([.. entries.Take(QuestList.MaxEntries).OrderBy(entry => entry.Quest)]));
        transport.Send(connection, Channel.ReliableOrdered, writer.WrittenSpan);
    }

    /// <summary>Forgets what was sent to the account, so the next <see cref="Update"/> sends again.</summary>
    public void Forget(ulong account) => _sent.Remove(account);

    private static ulong Fingerprint(PlayerQuests? quests)
    {
        ulong hash = 14_695_981_039_346_656_037;
        if (quests is null)
        {
            return hash;
        }

        foreach (QuestProgress progress in quests.Active)
        {
            hash = Mix(Mix(Mix(hash, (ulong)progress.Quest), (ulong)progress.Step), (ulong)progress.Status);
            foreach (int count in progress.Counts)
            {
                hash = Mix(hash, (ulong)count);
            }
        }

        return Mix(hash, (ulong)quests.Done.Count);
    }

    private static ulong Mix(ulong hash, ulong value) => (hash ^ value) * 1_099_511_628_211;
}

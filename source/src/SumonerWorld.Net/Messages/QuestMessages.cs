namespace SumonerWorld.Net.Messages;

/// <summary>One of the receiver's quests on the wire (since protocol 20).</summary>
/// <param name="Quest">Quest index in the data both sides load.</param>
/// <param name="Status">A <c>QuestStatus</c>: 1 active, 2 ready, 3 done.</param>
/// <param name="Step">The current step.</param>
/// <param name="Count0">First objective's count.</param>
/// <param name="Count1">Second objective's count.</param>
/// <param name="Count2">Third objective's count.</param>
/// <param name="Count3">Fourth objective's count.</param>
public readonly record struct QuestEntry(int Quest, byte Status, byte Step, int Count0 = 0, int Count1 = 0, int Count2 = 0, int Count3 = 0)
{
    /// <summary>The count of objective <paramref name="index"/>.</summary>
    public int this[int index] => index switch
    {
        0 => Count0,
        1 => Count1,
        2 => Count2,
        _ => Count3,
    };
}

/// <summary>Server → client, reliable (since protocol 20): the receiver's taken and done quests, in quest order.</summary>
/// <param name="Entries">At most <see cref="MaxEntries"/>.</param>
public readonly record struct QuestList(IReadOnlyList<QuestEntry> Entries)
{
    /// <summary>Most quests sent.</summary>
    public const int MaxEntries = 250;
}

/// <summary>Codec for <see cref="QuestList"/>.</summary>
public sealed class QuestListCodec : IMessageCodec<QuestList>
{
    private const int MaxStatus = 3;

    /// <inheritdoc />
    public MessageType Type => MessageType.QuestList;

    /// <inheritdoc />
    public void Write(ref MessageWriter writer, in QuestList message)
    {
        ArgumentNullException.ThrowIfNull(message.Entries);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(message.Entries.Count, QuestList.MaxEntries);
        Protocol.WriteHeader(ref writer, Type);
        writer.WriteByte((byte)message.Entries.Count);
        foreach (QuestEntry entry in message.Entries)
        {
            writer.WriteVarUInt((ulong)entry.Quest);
            writer.WriteByte(entry.Status);
            writer.WriteByte(entry.Step);
            for (int i = 0; i < 4; i++)
            {
                writer.WriteVarUInt((ulong)Math.Max(0, entry[i]));
            }
        }
    }

    /// <inheritdoc />
    public QuestList Read(ref MessageReader reader)
    {
        int count = reader.ReadByte();
        if (count > QuestList.MaxEntries)
        {
            throw new MalformedMessageException($"Quest list of {count} (max {QuestList.MaxEntries}).");
        }

        var entries = new QuestEntry[count];
        Span<int> counts = stackalloc int[4];
        for (int i = 0; i < count; i++)
        {
            int quest = WornItems.Small(ref reader);
            byte status = reader.ReadByte();
            byte step = reader.ReadByte();
            if (status is 0 or > MaxStatus)
            {
                throw new MalformedMessageException($"Quest {quest} has status {status}.");
            }

            for (int c = 0; c < 4; c++)
            {
                counts[c] = WornItems.Small(ref reader);
            }

            entries[i] = new QuestEntry(quest, status, step, counts[0], counts[1], counts[2], counts[3]);
        }

        reader.EnsureEnd();
        return new QuestList(entries);
    }
}

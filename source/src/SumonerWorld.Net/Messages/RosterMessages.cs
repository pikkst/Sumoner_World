namespace SumonerWorld.Net.Messages;

/// <summary>One player's public profile on the wire (since protocol 21, M2-20).</summary>
/// <param name="Account">Account id (the id of their avatar in snapshots).</param>
/// <param name="Class">Summoner class.</param>
/// <param name="Rank">Summoner Rank.</param>
/// <param name="Level">Level.</param>
/// <param name="Xp">Total experience.</param>
public readonly record struct RosterEntry(ulong Account, byte Class, byte Rank, byte Level, int Xp);

/// <summary>Server → client, reliable (since protocol 21): every player in the world and their profile, in account order.</summary>
/// <param name="Entries">At most <see cref="MaxEntries"/>.</param>
public readonly record struct PlayerRoster(IReadOnlyList<RosterEntry> Entries)
{
    /// <summary>Most players sent.</summary>
    public const int MaxEntries = 64;
}

/// <summary>Codec for <see cref="PlayerRoster"/>.</summary>
public sealed class PlayerRosterCodec : IMessageCodec<PlayerRoster>
{
    /// <inheritdoc />
    public MessageType Type => MessageType.PlayerRoster;

    /// <inheritdoc />
    public void Write(ref MessageWriter writer, in PlayerRoster message)
    {
        ArgumentNullException.ThrowIfNull(message.Entries);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(message.Entries.Count, PlayerRoster.MaxEntries);
        Protocol.WriteHeader(ref writer, Type);
        writer.WriteByte((byte)message.Entries.Count);
        foreach (RosterEntry entry in message.Entries)
        {
            writer.WriteVarUInt(entry.Account);
            writer.WriteByte(entry.Class);
            writer.WriteByte(entry.Rank);
            writer.WriteByte(entry.Level);
            writer.WriteVarUInt((ulong)Math.Max(0, entry.Xp));
        }
    }

    /// <inheritdoc />
    public PlayerRoster Read(ref MessageReader reader)
    {
        int count = reader.ReadByte();
        if (count > PlayerRoster.MaxEntries)
        {
            throw new MalformedMessageException($"Roster of {count} (max {PlayerRoster.MaxEntries}).");
        }

        var entries = new RosterEntry[count];
        for (int i = 0; i < count; i++)
        {
            ulong account = reader.ReadVarUInt();
            byte playerClass = reader.ReadByte();
            byte rank = reader.ReadByte();
            byte level = reader.ReadByte();
            ulong xp = reader.ReadVarUInt();
            if (xp > int.MaxValue)
            {
                throw new MalformedMessageException($"Player {account} has {xp} XP.");
            }

            entries[i] = new RosterEntry(account, playerClass, rank, level, (int)xp);
        }

        reader.EnsureEnd();
        return new PlayerRoster(entries);
    }
}

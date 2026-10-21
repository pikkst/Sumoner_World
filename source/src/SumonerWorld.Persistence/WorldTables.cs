using System.Numerics;
using Microsoft.Data.Sqlite;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Progression;
using SumonerWorld.Core.World;
using SumonerWorld.Persistence.Records;

namespace SumonerWorld.Persistence;

/// <summary>A world read back from the store.</summary>
/// <param name="World">Creatures, reputation, clock, random state and id counter.</param>
/// <param name="Players">Every known player's last place.</param>
public sealed record SavedWorld(WorldSnapshot World, IReadOnlyList<PlayerSave> Players);

/// <summary>The schema v2 world tables (players, creatures, reputation) and the world meta values; a save replaces all of them.</summary>
internal sealed class WorldTables(WorldStore store)
{
    public const string SavedKey = "world_saved";
    public const string TickKey = "world_tick";
    public const string RandomKey = "random_state";
    public const string LastIdKey = "last_stable_id";

    private readonly MessagePackRecordCodec<CreatureRecord> _creatures = new(1);
    private readonly MessagePackRecordCodec<PlayerRecord> _players = new(1);

    public void Save(SqliteTransaction transaction, WorldSnapshot world, IReadOnlyList<PlayerSave> players)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(players);
        foreach (string table in new[] { "creatures", "players", "reputation" })
        {
            using SqliteCommand clear = store.Command($"DELETE FROM {table};", transaction);
            clear.ExecuteNonQuery();
        }

        using (SqliteCommand insert = store.Command("INSERT INTO creatures (stable_id, payload) VALUES ($id, $payload);", transaction))
        {
            SqliteParameter id = insert.Parameters.Add("$id", SqliteType.Integer);
            SqliteParameter payload = insert.Parameters.Add("$payload", SqliteType.Blob);
            foreach (CreatureSave creature in world.Creatures)
            {
                id.Value = (long)creature.Id.Value;
                payload.Value = _creatures.Encode(SaveMapping.ToRecord(creature));
                insert.ExecuteNonQuery();
            }
        }

        using (SqliteCommand insert = store.Command("INSERT INTO players (account, payload) VALUES ($account, $payload);", transaction))
        {
            SqliteParameter account = insert.Parameters.Add("$account", SqliteType.Integer);
            SqliteParameter payload = insert.Parameters.Add("$payload", SqliteType.Blob);
            foreach (PlayerSave player in players)
            {
                account.Value = (long)player.Account;
                PlayerProgress? progress = player.Progress;
                payload.Value = _players.Encode(new PlayerRecord
                {
                    X = player.Position.X,
                    Y = player.Position.Y,
                    Z = player.Position.Z,
                    Class = progress is PlayerProgress p ? (byte)p.Class : null,
                    Rank = (byte)(progress?.Rank ?? 0),
                    Level = progress?.Level ?? 0,
                    Xp = progress?.Xp ?? 0,
                    Gold = player.Inventory?.Gold,
                    Items = [.. (player.Inventory?.Items ?? []).Select(item => new SlotRecord { Item = item.Item, Count = item.Count })],
                    Equipped = player.Equipped ?? [],
                });
                insert.ExecuteNonQuery();
            }
        }

        using (SqliteCommand insert = store.Command("INSERT INTO reputation (species, region, player, score) VALUES ($species, $region, $player, $score);", transaction))
        {
            SqliteParameter species = insert.Parameters.Add("$species", SqliteType.Text);
            SqliteParameter region = insert.Parameters.Add("$region", SqliteType.Integer);
            SqliteParameter player = insert.Parameters.Add("$player", SqliteType.Integer);
            SqliteParameter score = insert.Parameters.Add("$score", SqliteType.Integer);
            foreach (ReputationSave entry in world.Reputation)
            {
                (species.Value, region.Value, player.Value, score.Value) = (entry.Species, entry.Region, (long)entry.Player, entry.Score);
                insert.ExecuteNonQuery();
            }
        }

        WriteMeta(transaction, TickKey, (long)world.Tick);
        WriteMeta(transaction, RandomKey, unchecked((long)world.RandomState));
        WriteMeta(transaction, LastIdKey, (long)world.LastStableId);
        WriteMeta(transaction, SavedKey, 1);
    }

    public void PutCreature(SqliteTransaction transaction, CreatureSave creature)
    {
        using SqliteCommand upsert = store.Command("INSERT OR REPLACE INTO creatures (stable_id, payload) VALUES ($id, $payload);", transaction);
        upsert.Parameters.AddWithValue("$id", (long)creature.Id.Value);
        upsert.Parameters.AddWithValue("$payload", _creatures.Encode(SaveMapping.ToRecord(creature)));
        upsert.ExecuteNonQuery();
    }

    public ulong MaxIntentSession()
    {
        using SqliteCommand select = store.Command("SELECT max(session) FROM intent_dedupe;", null);
        return select.ExecuteScalar() is long session ? (ulong)session : 0;
    }

    public SavedWorld? Load()
    {
        if (store.ReadMeta(SavedKey, null) == 0)
        {
            return null;
        }

        var creatures = new List<CreatureSave>();
        using (SqliteCommand select = store.Command("SELECT stable_id, payload FROM creatures ORDER BY stable_id;", null))
        using (SqliteDataReader reader = select.ExecuteReader())
        {
            while (reader.Read())
            {
                var id = new StableId((ulong)reader.GetInt64(0));
                creatures.Add(SaveMapping.FromRecord(id, _creatures.Decode((byte[])reader.GetValue(1))));
            }
        }

        var players = new List<PlayerSave>();
        using (SqliteCommand select = store.Command("SELECT account, payload FROM players ORDER BY account;", null))
        using (SqliteDataReader reader = select.ExecuteReader())
        {
            while (reader.Read())
            {
                PlayerRecord record = _players.Decode((byte[])reader.GetValue(1));
                PlayerProgress? progress = record.Class is byte playerClass ? new PlayerProgress(playerClass, record.Rank, record.Level, record.Xp) : null;
                InventorySave? inventory = record.Gold is int gold ? new InventorySave(gold, [.. record.Items.Select(item => new SavedItem(item.Item, item.Count))]) : null;
                players.Add(new PlayerSave((ulong)reader.GetInt64(0), new Vector3(record.X, record.Y, record.Z), progress, inventory, record.Equipped));
            }
        }

        var reputation = new List<ReputationSave>();
        using (SqliteCommand select = store.Command("SELECT species, region, player, score FROM reputation ORDER BY species, region, player;", null))
        using (SqliteDataReader reader = select.ExecuteReader())
        {
            while (reader.Read())
            {
                reputation.Add(new ReputationSave(reader.GetString(0), reader.GetInt32(1), (ulong)reader.GetInt64(2), reader.GetInt32(3)));
            }
        }

        var world = new WorldSnapshot(
            (ulong)store.ReadMeta(TickKey, null),
            unchecked((ulong)store.ReadMeta(RandomKey, null)),
            (ulong)store.ReadMeta(LastIdKey, null),
            creatures,
            reputation);
        return new SavedWorld(world, players);
    }

    private void WriteMeta(SqliteTransaction transaction, string key, long value)
    {
        using SqliteCommand command = store.Command("INSERT OR REPLACE INTO meta (key, value) VALUES ($key, $value);", transaction);
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }
}

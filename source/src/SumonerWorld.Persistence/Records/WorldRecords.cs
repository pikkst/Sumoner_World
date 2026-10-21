using MessagePack;

namespace SumonerWorld.Persistence.Records;

/// <summary>A saved player (payload v1): where the avatar was last.</summary>
[MessagePackObject]
public sealed record PlayerRecord
{
    /// <summary>Position X.</summary>
    [Key(0)]
    public float X { get; init; }

    /// <summary>Position Y.</summary>
    [Key(1)]
    public float Y { get; init; }

    /// <summary>Position Z.</summary>
    [Key(2)]
    public float Z { get; init; }

    /// <summary>Summoner class (M2-09); null in saves from before it existed.</summary>
    [Key(3)]
    public byte? Class { get; init; }

    /// <summary>Summoner rank.</summary>
    [Key(4)]
    public byte Rank { get; init; }

    /// <summary>Level.</summary>
    [Key(5)]
    public int Level { get; init; }

    /// <summary>Total experience.</summary>
    [Key(6)]
    public int Xp { get; init; }

    /// <summary>Gold (M2-13); null in saves from before the inventory existed.</summary>
    [Key(7)]
    public int? Gold { get; init; }

    /// <summary>Inventory slots by item id.</summary>
    [Key(8)]
    public IReadOnlyList<SlotRecord> Items { get; init; } = [];
}

/// <summary>A saved inventory slot.</summary>
[MessagePackObject]
public sealed record SlotRecord
{
    /// <summary>Item id.</summary>
    [Key(0)]
    public string Item { get; init; } = string.Empty;

    /// <summary>How many.</summary>
    [Key(1)]
    public int Count { get; init; }
}

/// <summary>A saved creature (payload v1, "CreatureMind v1" inside, AI Bible §4.6).</summary>
[MessagePackObject]
public sealed record CreatureRecord
{
    /// <summary>Species data id.</summary>
    [Key(0)]
    public string Species { get; init; } = string.Empty;

    /// <summary>Position (x, y, z).</summary>
    [Key(1)]
    public IReadOnlyList<float> Position { get; init; } = [];

    /// <summary>Facing (x, y, z).</summary>
    [Key(2)]
    public IReadOnlyList<float> Facing { get; init; } = [];

    /// <summary>Herd.</summary>
    [Key(3)]
    public int Group { get; init; }

    /// <summary>Mind.</summary>
    [Key(4)]
    public MindRecord Mind { get; init; } = new();

    /// <summary>Bond.</summary>
    [Key(5)]
    public BondRecord Bond { get; init; } = new();

    /// <summary>Growth (M2-09); null in saves from before it existed.</summary>
    [Key(6)]
    public GrowthRecord? Growth { get; init; }

    /// <summary>Skills (M2-10); null in saves from before they existed.</summary>
    [Key(7)]
    public SkillsRecord? Skills { get; init; }

    /// <summary>Combos (M2-12); null in saves from before they existed.</summary>
    [Key(8)]
    public CombosRecord? Combos { get; init; }
}

/// <summary>A creature's combos (combat spec §10): learned bits, landings per combo, times it started one by itself.</summary>
[MessagePackObject]
public sealed record CombosRecord
{
    /// <summary>Learned combo bits.</summary>
    [Key(0)]
    public int Learned { get; init; }

    /// <summary>Landings per combo, in combo order.</summary>
    [Key(1)]
    public IReadOnlyList<int> Landed { get; init; } = [];

    /// <summary>Times it started a combo by itself.</summary>
    [Key(2)]
    public int SelfStarted { get; init; }
}

/// <summary>A creature's skills (companions spec §5, §9).</summary>
[MessagePackObject]
public sealed record SkillsRecord
{
    /// <summary>Learned node bits (bit = node index in the species' tree).</summary>
    [Key(0)]
    public ulong Learned { get; init; }

    /// <summary>Points spent.</summary>
    [Key(1)]
    public int Spent { get; init; }

    /// <summary>Tick it got points it has not spent (0 = none).</summary>
    [Key(2)]
    public ulong UnspentSince { get; init; }

    /// <summary>Skills it picked for itself.</summary>
    [Key(3)]
    public int PickedItself { get; init; }
}

/// <summary>A creature's growth: tier, level, stage and experience per role (companions spec §9).</summary>
[MessagePackObject]
public sealed record GrowthRecord
{
    /// <summary>Tier.</summary>
    [Key(0)]
    public byte Tier { get; init; }

    /// <summary>Level.</summary>
    [Key(1)]
    public int Level { get; init; }

    /// <summary>Life stage.</summary>
    [Key(2)]
    public byte Stage { get; init; }

    /// <summary>Combat experience.</summary>
    [Key(3)]
    public int CombatXp { get; init; }

    /// <summary>Work experience.</summary>
    [Key(4)]
    public int WorkXp { get; init; }

    /// <summary>Travel experience.</summary>
    [Key(5)]
    public int TravelXp { get; init; }

    /// <summary>Distance toward the next travel point, in centimetres.</summary>
    [Key(6)]
    public int TravelCm { get; init; }
}

/// <summary>A creature's mind: personality (boldness, loyalty, curiosity, temper), memories, opinion baselines, needs.</summary>
[MessagePackObject]
public sealed record MindRecord
{
    /// <summary>Personality at birth.</summary>
    [Key(0)]
    public IReadOnlyList<sbyte> Birth { get; init; } = [];

    /// <summary>Personality now.</summary>
    [Key(1)]
    public IReadOnlyList<sbyte> Current { get; init; } = [];

    /// <summary>Core memories.</summary>
    [Key(2)]
    public IReadOnlyList<MemoryRecordData> Core { get; init; } = [];

    /// <summary>Episodic memories.</summary>
    [Key(3)]
    public IReadOnlyList<MemoryRecordData> Episodic { get; init; } = [];

    /// <summary>Opinion baselines.</summary>
    [Key(4)]
    public IReadOnlyList<BaselineRecord> Baselines { get; init; } = [];

    /// <summary>Needs, in need order.</summary>
    [Key(5)]
    public IReadOnlyList<NeedRecord> Needs { get; init; } = [];
}

/// <summary>One memory (AI Bible §3.2).</summary>
[MessagePackObject]
public sealed record MemoryRecordData
{
    /// <summary>Memory event type id.</summary>
    [Key(0)]
    public ushort EventType { get; init; }

    /// <summary>Subject kind.</summary>
    [Key(1)]
    public byte SubjectKind { get; init; }

    /// <summary>Subject id.</summary>
    [Key(2)]
    public ulong SubjectId { get; init; }

    /// <summary>Valence.</summary>
    [Key(3)]
    public sbyte Valence { get; init; }

    /// <summary>Salience.</summary>
    [Key(4)]
    public ushort Salience { get; init; }

    /// <summary>Tick it was formed.</summary>
    [Key(5)]
    public uint CreatedTick { get; init; }

    /// <summary>Tick it was last reinforced.</summary>
    [Key(6)]
    public uint LastReinforcedTick { get; init; }

    /// <summary>Times it happened.</summary>
    [Key(7)]
    public ushort Count { get; init; }

    /// <summary>Tags.</summary>
    [Key(8)]
    public byte Tags { get; init; }
}

/// <summary>An opinion baseline in tenths.</summary>
[MessagePackObject]
public sealed record BaselineRecord
{
    /// <summary>Subject kind.</summary>
    [Key(0)]
    public byte SubjectKind { get; init; }

    /// <summary>Subject id.</summary>
    [Key(1)]
    public ulong SubjectId { get; init; }

    /// <summary>Trust (tenths).</summary>
    [Key(2)]
    public int Trust { get; init; }

    /// <summary>Fear (tenths).</summary>
    [Key(3)]
    public int Fear { get; init; }

    /// <summary>Affection (tenths).</summary>
    [Key(4)]
    public int Affection { get; init; }

    /// <summary>Respect (tenths).</summary>
    [Key(5)]
    public int Respect { get; init; }
}

/// <summary>A need's level and rate.</summary>
[MessagePackObject]
public sealed record NeedRecord
{
    /// <summary>Level × 1000.</summary>
    [Key(0)]
    public int Milli { get; init; }

    /// <summary>When the level was set.</summary>
    [Key(1)]
    public uint Tick { get; init; }

    /// <summary>Gain per day.</summary>
    [Key(2)]
    public int Rate { get; init; }
}

/// <summary>A creature's bond (Bonding Contract v1.0.0).</summary>
[MessagePackObject]
public sealed record BondRecord
{
    /// <summary>Ownership state.</summary>
    [Key(0)]
    public byte State { get; init; }

    /// <summary>Owner account.</summary>
    [Key(1)]
    public ulong Owner { get; init; }

    /// <summary>Pact Terms mask.</summary>
    [Key(2)]
    public byte Terms { get; init; }

    /// <summary>Broken terms so far.</summary>
    [Key(3)]
    public byte BrokenTerms { get; init; }

    /// <summary>Bond level.</summary>
    [Key(4)]
    public byte BondLevel { get; init; }

    /// <summary>Summoned now.</summary>
    [Key(5)]
    public bool Summoned { get; init; }

    /// <summary>Tick the Pact was sealed.</summary>
    [Key(6)]
    public uint PactTick { get; init; }

    /// <summary>Tick distrust started.</summary>
    [Key(7)]
    public uint DistrustSinceTick { get; init; }

    /// <summary>Currently distrustful.</summary>
    [Key(8)]
    public bool Distrustful { get; init; }
}

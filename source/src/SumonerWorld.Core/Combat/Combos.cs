using SumonerWorld.Core.Bonding;
using SumonerWorld.Core.Data;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Simulation;
using SumonerWorld.Core.World;

namespace SumonerWorld.Core.Combat;

/// <summary>
/// What a creature knows of combos (combat spec §10, M2-12): learned combo bits, how often it landed each (up to four combos),
/// how often it started one by itself, and until when it shows its pride after starting one by itself the first time. No padding
/// (hashed raw).
/// </summary>
/// <param name="ProudUntil">Tick its "I learned your style" emote ends.</param>
/// <param name="Learned">Learned combo bits (bit = index in the combos data).</param>
/// <param name="Landed0">Times it landed combo 0.</param>
/// <param name="Landed1">Times it landed combo 1.</param>
/// <param name="Landed2">Times it landed combo 2.</param>
/// <param name="Landed3">Times it landed combo 3.</param>
/// <param name="SelfStarted">Times it started a combo by itself.</param>
public record struct CreatureCombos(ulong ProudUntil, int Learned, int Landed0, int Landed1, int Landed2, int Landed3, int SelfStarted)
{
    /// <summary>True when combo <paramref name="index"/> is learned.</summary>
    public readonly bool Knows(int index) => (Learned & (1 << index)) != 0;

    /// <summary>Times it landed combo <paramref name="index"/>.</summary>
    public readonly int LandedOf(int index) => index switch
    {
        0 => Landed0,
        1 => Landed1,
        2 => Landed2,
        _ => Landed3,
    };

    /// <summary>The same with one more landing of combo <paramref name="index"/>.</summary>
    public readonly CreatureCombos WithLanding(int index) => index switch
    {
        0 => this with { Landed0 = Landed0 + 1 },
        1 => this with { Landed1 = Landed1 + 1 },
        2 => this with { Landed2 = Landed2 + 1 },
        _ => this with { Landed3 = Landed3 + 1 },
    };
}

/// <summary>Published when a combo lands.</summary>
/// <param name="Attacker">Who landed it.</param>
/// <param name="Target">Who took it.</param>
/// <param name="Combo">Index in the combos data.</param>
public readonly record struct ComboLanded(Entity Attacker, Entity Target, int Combo);

/// <summary>Combo matching (combat spec §10).</summary>
public static class ComboRules
{
    /// <summary>The first combo a move with <paramref name="move"/>'s tags makes on a target with these effects, or −1.</summary>
    public static int Match(IDataRegistry data, MoveDefinition move, EcsWorld world, Entity target, ulong now)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(move);
        StatusMarks marks = StatusRules.FlagsOf(world, target, now);
        for (int i = 0; i < data.Combos.Count; i++)
        {
            ComboDefinition combo = data.Combos[i];
            if (move.Tags.Contains(combo.MoveTag) && marks.HasFlag(MarkOf(combo.Effect)))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The status mark of an effect id.</summary>
    public static StatusMarks MarkOf(string effect) => effect switch
    {
        "rooted" => StatusMarks.Rooted,
        "slowed" => StatusMarks.Slowed,
        "mending" => StatusMarks.Mending,
        _ => StatusMarks.None,
    };
}

/// <summary>
/// Combo learning (combat spec §10, AI Bible §8, M2-12): a pacted creature that lands a combo <see cref="ComboDefinition.LearnAfter"/>
/// times knows it. From then on, when its summoner's trigger spell (entangle) catches a creature, every summoned companion that
/// knows the combo goes for that creature by itself, its combo move first; the first time it does, it shows it learned its
/// summoner's style (Affection). Runs after the hits of the tick.
/// </summary>
public sealed class ComboSystem : ISimulationSystem
{
    /// <summary>How long the "I learned your style" moment shows (2 s).</summary>
    public const ulong ProudTicks = 60;

    private const int EventCapacity = 16;

    private readonly CreatureWorld _world;
    private readonly ComponentStore<CreatureCombos> _combos;
    private readonly ComponentStore<CompanionState> _companions;
    private readonly ComponentStore<PlayerAvatar> _players;
    private readonly ComponentStore<MindRef> _minds;
    private readonly int[] _triggers;

    /// <summary>Creates the system for a world.</summary>
    public ComboSystem(CreatureWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        _world = world;
        _combos = world.World.Store<CreatureCombos>();
        _companions = world.World.Store<CompanionState>();
        _players = world.World.Store<PlayerAvatar>();
        _minds = world.World.Store<MindRef>();
        _triggers = [.. world.Data.Combos.Select(combo => world.Data.MoveIndex(combo.TriggerSpell))];
        if (!world.Events.IsRegistered<ComboLanded>())
        {
            world.Events.Register<ComboLanded>(EventCapacity);
        }
    }

    /// <summary>Combos learned since the start.</summary>
    public int Learned { get; private set; }

    /// <summary>Combos companions started by themselves since the start.</summary>
    public int SelfStarted { get; private set; }

    /// <inheritdoc />
    public SimulationStage Stage => SimulationStage.Combat;

    /// <inheritdoc />
    public void Execute(in SimulationTick tick)
    {
        foreach (ComboLanded landed in _world.Events.Read<ComboLanded>())
        {
            Learn(landed.Attacker, landed.Combo);
        }

        foreach (CombatHit hit in _world.Events.Read<CombatHit>())
        {
            for (int combo = 0; combo < _triggers.Length; combo++)
            {
                if (hit.Move == _triggers[combo] && _players.Has(hit.Attacker))
                {
                    StartByThemselves(_players.Get(hit.Attacker).AccountId, hit.Target, combo, tick.Index);
                }
            }
        }
    }

    private void Learn(Entity creature, int combo)
    {
        if (!_combos.Has(creature) || !_minds.Has(creature) || _world.Services.Bonding.GetBond(_minds.Get(creature).Handle).State != OwnershipState.Pacted)
        {
            return;
        }

        ref CreatureCombos combos = ref _combos.Get(creature);
        combos = combos.WithLanding(combo);
        if (!combos.Knows(combo) && combos.LandedOf(combo) >= _world.Data.Combos[combo].LearnAfter)
        {
            combos.Learned |= 1 << combo;
            Learned++;
        }
    }

    // The summoner's trigger spell caught a creature: companions that know the combo go for it.
    private void StartByThemselves(ulong account, Entity target, int combo, ulong now)
    {
        ReadOnlySpan<Entity> companions = _companions.Entities;
        for (int i = 0; i < companions.Length; i++)
        {
            Entity companion = companions[i];
            if (!_combos.Has(companion) || !_combos.Get(companion).Knows(combo)
                || _world.Services.Bonding.GetBond(_minds.Get(companion).Handle).Owner != account)
            {
                continue;
            }

            ref CompanionState state = ref _companions.Components[i];
            state.Foe = target;
            state.FoeUntil = now + CompanionAlertSystem.FoeTicks;
            ref CreatureCombos combos = ref _combos.Get(companion);
            combos.SelfStarted++;
            if (combos.SelfStarted == 1)
            {
                combos.ProudUntil = now + ProudTicks;
            }

            SelfStarted++;
        }
    }
}

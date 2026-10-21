using System.Numerics;
using SumonerWorld.Core.Cognition.Appraisal;
using SumonerWorld.Core.Combat;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Simulation;
using SumonerWorld.Core.World;

namespace SumonerWorld.Core.Bonding;

/// <summary>
/// A creature answering a Pact offer with its body (M2-05: "the creature's answer is shown by behaviour first, text second").
/// <c>Accepted</c> and <c>Moving</c> are ints so the struct has no padding (it is hashed as raw bytes).
/// </summary>
/// <param name="Since">Tick of the offer.</param>
/// <param name="Player">The offering player's avatar.</param>
/// <param name="Accepted">1 if it accepted.</param>
/// <param name="Moving">1 once it has started walking (to the player, or away).</param>
public record struct PactAnswer(ulong Since, Entity Player, int Accepted, int Moving);

/// <summary>
/// Plays the answer to a Pact offer (M2-05, AI Bible §6.2 body language) for <see cref="TotalTicks"/>, in place of the behaviour
/// trees: for <see cref="ConsiderTicks"/> the creature stops, faces the player and shows Curiosity (it is thinking it over); then,
/// if it accepted, it walks up to the player showing Affection; if it refused, it turns its back and walks
/// <see cref="TurnAwayMetres"/> away. The player's client prints the answer only after this has begun. A snared creature answers
/// on the spot (it turns away, or keeps facing the player); a downed one does not answer. Deterministic (R11).
/// </summary>
public sealed class PactAnswerSystem : ISimulationSystem
{
    /// <summary>How long the creature considers the offer (0.5 s).</summary>
    public const ulong ConsiderTicks = 15;

    /// <summary>How long the whole answer lasts (4 s).</summary>
    public const ulong TotalTicks = 120;

    /// <summary>How far a refusing creature walks off, in metres.</summary>
    public const float TurnAwayMetres = 6f;

    private const float AcceptSpeedPercent = 80;
    private const float RefuseSpeedPercent = 60;
    private const float Percent = 100;
    private const float GapMetres = 0.6f;

    private readonly CreatureWorld _world;
    private readonly ComponentStore<PactAnswer> _answers;
    private readonly ComponentStore<Position> _positions;
    private readonly ComponentStore<Facing> _facings;
    private readonly ComponentStore<MoveTarget> _targets;
    private readonly ComponentStore<CurrentEmote> _emotes;
    private readonly ComponentStore<Snared> _snared;
    private readonly ComponentStore<Downed> _downed;

    /// <summary>Creates the system for a world.</summary>
    public PactAnswerSystem(CreatureWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        _world = world;
        EcsWorld ecs = world.World;
        _answers = ecs.Store<PactAnswer>();
        _positions = ecs.Store<Position>();
        _facings = ecs.Store<Facing>();
        _targets = ecs.Store<MoveTarget>();
        _emotes = ecs.Store<CurrentEmote>();
        _snared = ecs.Store<Snared>();
        _downed = ecs.Store<Downed>();
    }

    /// <inheritdoc />
    public SimulationStage Stage => SimulationStage.Behaviour;

    /// <summary>Starts a creature's answer (called when the offer commits); it stops where it is.</summary>
    public static void Begin(EcsWorld world, Entity creature, Entity player, bool accepted, ulong now)
    {
        ArgumentNullException.ThrowIfNull(world);
        var answer = new PactAnswer(now, player, accepted ? 1 : 0, 0);
        if (world.Has<PactAnswer>(creature))
        {
            world.GetComponent<PactAnswer>(creature) = answer;
        }
        else
        {
            world.Add(creature, answer);
        }

        world.GetComponent<MoveTarget>(creature) = world.GetComponent<MoveTarget>(creature) with { Active = 0 };
    }

    /// <inheritdoc />
    public void Execute(in SimulationTick tick)
    {
        ReadOnlySpan<Entity> entities = _answers.Entities;

        // Backwards, because removing an answer moves the last one into its place.
        for (int i = entities.Length - 1; i >= 0; i--)
        {
            Entity creature = entities[i];
            ref PactAnswer answer = ref _answers.Get(creature);
            ulong age = tick.Index - answer.Since;
            if (age >= TotalTicks || !_positions.Has(answer.Player) || _downed.Has(creature))
            {
                _world.World.Remove<PactAnswer>(creature);
                continue;
            }

            Answer(creature, ref answer, age);
        }
    }

    private void Answer(Entity creature, ref PactAnswer answer, ulong age)
    {
        Vector3 position = _positions.Get(creature).Value;
        Vector3 toPlayer = _positions.Get(answer.Player).Value - position;
        toPlayer.Y = 0;
        float distance = toPlayer.Length();
        Vector3 towards = distance > 0 ? toPlayer / distance : _facings.Get(creature).Value;
        if (age < ConsiderTicks)
        {
            _facings.Get(creature) = new Facing(towards);
            _emotes.Get(creature) = new CurrentEmote(Emote.Curiosity);
            return;
        }

        bool accepted = answer.Accepted != 0;
        if (accepted)
        {
            _emotes.Get(creature) = new CurrentEmote(Emote.Affection);
        }

        if (_snared.Has(creature))
        {
            _facings.Get(creature) = new Facing(accepted ? towards : -towards);
            return;
        }

        if (answer.Moving != 0)
        {
            return;
        }

        float speed = _world.Settings.WalkSpeed * (accepted ? AcceptSpeedPercent : RefuseSpeedPercent) / Percent;
        float stop = _world.Sizes.RadiusOf(creature) + _world.Sizes.RadiusOf(answer.Player) + GapMetres;
        Vector3 target = accepted
            ? position + (towards * MathF.Max(0, distance - stop))
            : Vector3.Clamp(position - (towards * TurnAwayMetres), _world.Settings.BoundsMin, _world.Settings.BoundsMax);
        if (_world.Paths.Request(creature, position, target, speed))
        {
            _targets.Get(creature) = _targets.Get(creature) with { Active = 0 };
            answer.Moving = 1;
        }
    }
}

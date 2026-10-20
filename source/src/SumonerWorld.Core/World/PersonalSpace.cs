using System.Numerics;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Simulation;
using SumonerWorld.Core.Spatial;

namespace SumonerWorld.Core.World;

/// <summary>Body footprint radii on the ground plane: creatures from their species data (<c>bodyRadiusCm</c>), players fixed.</summary>
public sealed class BodySizes
{
    /// <summary>A player's footprint radius in metres.</summary>
    public const float PlayerRadius = 0.4f;

    private const float CentimetresPerMetre = 100f;

    private readonly ComponentStore<SpeciesRef> _species;
    private readonly float[] _radii;

    /// <summary>Creates the sizes for a world's species.</summary>
    public BodySizes(IEcsWorld world, IReadOnlyList<SpeciesDefinition> species)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(species);
        _species = world.Store<SpeciesRef>();
        _radii = [.. species.Select(definition => definition.BodyRadiusCm / CentimetresPerMetre)];
        Largest = _radii.Length == 0 ? PlayerRadius : Math.Max(PlayerRadius, _radii.Max());
    }

    /// <summary>The largest radius of any body.</summary>
    public float Largest { get; }

    /// <summary>The footprint radius of a creature or player avatar.</summary>
    public float RadiusOf(Entity entity) => _species.Has(entity) ? _radii[_species.Get(entity).Index] : PlayerRadius;
}

/// <summary>
/// Keeps creatures from standing inside each other or inside players (seen in the M1-22 dev-server test). After movement, each
/// creature that overlaps a neighbour's footprint steps away: half the overlap from another creature (the other does the same in
/// its turn), the whole overlap from a player, whose position the server never changes outside the player's own input (client
/// prediction). At most <see cref="MaxStepPerTick"/> per tick, so the correction looks like a shuffle, not a jump. Deterministic
/// (fixed order, exact ties broken by entity index) and allocation-free (R11, R21).
/// </summary>
public sealed class SeparationSystem(IEcsWorld world, ISpatialIndex grid, BodySizes sizes, Heightfield? ground) : ISimulationSystem
{
    /// <summary>Largest correction per tick, in metres (about half the walking speed).</summary>
    public const float MaxStepPerTick = 0.05f;

    private const int NeighbourCapacity = 32;
    private const float Coincident = 1e-4f;

    private readonly Entity[] _neighbours = new Entity[NeighbourCapacity];
    private readonly ComponentStore<SpeciesRef> _creatures = world.Store<SpeciesRef>();
    private readonly ComponentStore<Position> _positions = world.Store<Position>();
    private readonly ComponentStore<PlayerAvatar> _players = world.Store<PlayerAvatar>();

    /// <inheritdoc />
    public SimulationStage Stage => SimulationStage.Movement;

    /// <summary>Corrections made since the start.</summary>
    public int Corrections { get; private set; }

    /// <inheritdoc />
    public void Execute(in SimulationTick tick)
    {
        ReadOnlySpan<Entity> creatures = _creatures.Entities;
        for (int i = 0; i < creatures.Length; i++)
        {
            Entity creature = creatures[i];
            ref Position position = ref _positions.Get(creature);
            Vector3 step = Overlap(creature, position.Value);
            if (step == Vector3.Zero)
            {
                continue;
            }

            float length = step.Length();
            if (length > MaxStepPerTick)
            {
                step *= MaxStepPerTick / length;
            }

            Vector3 moved = position.Value + step;
            position = new Position(ground is null ? moved : moved with { Y = ground.HeightAt(moved.X, moved.Z) });
            grid.Move(creature, position.Value);
            Corrections++;
        }
    }

    private Vector3 Overlap(Entity creature, Vector3 at)
    {
        float radius = sizes.RadiusOf(creature);
        SpatialQueryResult found = grid.QueryRadius(at, radius + sizes.Largest, _neighbours);
        Vector3 step = Vector3.Zero;
        for (int n = 0; n < found.Count; n++)
        {
            Entity other = _neighbours[n];
            if (other == creature || !_positions.Has(other))
            {
                continue;
            }

            Vector3 apart = at - _positions.Get(other).Value;
            apart.Y = 0;
            float distance = apart.Length();
            float overlap = radius + sizes.RadiusOf(other) - distance;
            if (overlap <= 0)
            {
                continue;
            }

            Vector3 away = distance > Coincident ? apart / distance : (creature.Index < other.Index ? Vector3.UnitX : -Vector3.UnitX);
            step += away * (_players.Has(other) ? overlap : overlap / 2);
        }

        return step;
    }
}

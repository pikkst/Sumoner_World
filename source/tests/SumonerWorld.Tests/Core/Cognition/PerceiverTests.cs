using System.Numerics;
using SumonerWorld.Core.Cognition;
using SumonerWorld.Core.Cognition.Memory;
using SumonerWorld.Core.Cognition.Opinions;
using SumonerWorld.Core.Cognition.Perception;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Spatial;
using static SumonerWorld.Tests.Core.Cognition.CognitionTestData;

namespace SumonerWorld.Tests.Core.Cognition;

[Collection(AllocationMeasurements.Name)]
public class PerceiverTests
{
    private static readonly PerceptionDefinition Senses = new() { SightRange = 50, SightHalfAngle = 60, HearingRange = 10 };
    private static readonly Entity Self = new(0, 0);

    [Fact]
    public void Things_in_the_view_cone_are_seen_close_things_behind_are_heard_and_the_rest_unnoticed()
    {
        var world = new World();
        Entity ahead = world.Add(1, new Vector3(0, 0, 30));
        Entity closeBehind = world.Add(2, new Vector3(0, 0, -5));
        Entity farBehind = world.Add(3, new Vector3(0, 0, -30));
        Entity tooFar = world.Add(4, new Vector3(0, 0, 80));

        Percept[] percepts = world.Perceive();

        Assert.Equal([new Percept(closeBehind, 500, Sense.Hearing), new Percept(ahead, 3000, Sense.Sight)], percepts);
        Assert.DoesNotContain(percepts, percept => percept.Entity == farBehind || percept.Entity == tooFar || percept.Entity == Self);
    }

    [Fact]
    public void Something_both_seen_and_heard_is_reported_once_as_seen()
    {
        var world = new World();
        Entity near = world.Add(1, new Vector3(0, 0, 5));

        Assert.Equal([new Percept(near, 500, Sense.Sight)], world.Perceive());
    }

    [Fact]
    public void A_full_buffer_keeps_the_nearest()
    {
        var world = new World();
        for (int i = 1; i <= 10; i++)
        {
            world.Add(i, new Vector3(0, 0, 50 - (i * 4)));
        }

        Percept[] nearest = world.Perceive(capacity: 3);

        Assert.Equal([1000, 1400, 1800], nearest.Select(percept => percept.DistanceCm));
    }

    [Fact]
    public void Focus_prefers_players_then_strong_feelings_then_the_nearest()
    {
        var world = new World();
        Entity creatureNear = world.Add(1, new Vector3(0, 0, 3));
        Entity hatedCreature = world.Add(2, new Vector3(0, 0, 20));
        Entity player = world.Add(3, new Vector3(0, 0, 40));
        world.Store.Remember(world.Mind, KilledKin, World.Subject(hatedCreature), 0);

        Assert.Equal(player, world.Focus().Entity);

        world.Remove(player);
        Assert.Equal(hatedCreature, world.Focus().Entity);

        world.Remove(hatedCreature);
        FocusChoice nearest = world.Focus();
        Assert.Equal(creatureNear, nearest.Entity);
        Assert.Equal(60, nearest.Distance);
    }

    [Fact]
    public void Nothing_perceived_means_no_focus()
    {
        var world = new World();

        Assert.False(world.TryFocus(out _));
    }

    [Fact]
    public void Perceiving_does_not_allocate()
    {
        var world = new World();
        for (int i = 1; i < 40; i++)
        {
            world.Add(i, new Vector3(i % 7, 0, i));
        }

        world.Perceive();
        var buffer = new Percept[16];
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 2_000; i++)
        {
            world.PerceiveInto(buffer);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private sealed class World : ISubjectResolver
    {
        private const int PlayerIndex = 3;
        private readonly SpatialGrid _grid = new(new SpatialGridSettings(-100, -100, 200, 200, 16, 64));
        private readonly Perceiver _perceiver;

        public World()
        {
            Store = new CreatureMindStore(ShippedRules(), capacity: 2);
            Mind = Store.Allocate(new Personality(0, 0, 0, 0), Needs);
            _perceiver = new Perceiver(_grid, new OpinionModel(Store, ShippedRules()));
            _grid.Insert(Self, Vector3.Zero);
        }

        public CreatureMindStore Store { get; }

        public MindHandle Mind { get; }

        public Entity Add(int index, Vector3 position)
        {
            var entity = new Entity(index, 0);
            _grid.Insert(entity, position);
            return entity;
        }

        public void Remove(Entity entity) => _grid.Remove(entity);

        public Percept[] Perceive(int capacity = 16)
        {
            var buffer = new Percept[capacity];
            return buffer[..PerceiveInto(buffer)];
        }

        public int PerceiveInto(Percept[] buffer) => _perceiver.Perceive(Self, Vector3.Zero, Vector3.UnitZ, Senses, buffer);

        public FocusChoice Focus() => TryFocus(out FocusChoice focus) ? focus : throw new InvalidOperationException("No focus.");

        public bool TryFocus(out FocusChoice focus) => _perceiver.SelectFocus(Mind, Perceive(), Senses, this, 0, out focus);

        public static MemorySubject Subject(Entity entity) => entity.Index == PlayerIndex
            ? new MemorySubject(SubjectKind.Player, 1_000)
            : new MemorySubject(SubjectKind.Creature, (ulong)entity.Index);

        public bool TryResolve(Entity entity, out MemorySubject subject)
        {
            subject = Subject(entity);
            return true;
        }
    }
}

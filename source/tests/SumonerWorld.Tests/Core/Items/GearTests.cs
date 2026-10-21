using System.Numerics;
using SumonerWorld.Core.Bonding;
using SumonerWorld.Core.Cognition;
using SumonerWorld.Core.Combat;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Intents;
using SumonerWorld.Core.Items;
using SumonerWorld.Core.Progression;
using SumonerWorld.Core.Simulation;
using SumonerWorld.Core.Transactions;
using SumonerWorld.Core.World;
using SumonerWorld.Scenarios;

namespace SumonerWorld.Tests.Core.Items;

public sealed class GearTests : IDisposable
{
    private const ulong Alice = 1001;
    private const ulong Bob = 1002;
    private static readonly Vector3 Start = new(100, 0, 100);

    private readonly CreatureWorld _world = new(ShippedData.Current, new CreatureWorldSettings(), seed: 62);
    private readonly SimulationScheduler _scheduler;
    private readonly TransactionProcessor _processor = new(new IntentDeduplicator());
    private readonly Entity _alice;
    private readonly Entity _bob;
    private uint _sequence;

    public GearTests()
    {
        _scheduler = new SimulationScheduler(_world.Clock, _world.Events);
        _world.CreateSystems().RegisterAll(_scheduler);
        _alice = _world.SpawnPlayerAvatar(Alice, Start);
        _bob = _world.SpawnPlayerAvatar(Bob, Start + new Vector3(2, 0, 0));
    }

    private Inventory Bag => _world.Economy.Inventories.Of(Alice);

    public void Dispose() => _world.Dispose();

    [Fact]
    public void Putting_on_a_staff_raises_power_and_taking_it_off_brings_it_back_to_the_bag()
    {
        int staff = Give("wooden_staff");
        int before = Me.Power;

        IntentResult equipped = Run(new EquipTransaction(_world, _alice, new StableId(0), staff));

        Assert.True(equipped.IsApplied);
        Assert.Equal((byte)EquipPosition.Weapon, equipped.Detail);
        Assert.Equal(before * 110 / 100, Me.Power);
        Assert.Equal(0, Bag.CountOf(staff));
        Assert.Equal(staff, _world.World.GetComponent<Equipment>(_alice).ItemAt(EquipPosition.Weapon));

        Assert.True(Run(new UnequipTransaction(_world, _alice, new StableId(0), EquipPosition.Weapon)).IsApplied);
        Assert.Equal(before, Me.Power);
        Assert.Equal(1, Bag.CountOf(staff));
        Assert.Equal(IntentRejectReason.InvalidArguments, Run(new UnequipTransaction(_world, _alice, new StableId(0), EquipPosition.Weapon)).Reason);
    }

    [Fact]
    public void A_second_charm_goes_to_the_second_position_and_a_third_swaps_the_old_one_back_into_the_bag()
    {
        int charm = Give("mossback_charm", 3);
        int resist = Me.ResistPrimal;
        int stamina = Me.MaxStamina;

        Assert.Equal((byte)EquipPosition.Charm1, Run(new EquipTransaction(_world, _alice, new StableId(0), charm)).Detail);
        Assert.Equal((byte)EquipPosition.Charm2, Run(new EquipTransaction(_world, _alice, new StableId(0), charm)).Detail);
        Assert.Equal((resist + 200, stamina + 20), (Me.ResistPrimal, Me.MaxStamina));

        Assert.True(Run(new EquipTransaction(_world, _alice, new StableId(0), charm)).IsApplied);
        Assert.Equal(1, Bag.CountOf(charm));
        Assert.Equal((resist + 200, stamina + 20), (Me.ResistPrimal, Me.MaxStamina));
    }

    [Fact]
    public void Gear_has_a_level_and_creatures_wear_only_what_fits_their_body()
    {
        int vest = Give("leather_vest");
        Assert.Equal(GearRefusal.LevelTooLow, Refusal(new EquipTransaction(_world, _alice, new StableId(0), vest)));
        _world.World.GetComponent<PlayerProgress>(_alice).Level = 3;
        int guard = Me.Guard;
        Assert.True(Run(new EquipTransaction(_world, _alice, new StableId(0), vest)).IsApplied);
        Assert.Equal(guard + 2, Me.Guard);

        Entity owl = Pacted("hollow_owl", Alice);
        Entity calf = Pacted("mossback", Alice);
        int staff = Give("wooden_staff");
        int charm = Give("mossback_charm");
        Give("leather_vest");
        _world.World.GetComponent<CreatureGrowth>(calf).Level = 3;

        Assert.Equal(GearRefusal.CannotWear, Refusal(new EquipTransaction(_world, _alice, IdOf(owl), vest)));
        Assert.Equal(GearRefusal.CannotWear, Refusal(new EquipTransaction(_world, _alice, IdOf(calf), staff)));
        int owlResist = _world.World.GetComponent<Combatant>(owl).ResistPrimal;
        Assert.True(Run(new EquipTransaction(_world, _alice, IdOf(owl), charm)).IsApplied);
        Assert.Equal(owlResist + 100, _world.World.GetComponent<Combatant>(owl).ResistPrimal);
        Assert.True(Run(new EquipTransaction(_world, _alice, IdOf(calf), vest)).IsApplied);

        Entity wild = _world.SpawnCreature(_world.SpeciesIndex("mossback"), Start + new Vector3(0, 0, 4), new Personality(0, 0, 0, 0), group: 3);
        Assert.Equal(IntentRejectReason.InvalidTarget, Run(new EquipTransaction(_world, _alice, IdOf(wild), staff)).Reason);
        Assert.Equal(IntentRejectReason.InvalidTarget, Run(new EquipTransaction(_world, _bob, IdOf(owl), staff)).Reason);
    }

    [Fact]
    public void Gear_stays_on_after_a_level_up_and_through_a_restat()
    {
        int staff = Give("wooden_staff");
        Run(new EquipTransaction(_world, _alice, new StableId(0), staff));
        Assert.True(PlayerStats.Earn(_world, _alice, 1_000));
        int power = Me.Power;

        PlayerStats.Restat(_world, _alice);

        Assert.Equal(power, Me.Power);
        _world.World.GetComponent<Equipment>(_alice) = default;
        PlayerStats.Restat(_world, _alice);
        Assert.True(Me.Power < power);
    }

    [Fact]
    public void Crafting_takes_its_time_then_turns_the_inputs_into_the_output_once()
    {
        int branch = Give("fallen_branch", 4);
        int staff = _world.Data.ItemIndex("wooden_staff");
        int recipe = RecipeOf("wooden_staff");

        var craft = new CraftTransaction(_world, _alice, recipe);
        IntentResult first = _processor.Execute(new IntentId(3, 1), ref craft, _world.Clock.Current.Index);
        IntentResult again = _processor.Execute(new IntentId(3, 1), ref craft, _world.Clock.Current.Index);
        Assert.True(first.IsApplied);
        Assert.Equal(first, again);
        Assert.Equal(GearRefusal.Busy, Refusal(new CraftTransaction(_world, _alice, recipe)));

        Ticks(_world.Data.Recipes[recipe].Ticks / 2);
        Assert.Equal((4, 0), (Bag.CountOf(branch), Bag.CountOf(staff)));
        Ticks(_world.Data.Recipes[recipe].Ticks);

        Assert.Equal((1, 1), (Bag.CountOf(branch), Bag.CountOf(staff)));
        Assert.False(_world.World.Has<Crafting>(_alice));
        Assert.Equal(GearRefusal.MissingInputs, Refusal(new CraftTransaction(_world, _alice, recipe)));
    }

    [Fact]
    public void A_hit_stops_crafting_and_nothing_is_taken()
    {
        int branch = Give("fallen_branch", 3);
        int recipe = RecipeOf("wooden_staff");
        Assert.True(Run(new CraftTransaction(_world, _alice, recipe), _world.Clock.Current.Index).IsApplied);
        Ticks(5);

        Entity fox = _world.SpawnCreature(_world.SpeciesIndex("bogfox"), Start + new Vector3(0, 0, 3), new Personality(0, 0, 0, 0), group: 3);
        _world.Events.Publish(new CombatHit(fox, _alice, 0, 5, 95));
        Ticks(1);
        Ticks(_world.Data.Recipes[recipe].Ticks);

        Assert.False(_world.World.Has<Crafting>(_alice));
        Assert.Equal(3, Bag.CountOf(branch));
        Assert.Equal(0, Bag.CountOf(_world.Data.ItemIndex("wooden_staff")));
    }

    [Fact]
    public void Crafting_needs_the_inputs_and_room_for_the_result()
    {
        int recipe = RecipeOf("wooden_staff");
        Assert.Equal(GearRefusal.MissingInputs, Refusal(new CraftTransaction(_world, _alice, recipe)));
        Assert.Equal(IntentRejectReason.InvalidArguments, Run(new CraftTransaction(_world, _alice, 999)).Reason);

        Give("fallen_branch", 3);
        Give("owl_feather", 30 * (Inventory.Slots - 1));
        Assert.Equal(GearRefusal.InventoryFull, Refusal(new CraftTransaction(_world, _alice, recipe)));
    }

    [Fact]
    public void What_is_worn_is_saved_by_item_id_and_unknown_ids_are_dropped()
    {
        int staff = Give("wooden_staff");
        int charm = Give("mossback_charm");
        Run(new EquipTransaction(_world, _alice, new StableId(0), staff));
        Run(new EquipTransaction(_world, _alice, new StableId(0), charm));

        IReadOnlyList<string> saved = EquipRules.Export(_world.Data, _world.World.GetComponent<Equipment>(_alice));

        Assert.Equal(["wooden_staff", string.Empty, string.Empty, string.Empty, "mossback_charm", string.Empty], saved);
        Assert.Equal(_world.World.GetComponent<Equipment>(_alice), EquipRules.Import(_world.Data, saved));
        Assert.Equal(default, EquipRules.Import(_world.Data, ["dragon_blade"]));
        Assert.Equal(default, EquipRules.Import(_world.Data, null));
    }

    private ref Combatant Me => ref _world.World.GetComponent<Combatant>(_alice);

    private int Give(string item, int count = 1)
    {
        int index = _world.Data.ItemIndex(item);
        Bag.Add(_world.Data, [new ItemCount(index, count)]);
        return index;
    }

    private int RecipeOf(string output) => _world.Data.Recipes.Select((recipe, i) => (recipe, i)).Single(item => item.recipe.Output.Item == output).i;

    private Entity Pacted(string species, ulong owner)
    {
        Entity creature = _world.SpawnCreature(_world.SpeciesIndex(species), Start + new Vector3(3, 0, 3), new Personality(0, 0, 0, 0), group: 7);
        _world.Services.Bonding.Restore(
            _world.World.GetComponent<MindRef>(creature).Handle, new BondState(OwnershipState.Pacted, owner, PactTerms.Of(0), 0, 1, false, 0, 0, false));
        return creature;
    }

    private StableId IdOf(Entity creature) => _world.World.GetComponent<StableId>(creature);

    private void Ticks(int count)
    {
        for (int i = 0; i < count; i++)
        {
            _scheduler.RunTick();
        }
    }

    private GearRefusal Refusal<T>(T transaction)
        where T : struct, ITransaction
    {
        IntentResult result = Run(transaction);
        Assert.Equal(IntentRejectReason.WrongState, result.Reason);
        return (GearRefusal)result.Detail;
    }

    private IntentResult Run<T>(T transaction, ulong now = 0)
        where T : struct, ITransaction
    {
        return _processor.Execute(new IntentId(2, ++_sequence), ref transaction, now);
    }
}

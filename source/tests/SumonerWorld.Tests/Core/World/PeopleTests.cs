using System.Numerics;
using SumonerWorld.Core.Academy;
using SumonerWorld.Core.Data;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Intents;
using SumonerWorld.Core.Transactions;
using SumonerWorld.Core.World;
using SumonerWorld.Net.Client;
using SumonerWorld.Scenarios;

namespace SumonerWorld.Tests.Core.World;

/// <summary>The Vale's people (E5a): where they are, talking to them, and their data.</summary>
public sealed class PeopleTests : IDisposable
{
    private readonly CreatureWorld _world = new(ShippedData.Current, new CreatureWorldSettings(), seed: 71);
    private readonly TransactionProcessor _processor = new(new IntentDeduplicator());
    private uint _sequence;

    public void Dispose() => _world.Dispose();

    [Fact]
    public void The_academys_people_come_first_and_keep_their_indices()
    {
        DataRegistry data = ShippedData.Current;
        Assert.Equal(data.Academy!.Npcs.Select(npc => npc.Id), data.People.Take(data.Academy.Npcs.Count).Select(npc => npc.Id));
        Assert.Contains(data.People, person => person.Id == "marta");
        Assert.Equal(4, data.People.Count(person => person.Role == "mentor"));
    }

    [Fact]
    public void A_walker_rests_at_each_point_walks_between_them_and_starts_over()
    {
        var walker = new NpcDefinition
        {
            Id = "walker",
            Name = "Walker",
            Route = [new RoutePoint { X = 100, Z = 100 }, new RoutePoint { X = 111, Z = 100 }],
        };
        double second = People.TicksPerSecond;

        Assert.Equal((100f, 100f), XZ(People.At(walker, 0)));
        Assert.Equal((100f, 100f), XZ(People.At(walker, (People.RestSeconds - 0.1) * second)));
        Assert.Equal(105.5f, People.At(walker, (People.RestSeconds + 5) * second).X, 2);
        Assert.Equal(90f, People.At(walker, (People.RestSeconds + 5) * second).Facing, 1);
        double cycle = 2 * (People.RestSeconds + (11 / People.WalkSpeed));
        Assert.Equal(People.At(walker, 1234), People.At(walker, 1234 + (cycle * second)));
        var stander = new NpcDefinition { Id = "stander", Name = "Stander", X = 5, Z = 6, Facing = 90 };
        Assert.Equal((5f, 6f, 90f), People.At(stander, 99_999));
    }

    [Fact]
    public void Talking_to_a_person_of_the_vale_needs_them_in_reach_where_they_are_now()
    {
        int marta = People.IndexOf(_world.Data, "marta");
        NpcDefinition person = _world.Data.People[marta];
        Entity near = _world.SpawnPlayerAvatar(1001, new Vector3(person.X + 2, 0, person.Z));
        Entity far = _world.SpawnPlayerAvatar(1002, new Vector3(person.X + 20, 0, person.Z));

        Assert.True(Talk(near, marta).IsApplied);
        Assert.Equal(IntentRejectReason.OutOfRange, Talk(far, marta).Reason);
        Assert.Equal(marta, People.Near(_world.Data, person.X + 1, person.Z, 0, TalkTransaction.Range));
    }

    [Fact]
    public void The_vale_people_greet_first_then_say_their_lines_in_turn()
    {
        NpcDefinition marta = _world.Data.People[People.IndexOf(_world.Data, "marta")];

        Assert.Equal(marta.Greeting, AcademyText.PersonPage(marta, 1).Text);
        Assert.Equal(marta.Lines[0], AcademyText.PersonPage(marta, 2).Text);
        Assert.Equal(marta.Lines[0], AcademyText.PersonPage(marta, 2 + marta.Lines.Count).Text);
    }

    [Fact]
    public void A_person_with_an_unknown_role_or_a_round_outside_the_vale_is_refused()
    {
        List<DataFile> files = [.. ShippedData.Files];
        int index = files.FindIndex(file => file.Path.EndsWith("slice_region.json", StringComparison.Ordinal));
        files[index] = files[index] with
        {
            Content = files[index].Content
                .Replace("\"role\": \"innkeeper\"", "\"role\": \"wizard\"", StringComparison.Ordinal)
                .Replace("{\"x\": 186, \"z\": 300}", "{\"x\": 5, \"z\": 300}", StringComparison.Ordinal),
        };

        var error = Assert.Throws<DataValidationException>(() => DataRegistryLoader.Load(files));

        Assert.Contains(error.Errors, message => message.Contains("person 'marta'", StringComparison.Ordinal));
        Assert.Contains(error.Errors, message => message.Contains("person 'old_tam' stands or walks outside", StringComparison.Ordinal));
    }

    private static (float X, float Z) XZ((float X, float Z, float Facing) at) => (at.X, at.Z);

    private IntentResult Talk(Entity avatar, int npc)
    {
        var transaction = new TalkTransaction(_world, avatar, npc);
        return _processor.Execute(new IntentId(1, ++_sequence), ref transaction, _world.Clock.Current.Index);
    }
}

using System.Numerics;
using SumonerWorld.Core.Movement;
using SumonerWorld.Net.Client;
using SumonerWorld.Net.Messages;

namespace SumonerWorld.Tests.Net;

public class ClientPredictionTests
{
    private static readonly MovementModel Movement = new(new MovementSettings { SpeedPerTick = 0.2f, BoundsMax = new Vector3(512, 0, 512) });
    private static readonly MoveInput East = new(1000, 0);

    [Fact]
    public void Inputs_move_the_character_at_once_and_an_agreeing_server_changes_nothing()
    {
        ClientPrediction prediction = Started(new Vector3(100, 0, 100));
        for (int i = 0; i < 5; i++)
        {
            prediction.Record(East, 0);
        }

        Vector3 serverAfterTwo = Movement.Advance(Movement.Advance(new Vector3(100, 0, 100), East), East);
        Correction correction = prediction.Reconcile(2, serverAfterTwo);

        Assert.Equal(Correction.None, correction);
        Assert.Equal(3, prediction.PendingCount);
        Assert.Equal(Movement.Advance(Movement.Advance(Movement.Advance(serverAfterTwo, East), East), East), prediction.Position);
    }

    [Theory]
    [InlineData(0.3f, Correction.Snapped)]
    [InlineData(1.5f, Correction.Smoothed)]
    [InlineData(5f, Correction.Teleported)]
    public void Errors_are_snapped_smoothed_or_teleported_by_size(float error, Correction expected)
    {
        ClientPrediction prediction = Started(new Vector3(100, 0, 100));
        prediction.Record(East, 0);
        Vector3 before = prediction.Position;

        Correction correction = prediction.Reconcile(1, before + new Vector3(0, 0, error));

        Assert.Equal(expected, correction);
        Assert.Equal(before + new Vector3(0, 0, error), prediction.Position);
        if (expected == Correction.Smoothed)
        {
            Assert.Equal(before, prediction.RenderPosition);
            for (int i = 0; i < ClientPrediction.SmoothTicks; i++)
            {
                prediction.Record(MoveInput.None, 0);
            }
        }

        Assert.Equal(prediction.Position, prediction.RenderPosition);
    }

    [Fact]
    public void Old_acks_are_ignored_and_the_newest_inputs_are_offered_for_sending()
    {
        ClientPrediction prediction = Started(Vector3.Zero);
        for (int i = 0; i < ClientPrediction.Capacity + 10; i++)
        {
            prediction.Record(East, 0);
        }

        var newest = new PlayerInput[InputBatch.MaxInputs];
        int count = prediction.CopyNewest(newest);
        prediction.Reconcile(70, prediction.Position);
        Vector3 position = prediction.Position;

        Assert.Equal(ClientPrediction.Capacity, 74 - 10);
        Assert.Equal(InputBatch.MaxInputs, count);
        Assert.Equal(prediction.LastSequence, newest[^1].Sequence);
        Assert.Equal(Correction.None, prediction.Reconcile(60, Vector3.Zero));
        Assert.Equal(position, prediction.Position);
    }

    [Fact]
    public void A_dodge_press_dashes_like_the_server_and_a_replay_repeats_it()
    {
        var prediction = new ClientPrediction(Movement) { Dodge = new DodgePrediction(Ticks: 9, SpeedPercent: 250) };
        var start = new Vector3(100, 0, 100);
        prediction.Reconcile(0, start);

        prediction.Record(East, PlayerButtons.Dodge);
        for (int i = 0; i < 11; i++)
        {
            prediction.Record(East, PlayerButtons.Dodge); // held: only the press counts
        }

        Vector3 expected = start;
        for (int i = 0; i < 12; i++)
        {
            expected = i < 9 ? Movement.Dash(expected, Vector3.UnitX, 250) : Movement.Advance(expected, East);
        }

        Assert.Equal(expected.X, prediction.Position.X, 4);
        Assert.Equal(Correction.None, prediction.Reconcile(3, Movement.Dash(Movement.Dash(Movement.Dash(start, Vector3.UnitX, 250), Vector3.UnitX, 250), Vector3.UnitX, 250)));
    }

    [Fact]
    public void A_frozen_character_stays_where_it_is()
    {
        ClientPrediction prediction = Started(new Vector3(100, 0, 100));
        prediction.Frozen = true;
        prediction.Record(East, 0);
        prediction.Record(East, PlayerButtons.Dodge);

        Assert.Equal(new Vector3(100, 0, 100), prediction.Position);
    }

    [Fact]
    public void Predicting_before_the_first_snapshot_is_a_bug()
    {
        Assert.Throws<InvalidOperationException>(() => new ClientPrediction(Movement).Record(East, 0));
    }

    private static ClientPrediction Started(Vector3 position)
    {
        var prediction = new ClientPrediction(Movement);
        prediction.Reconcile(0, position);
        return prediction;
    }
}

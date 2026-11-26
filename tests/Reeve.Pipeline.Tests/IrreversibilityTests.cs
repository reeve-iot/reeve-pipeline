using FluentAssertions;
using Microsoft.Extensions.Time.Testing;

namespace Reeve.Pipeline.Tests;

public sealed class IrreversibilityTests
{
    private const string Reason = "a certified N+1 may have forward-migrated data";

    private static readonly DateTimeOffset Now = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_point_of_no_return_is_answerable_before_anything_runs()
    {
        var pipeline = Pipeline<TestContext>.Resolve(
        [
            new ScriptedStage("a", StagePlacement.First),
            new ScriptedStage("b", StagePlacement.After("a"))
            {
                Reversibility = StageReversibility.IrreversibleOnceEntered(Reason),
            },
        ]);

        pipeline.FirstIrreversibleStageId.Should().Be("b");
        pipeline.FirstIrreversibleReason.Should().Be(
            Reason,
            "an operator learning about a point of no return by being past it is the defect this fixes");
    }

    [Fact]
    public void With_two_irreversible_stages_it_reports_the_earlier_one()
    {
        var pipeline = Pipeline<TestContext>.Resolve(
        [
            new ScriptedStage("a", StagePlacement.First),
            new ScriptedStage("b", StagePlacement.After("a"))
            {
                Reversibility = StageReversibility.IrreversibleOnceEntered("the earlier one"),
            },
            new ScriptedStage("c", StagePlacement.After("b"))
            {
                Reversibility = StageReversibility.IrreversibleOnceEntered("the later one"),
            },
        ]);

        pipeline.FirstIrreversibleStageId.Should().Be("b");
        pipeline.FirstIrreversibleReason.Should().Be(
            "the earlier one",
            "a warning naming the LAST point of no return would understate when the run stops being reversible");
    }

    [Fact]
    public void A_pipeline_that_can_always_unwind_says_so()
    {
        var pipeline = Pipeline<TestContext>.Resolve([new ScriptedStage("a", StagePlacement.First)]);

        pipeline.FirstIrreversibleStageId.Should().BeNull();
        pipeline.FirstIrreversibleReason.Should().BeNull();
    }

    [Fact]
    public async Task A_failure_after_an_irreversible_stage_refuses_to_unwind_and_quotes_the_reason()
    {
        var a = new ScriptedStage("a", StagePlacement.First);
        var b = new ScriptedStage("b", StagePlacement.After("a"))
        {
            Reversibility = StageReversibility.IrreversibleOnceEntered(Reason),
        };
        var c = new ScriptedStage("c", StagePlacement.After("b")).ThenUp(StageOutcome.Failed("the peer is gone"));
        var runner = new PipelineRunner<TestContext>(
            Pipeline<TestContext>.Resolve([a, b, c]), new FakeTimeProvider(Now));

        var journal = PipelineJournal.New();
        PipelineTick tick = null!;
        for (var i = 0; i < 3; i++)
        {
            tick = await runner.AdvanceAsync(journal, new TestContext(), CancellationToken.None);
            journal = tick.Journal;
        }

        tick.Result.Should().Be(PipelineTickResult.Failed);
        tick.Detail.Should().Contain("the peer is gone").And.Contain(Reason);
        journal.State.Should().Be(PipelineState.Failed);
        a.Calls.Should().Equal("up");
        b.Calls.Should().Equal("up");
        c.Calls.Should().Equal(["up"], "nothing is undone once the run is forward-only");
    }

    [Fact]
    public void CanCompensate_refuses_and_names_the_stage()
    {
        var pipeline = Pipeline<TestContext>.Resolve(
        [
            new ScriptedStage("a", StagePlacement.First)
            {
                Reversibility = StageReversibility.IrreversibleOnceEntered(Reason),
            },
        ]);
        var entered = PipelineJournal.New() with
        {
            Entries = [new StageEntry("a", Now, PipelineDirection.Forward, StageEntryState.Done, null)],
        };

        var can = PipelineRunner<TestContext>.CanCompensate(entered, pipeline, out var refusal);

        can.Should().BeFalse();
        refusal.Should().Contain("'a'").And.Contain(Reason);
    }

    [Fact]
    public void An_irreversible_stage_not_yet_entered_refuses_nothing()
    {
        var pipeline = Pipeline<TestContext>.Resolve(
        [
            new ScriptedStage("a", StagePlacement.First),
            new ScriptedStage("b", StagePlacement.After("a"))
            {
                Reversibility = StageReversibility.IrreversibleOnceEntered(Reason),
            },
        ]);
        var entered = PipelineJournal.New() with
        {
            Entries = [new StageEntry("a", Now, PipelineDirection.Forward, StageEntryState.Done, null)],
        };

        PipelineRunner<TestContext>.CanCompensate(entered, pipeline, out var refusal).Should().BeTrue();
        refusal.Should().BeNull();
    }
}

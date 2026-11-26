using FluentAssertions;
using Microsoft.Extensions.Time.Testing;

namespace Reeve.Pipeline.Tests;

public sealed class ForwardWalkTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);

    private static (PipelineRunner<TestContext> Runner, ScriptedStage[] Stages) ThreeStages(
        params StageOutcome[][] scripts)
    {
        var stages = new[]
        {
            new ScriptedStage("a", StagePlacement.First),
            new ScriptedStage("b", StagePlacement.After("a")),
            new ScriptedStage("c", StagePlacement.After("b")),
        };

        for (var i = 0; i < scripts.Length && i < stages.Length; i++)
        {
            stages[i].ThenUp(scripts[i]);
        }

        var pipeline = Pipeline<TestContext>.Resolve(stages);
        return (new PipelineRunner<TestContext>(pipeline, new FakeTimeProvider(Now)), stages);
    }

    private static async Task<(PipelineJournal Journal, List<PipelineTick> Ticks)> RunAsync(
        PipelineRunner<TestContext> runner, int ticks, PipelineJournal? from = null)
    {
        var journal = from ?? PipelineJournal.New();
        var seen = new List<PipelineTick>();

        for (var i = 0; i < ticks; i++)
        {
            var tick = await runner.AdvanceAsync(journal, new TestContext(), CancellationToken.None);
            journal = tick.Journal;
            seen.Add(tick);
        }

        return (journal, seen);
    }

    [Fact]
    public async Task One_tick_calls_exactly_one_stage()
    {
        var (runner, stages) = ThreeStages();

        await runner.AdvanceAsync(PipelineJournal.New(), new TestContext(), CancellationToken.None);

        stages[0].Calls.Should().Equal("up");
        stages[1].Calls.Should().BeEmpty("a tick is one step, never a run to completion");
        stages[2].Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Three_stages_reach_Completed_in_order()
    {
        var (runner, stages) = ThreeStages();

        var (journal, ticks) = await RunAsync(runner, 3);

        ticks.Select(t => t.Result).Should().Equal(
            PipelineTickResult.Advanced, PipelineTickResult.Advanced, PipelineTickResult.Completed);
        journal.State.Should().Be(PipelineState.Completed);
        journal.Entries.Select(e => e.StageId).Should().Equal("a", "b", "c");
        journal.Entries.Should().OnlyContain(e => e.State == StageEntryState.Done);
        stages.Should().OnlyContain(s => s.Calls.Count == 1);
    }

    [Fact]
    public async Task A_working_stage_is_entered_once_and_asked_again()
    {
        var (runner, stages) = ThreeStages(
            [StageOutcome.Working("waiting on one"), StageOutcome.Working("waiting on two"), StageOutcome.Done]);

        var (journal, ticks) = await RunAsync(runner, 3);

        ticks.Select(t => t.Result).Should().Equal(
            PipelineTickResult.Waiting, PipelineTickResult.Waiting, PipelineTickResult.Advanced);
        stages[0].Calls.Should().Equal("up", "up", "up");
        journal.Entries.Should().ContainSingle(e => e.StageId == "a",
            "re-asking a working stage is not re-entering it");
    }

    [Fact]
    public async Task A_working_entry_carries_the_latest_detail()
    {
        var (runner, _) = ThreeStages([StageOutcome.Working("first"), StageOutcome.Working("second")]);

        var (journal, ticks) = await RunAsync(runner, 2);

        ticks[^1].Detail.Should().Be("second");
        journal.Entries.Single().Detail.Should().Be("second");
    }

    [Fact]
    public async Task Ticking_a_terminal_journal_changes_nothing()
    {
        var (runner, stages) = ThreeStages();
        var done = PipelineJournal.New() with { State = PipelineState.Completed };

        var tick = await runner.AdvanceAsync(done, new TestContext(), CancellationToken.None);

        tick.Journal.Should().BeSameAs(done);
        tick.Result.Should().Be(PipelineTickResult.Completed);
        stages.Should().OnlyContain(s => s.Calls.Count == 0,
            "a caller whose row and whose loop disagree by one tick is normal, not an error");
    }

    [Fact]
    public async Task A_stage_id_the_pipeline_does_not_know_is_reported_not_thrown()
    {
        var (runner, _) = ThreeStages();
        var stranded = PipelineJournal.New() with
        {
            Entries = [new StageEntry("gone", Now, PipelineDirection.Forward, StageEntryState.Done, null)],
        };

        var tick = await runner.AdvanceAsync(stranded, new TestContext(), CancellationToken.None);

        tick.Result.Should().Be(PipelineTickResult.UnknownStage);
        tick.StageId.Should().Be("gone");
        tick.Journal.Should().BeSameAs(stranded,
            "what a removed stage means for a stored row is the caller's decision, not this library's");
    }

    [Fact]
    public async Task Advancing_twice_from_the_same_journal_gives_the_same_journal()
    {
        var (runner, _) = ThreeStages();
        var journal = PipelineJournal.New();

        var once = await runner.AdvanceAsync(journal, new TestContext(), CancellationToken.None);
        var twice = await runner.AdvanceAsync(journal, new TestContext(), CancellationToken.None);

        twice.Journal.Should().BeEquivalentTo(once.Journal,
            "a caller that failed to persist a tick replays it, and that must be safe");
    }
}

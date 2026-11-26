using FluentAssertions;
using Microsoft.Extensions.Time.Testing;

namespace Reeve.Pipeline.Tests;

public sealed class CompensationTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);

    private static async Task<(PipelineJournal Journal, List<PipelineTickResult> Results)> RunAsync(
        PipelineRunner<TestContext> runner, int ticks)
    {
        var journal = PipelineJournal.New();
        var results = new List<PipelineTickResult>();

        for (var i = 0; i < ticks; i++)
        {
            var tick = await runner.AdvanceAsync(journal, new TestContext(), CancellationToken.None);
            journal = tick.Journal;
            results.Add(tick.Result);
        }

        return (journal, results);
    }

    private static PipelineRunner<TestContext> Runner(params IStage<TestContext>[] stages) =>
        new(Pipeline<TestContext>.Resolve(stages), new FakeTimeProvider(Now));

    [Fact]
    public async Task A_failure_unwinds_the_entered_stages_newest_first()
    {
        var log = new List<string>();
        var a = new ScriptedStage("a", StagePlacement.First) { SharedLog = log };
        var b = new ScriptedStage("b", StagePlacement.After("a")) { SharedLog = log };
        var c = new ScriptedStage("c", StagePlacement.After("b")) { SharedLog = log };
        c.ThenUp(StageOutcome.Failed("no"));

        var (journal, results) = await RunAsync(Runner(a, b, c), 7);

        journal.State.Should().Be(PipelineState.Compensated);
        results[^1].Should().Be(PipelineTickResult.Compensated);
        log.Should().Equal(
            ["a:up", "b:up", "c:up", "c:down", "b:down", "a:down"],
            "the unwind is newest-first, and the stage that failed part-way is compensated too");
    }

    [Fact]
    public async Task A_stage_never_entered_is_never_compensated()
    {
        var a = new ScriptedStage("a", StagePlacement.First).ThenUp(StageOutcome.Failed("no"));
        var b = new ScriptedStage("b", StagePlacement.After("a"));

        var (journal, _) = await RunAsync(Runner(a, b), 4);

        journal.State.Should().Be(PipelineState.Compensated);
        a.Calls.Should().Equal("up", "down");
        b.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task An_undo_that_fails_ends_the_run_and_stops_unwinding()
    {
        var a = new ScriptedStage("a", StagePlacement.First);
        var b = new ScriptedStage("b", StagePlacement.After("a"))
        {
            DownFallback = StageOutcome.Failed("the volume is gone"),
        };
        b.ThenUp(StageOutcome.Failed("no"));

        var (journal, results) = await RunAsync(Runner(a, b), 5);

        journal.State.Should().Be(PipelineState.Failed);
        results.Should().Contain(PipelineTickResult.Failed);
        a.Calls.Should().Equal(["up"], "unwinding stops at the undo that failed; it does not skip past it");
    }

    [Fact]
    public async Task A_working_undo_is_asked_again_without_being_re_entered()
    {
        var a = new ScriptedStage("a", StagePlacement.First);
        a.ThenUp(StageOutcome.Failed("no")).ThenDown(StageOutcome.Working("draining"), StageOutcome.Done);

        var (journal, _) = await RunAsync(Runner(a), 4);

        a.Calls.Should().Equal("up", "down", "down");
        journal.State.Should().Be(PipelineState.Compensated);
        journal.Entries.Count(e => e.Direction == PipelineDirection.Compensating).Should().Be(1);
    }
}

using FluentAssertions;
using Microsoft.Extensions.Time.Testing;

namespace Reeve.Pipeline.Tests;

public sealed class BudgetTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);

    private static (PipelineRunner<TestContext> Runner, FakeTimeProvider Clock, ScriptedStage Stage) One(
        StageBudget budget)
    {
        var stage = new ScriptedStage("a", StagePlacement.First) { Budget = budget }
            .ThenUp(StageOutcome.Working("one"), StageOutcome.Working("two"), StageOutcome.Working("three"));
        var clock = new FakeTimeProvider(Now);
        return (new PipelineRunner<TestContext>(Pipeline<TestContext>.Resolve([stage]), clock), clock, stage);
    }

    [Fact]
    public async Task Inside_its_budget_a_working_stage_is_simply_waiting()
    {
        var (runner, clock, _) = One(
            StageBudget.Of(TimeSpan.FromMinutes(5), StageTimeoutPolicy.KeepWaitingAndReport));

        var first = await runner.AdvanceAsync(PipelineJournal.New(), new TestContext(), CancellationToken.None);
        clock.Advance(TimeSpan.FromMinutes(1));
        var second = await runner.AdvanceAsync(first.Journal, new TestContext(), CancellationToken.None);

        second.Result.Should().Be(PipelineTickResult.Waiting);
    }

    [Fact]
    public async Task Past_its_budget_KeepWaitingAndReport_keeps_waiting_and_says_so()
    {
        var (runner, clock, stage) = One(
            StageBudget.Of(TimeSpan.FromMinutes(5), StageTimeoutPolicy.KeepWaitingAndReport));

        var first = await runner.AdvanceAsync(PipelineJournal.New(), new TestContext(), CancellationToken.None);
        clock.Advance(TimeSpan.FromMinutes(6));
        var second = await runner.AdvanceAsync(first.Journal, new TestContext(), CancellationToken.None);

        second.Result.Should().Be(PipelineTickResult.WaitingOverBudget);
        second.Journal.State.Should().Be(PipelineState.Running, "reporting is not failing");
        stage.Calls.Should().Equal("up", "up");
    }

    [Fact]
    public async Task Past_its_budget_FailAndCompensate_fails_and_names_the_budget()
    {
        var (runner, clock, stage) = One(
            StageBudget.Of(TimeSpan.FromMinutes(5), StageTimeoutPolicy.FailAndCompensate));

        var first = await runner.AdvanceAsync(PipelineJournal.New(), new TestContext(), CancellationToken.None);
        clock.Advance(TimeSpan.FromMinutes(6));
        var second = await runner.AdvanceAsync(first.Journal, new TestContext(), CancellationToken.None);

        second.Journal.Direction.Should().Be(PipelineDirection.Compensating);
        second.Journal.Entries.Single().Detail.Should().Contain("budget");
        stage.Calls.Should().Equal("up", "up");
    }

    [Fact]
    public async Task The_budget_runs_from_the_journal_not_from_the_process()
    {
        var (runner, clock, _) = One(
            StageBudget.Of(TimeSpan.FromMinutes(5), StageTimeoutPolicy.KeepWaitingAndReport));
        var first = await runner.AdvanceAsync(PipelineJournal.New(), new TestContext(), CancellationToken.None);
        clock.Advance(TimeSpan.FromMinutes(6));

        var stage = new ScriptedStage("a", StagePlacement.First)
        {
            Budget = StageBudget.Of(TimeSpan.FromMinutes(5), StageTimeoutPolicy.KeepWaitingAndReport),
        }.ThenUp(StageOutcome.Working("still"));
        var failover = new PipelineRunner<TestContext>(Pipeline<TestContext>.Resolve([stage]), clock);

        var after = await failover.AdvanceAsync(first.Journal, new TestContext(), CancellationToken.None);

        after.Result.Should().Be(
            PipelineTickResult.WaitingOverBudget,
            "a budget that reset on failover would never expire at the moment it is meant to");
    }
}

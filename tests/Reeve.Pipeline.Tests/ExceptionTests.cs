using FluentAssertions;
using Microsoft.Extensions.Time.Testing;

namespace Reeve.Pipeline.Tests;

public sealed class ExceptionTests
{
    private sealed class ThrowingStage(params StageOutcome?[] script) : IStage<TestContext>
    {
        private readonly Queue<StageOutcome?> _script = new(script);

        public string Id => "throws";

        public StagePlacement Placement => StagePlacement.First;

        public StageReversibility Reversibility => StageReversibility.Reversible;

        public StageBudget Budget { get; } =
            StageBudget.Of(TimeSpan.FromMinutes(1), StageTimeoutPolicy.FailAndCompensate);

        public int UpCalls { get; private set; }

        public ValueTask<StageOutcome> UpAsync(TestContext context, CancellationToken cancellationToken)
        {
            UpCalls++;
            return _script.Dequeue() is { } outcome
                ? ValueTask.FromResult(outcome)
                : throw new InvalidOperationException("boom");
        }

        public ValueTask<StageOutcome> DownAsync(TestContext context, CancellationToken cancellationToken) =>
            ValueTask.FromResult(StageOutcome.Done);
    }

    [Fact]
    public async Task A_throwing_stage_propagates_records_nothing_and_its_budget_does_not_fire()
    {
        var stage = new ThrowingStage(StageOutcome.Working("started"), null, null, StageOutcome.Done);
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero));
        var runner = new PipelineRunner<TestContext>(Pipeline<TestContext>.Resolve([stage]), time);

        var working = await runner.AdvanceAsync(PipelineJournal.New(), new TestContext(), CancellationToken.None);
        working.Result.Should().Be(PipelineTickResult.Waiting);

        time.Advance(TimeSpan.FromHours(1));
        var tick = () => runner.AdvanceAsync(working.Journal, new TestContext(), CancellationToken.None).AsTask();
        await tick.Should().ThrowAsync<InvalidOperationException>(
            "an exception is not a Working answer, so the spent budget is not applied to it");
        await tick.Should().ThrowAsync<InvalidOperationException>();

        var done = await runner.AdvanceAsync(working.Journal, new TestContext(), CancellationToken.None);

        stage.UpCalls.Should().Be(4);
        done.Result.Should().Be(PipelineTickResult.Completed);
        done.Journal.Entries.Should().ContainSingle().Which.State.Should().Be(StageEntryState.Done);
    }
}

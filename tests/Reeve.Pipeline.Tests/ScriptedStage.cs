namespace Reeve.Pipeline.Tests;

public sealed record TestContext(string Name = "ctx");

public sealed class ScriptedStage(string id, StagePlacement placement) : IStage<TestContext>
{
    private readonly Queue<StageOutcome> _up = new();
    private readonly Queue<StageOutcome> _down = new();

    public List<string>? SharedLog { get; init; }

    public string Id { get; } = id;

    public StagePlacement Placement { get; } = placement;

    public StageReversibility Reversibility { get; init; } = StageReversibility.Reversible;

    public StageBudget Budget { get; init; } = StageBudget.None;

    public List<string> Calls { get; } = [];

    public StageOutcome UpFallback { get; init; } = StageOutcome.Done;

    public StageOutcome DownFallback { get; init; } = StageOutcome.Done;

    public ScriptedStage ThenUp(params StageOutcome[] outcomes)
    {
        foreach (var outcome in outcomes)
        {
            _up.Enqueue(outcome);
        }

        return this;
    }

    public ScriptedStage ThenDown(params StageOutcome[] outcomes)
    {
        foreach (var outcome in outcomes)
        {
            _down.Enqueue(outcome);
        }

        return this;
    }

    public ValueTask<StageOutcome> UpAsync(TestContext context, CancellationToken cancellationToken)
    {
        Calls.Add("up");
        SharedLog?.Add($"{Id}:up");
        return ValueTask.FromResult(_up.Count > 0 ? _up.Dequeue() : UpFallback);
    }

    public ValueTask<StageOutcome> DownAsync(TestContext context, CancellationToken cancellationToken)
    {
        Calls.Add("down");
        SharedLog?.Add($"{Id}:down");
        return ValueTask.FromResult(_down.Count > 0 ? _down.Dequeue() : DownFallback);
    }
}

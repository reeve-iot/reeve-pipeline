namespace Reeve.Pipeline;

/// <summary>What exceeding a stage's budget means.</summary>
public enum StageTimeoutPolicy
{
    /// <summary>Treat it as a failure and unwind.</summary>
    FailAndCompensate = 0,

    /// <summary>Keep waiting, and report that the budget is spent.</summary>
    KeepWaitingAndReport = 1,
}

/// <summary>How long a stage may keep answering <see cref="StageOutcomeKind.Working"/>, and what happens
/// when it has. The limit is measured from the instant the journal records the stage as entered, so it
/// survives the caller restarting mid-stage.</summary>
public sealed record StageBudget
{
    private StageBudget(TimeSpan? limit, StageTimeoutPolicy policy)
    {
        Limit = limit;
        Policy = policy;
    }

    /// <summary>The limit, or null when the stage may work indefinitely.</summary>
    public TimeSpan? Limit { get; }

    /// <summary>What exceeding <see cref="Limit"/> means. Meaningless when the limit is null.</summary>
    public StageTimeoutPolicy Policy { get; }

    /// <summary>No limit.</summary>
    public static StageBudget None { get; } = new(null, StageTimeoutPolicy.KeepWaitingAndReport);

    /// <summary>A limit, and what exceeding it means.</summary>
    public static StageBudget Of(TimeSpan limit, StageTimeoutPolicy policy)
    {
        if (limit <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), limit, "A budget limit must be positive.");
        }

        return new StageBudget(limit, policy);
    }
}

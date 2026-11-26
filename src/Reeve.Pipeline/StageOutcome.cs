namespace Reeve.Pipeline;

/// <summary>What a stage call answered.</summary>
public enum StageOutcomeKind
{
    /// <summary>Still working. The next tick asks again.</summary>
    Working = 0,

    /// <summary>Finished. The walk moves on.</summary>
    Done = 1,

    /// <summary>Could not finish, and said why.</summary>
    Failed = 2,
}

/// <summary>One stage call's answer. <see cref="Detail"/> is the caller's to surface; this library only
/// carries it into the journal and out through the tick.</summary>
public readonly record struct StageOutcome(StageOutcomeKind Kind, string? Detail)
{
    /// <summary>Still working, optionally saying what it is waiting for.</summary>
    public static StageOutcome Working(string? detail = null) => new(StageOutcomeKind.Working, detail);

    /// <summary>Finished.</summary>
    public static StageOutcome Done { get; } = new(StageOutcomeKind.Done, null);

    /// <summary>Could not finish. The reason is required; a blank one is refused.</summary>
    public static StageOutcome Failed(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A failed outcome must say why.", nameof(reason));
        }

        return new StageOutcome(StageOutcomeKind.Failed, reason);
    }
}

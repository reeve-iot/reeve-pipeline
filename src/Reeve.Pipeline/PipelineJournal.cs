namespace Reeve.Pipeline;

/// <summary>Which way the walk is going.</summary>
public enum PipelineDirection
{
    /// <summary>Carrying stages out.</summary>
    Forward = 0,

    /// <summary>Undoing the ones that were entered, newest first.</summary>
    Compensating = 1,
}

/// <summary>Where a run stands.</summary>
public enum PipelineState
{
    /// <summary>Still going.</summary>
    Running = 0,

    /// <summary>Every forward stage finished.</summary>
    Completed = 1,

    /// <summary>The backwards walk reached the beginning.</summary>
    Compensated = 2,

    /// <summary>A stage failed and the run could not be unwound, or an undo itself failed.</summary>
    Failed = 3,
}

/// <summary>How one stage's call went.</summary>
public enum StageEntryState
{
    /// <summary>Still working; the next tick asks the same stage again.</summary>
    Working = 0,

    /// <summary>Finished.</summary>
    Done = 1,

    /// <summary>Failed, with the reason in the entry's detail.</summary>
    Failed = 2,
}

/// <summary>One stage's progress, in one direction. A stage entered forward and later undone has two.</summary>
public sealed record StageEntry(
    string StageId,
    DateTimeOffset EnteredAtUtc,
    PipelineDirection Direction,
    StageEntryState State,
    string? Detail);

/// <summary>The whole of a run's durable state. The caller stores it, in its own row and its own format;
/// this library ships no serialiser.</summary>
public sealed record PipelineJournal(
    IReadOnlyList<StageEntry> Entries,
    PipelineDirection Direction,
    PipelineState State)
{
    /// <summary>An empty run, going forward.</summary>
    public static PipelineJournal New() => new([], PipelineDirection.Forward, PipelineState.Running);

    /// <summary>Whether this run has an outcome. Ticking a terminal journal is a no-op, not an error.</summary>
    public bool IsTerminal => State != PipelineState.Running;
}

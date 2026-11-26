namespace Reeve.Pipeline;

/// <summary>What one tick did.</summary>
public enum PipelineTickResult
{
    /// <summary>A stage finished and the walk moved on.</summary>
    Advanced = 0,

    /// <summary>A stage is still working, or a forward failure has just turned the run around to compensate.
    /// Tick again when the caller's loop next runs.</summary>
    Waiting = 1,

    /// <summary>Still working, and past its budget, under <see cref="StageTimeoutPolicy.KeepWaitingAndReport"/>.
    /// The run continues. The library logs nothing, so this result is the only report.</summary>
    WaitingOverBudget = 2,

    /// <summary>Every forward stage is done.</summary>
    Completed = 3,

    /// <summary>The backwards walk reached the beginning.</summary>
    Compensated = 4,

    /// <summary>A stage failed and could not be compensated, or an undo failed.</summary>
    Failed = 5,

    /// <summary>The journal names a stage this pipeline does not have. Nothing was called and nothing
    /// changed: what that means for a stored row is the caller's to decide.</summary>
    UnknownStage = 6,
}

/// <summary>One tick's answer. <see cref="Journal"/> is what the caller persists.</summary>
public sealed record PipelineTick(
    PipelineJournal Journal,
    PipelineTickResult Result,
    string? StageId,
    string? Detail);

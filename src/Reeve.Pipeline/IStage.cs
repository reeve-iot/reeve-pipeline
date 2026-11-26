namespace Reeve.Pipeline;

/// <summary>One step of a pipeline, with the action that carries it out and the action that undoes it.
///
/// <para>Both must be idempotent: a tick whose journal the caller did not persist is replayed.</para>
///
/// <para><see cref="DownAsync"/> is also called on a stage that failed part-way through
/// <see cref="UpAsync"/>, so it must be safe on a stage that never began.</para>
///
/// <para>Report a failure by returning <see cref="StageOutcome.Failed"/>. An exception propagates out of
/// <see cref="PipelineRunner{TContext}.AdvanceAsync"/> and nothing is recorded.</para>
/// </summary>
/// <typeparam name="TContext">Whatever the caller's stages need. This library never inspects it.</typeparam>
public interface IStage<TContext>
{
    /// <summary>Stable, and written into the journal. Renaming one strands runs sitting on it.</summary>
    string Id { get; }

    /// <summary>Where this stage sits.</summary>
    StagePlacement Placement { get; }

    /// <summary>Whether entering this stage forfeits the run's ability to unwind.</summary>
    StageReversibility Reversibility { get; }

    /// <summary>How long this stage may keep working, and what exceeding that means.</summary>
    StageBudget Budget { get; }

    /// <summary>Carries the stage out. Returns rather than blocks: still working is an answer.</summary>
    ValueTask<StageOutcome> UpAsync(TContext context, CancellationToken cancellationToken);

    /// <summary>Undoes the stage. Must be idempotent and safe on a stage that never began.</summary>
    ValueTask<StageOutcome> DownAsync(TContext context, CancellationToken cancellationToken);
}

namespace Reeve.Pipeline;

/// <summary>Walks a <see cref="Pipeline{TContext}"/> one step per call. It starts nothing, stores nothing and
/// waits for nothing: the caller owns the loop and the journal.</summary>
/// <typeparam name="TContext">Whatever the caller's stages need.</typeparam>
public sealed class PipelineRunner<TContext>(Pipeline<TContext> pipeline, TimeProvider time)
{
    private readonly Pipeline<TContext> _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    private readonly TimeProvider _time = time ?? throw new ArgumentNullException(nameof(time));

    /// <summary>Makes at most one stage call and returns a new journal. Never blocks on a stage: one that is
    /// still working says so, and the next tick asks again.</summary>
    public async ValueTask<PipelineTick> AdvanceAsync(
        PipelineJournal journal, TContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(journal);

        if (journal.IsTerminal)
        {
            return new PipelineTick(journal, Terminal(journal.State), null, null);
        }

        var position = Position(journal);
        if (position is null)
        {
            var state = journal.Direction == PipelineDirection.Forward
                ? PipelineState.Completed
                : PipelineState.Compensated;
            return new PipelineTick(journal with { State = state }, Terminal(state), null, null);
        }

        var stage = _pipeline.Find(position);
        if (stage is null)
        {
            return new PipelineTick(journal, PipelineTickResult.UnknownStage, position, null);
        }

        var outcome = journal.Direction == PipelineDirection.Forward
            ? await stage.UpAsync(context, cancellationToken).ConfigureAwait(false)
            : await stage.DownAsync(context, cancellationToken).ConfigureAwait(false);

        return Fold(journal, stage, outcome);
    }

    /// <summary>Whether a run holding this journal may be unwound, and why not when it may not.</summary>
    public static bool CanCompensate(PipelineJournal journal, Pipeline<TContext> pipeline, out string? refusal)
    {
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(pipeline);

        foreach (var id in EnteredForward(journal))
        {
            var stage = pipeline.Find(id);
            if (stage is null || stage.Reversibility.IsReversible)
            {
                continue;
            }

            refusal = $"stage '{id}' is irreversible once entered: {stage.Reversibility.OperatorReason}";
            return false;
        }

        refusal = null;
        return true;
    }

    private static PipelineTickResult Terminal(PipelineState state) => state switch
    {
        PipelineState.Completed => PipelineTickResult.Completed,
        PipelineState.Compensated => PipelineTickResult.Compensated,
        _ => PipelineTickResult.Failed,
    };

    private static IEnumerable<string> EnteredForward(PipelineJournal journal) =>
        journal.Entries
            .Where(e => e.Direction == PipelineDirection.Forward)
            .Select(e => e.StageId)
            .Distinct(StringComparer.Ordinal);

    private static (IReadOnlyList<StageEntry> Entries, DateTimeOffset EnteredAt) Upsert(
        PipelineJournal journal,
        string stageId,
        PipelineDirection direction,
        StageEntryState state,
        string? detail,
        DateTimeOffset now)
    {
        var entries = journal.Entries.ToList();
        var last = entries.Count > 0 ? entries[^1] : null;

        if (last is not null
            && string.Equals(last.StageId, stageId, StringComparison.Ordinal)
            && last.Direction == direction)
        {
            entries[^1] = last with { State = state, Detail = detail };
            return (entries, last.EnteredAtUtc);
        }

        entries.Add(new StageEntry(stageId, now, direction, state, detail));
        return (entries, now);
    }

    private string? Position(PipelineJournal journal)
    {
        var last = journal.Entries.Count > 0 ? journal.Entries[^1] : null;

        if (journal.Direction == PipelineDirection.Forward)
        {
            if (last is null)
            {
                return _pipeline.Order[0];
            }

            if (last.State == StageEntryState.Working)
            {
                return last.StageId;
            }

            var at = IndexOf(last.StageId);

            if (at < 0)
            {
                return last.StageId;
            }

            return at + 1 < _pipeline.Order.Count ? _pipeline.Order[at + 1] : null;
        }

        if (last is { Direction: PipelineDirection.Compensating, State: StageEntryState.Working })
        {
            return last.StageId;
        }

        var undone = journal.Entries
            .Where(e => e.Direction == PipelineDirection.Compensating && e.State == StageEntryState.Done)
            .Select(e => e.StageId)
            .ToHashSet(StringComparer.Ordinal);

        return EnteredForward(journal).LastOrDefault(id => !undone.Contains(id));
    }

    private int IndexOf(string stageId)
    {
        for (var i = 0; i < _pipeline.Order.Count; i++)
        {
            if (string.Equals(_pipeline.Order[i], stageId, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private PipelineTick Fold(PipelineJournal journal, IStage<TContext> stage, StageOutcome outcome) =>
        outcome.Kind switch
        {
            StageOutcomeKind.Working => FoldWorking(journal, stage, outcome),
            StageOutcomeKind.Done => FoldDone(journal, stage, outcome),
            _ => FoldFailed(journal, stage, outcome),
        };

    private PipelineTick FoldWorking(PipelineJournal journal, IStage<TContext> stage, StageOutcome outcome)
    {
        var now = _time.GetUtcNow();
        var (entries, enteredAt) = Upsert(
            journal, stage.Id, journal.Direction, StageEntryState.Working, outcome.Detail, now);

        var overBudget = stage.Budget.Limit is { } limit && now - enteredAt >= limit;
        if (overBudget && stage.Budget.Policy == StageTimeoutPolicy.FailAndCompensate)
        {
            return FoldFailed(
                journal,
                stage,
                StageOutcome.Failed(
                    $"stage '{stage.Id}' was still working after its budget of {stage.Budget.Limit}"));
        }

        return new PipelineTick(
            journal with { Entries = entries },
            overBudget ? PipelineTickResult.WaitingOverBudget : PipelineTickResult.Waiting,
            stage.Id,
            outcome.Detail);
    }

    private PipelineTick FoldDone(PipelineJournal journal, IStage<TContext> stage, StageOutcome outcome)
    {
        var (entries, _) = Upsert(
            journal, stage.Id, journal.Direction, StageEntryState.Done, outcome.Detail, _time.GetUtcNow());
        var moved = journal with { Entries = entries };

        if (Position(moved) is not null)
        {
            return new PipelineTick(moved, PipelineTickResult.Advanced, stage.Id, outcome.Detail);
        }

        var state = moved.Direction == PipelineDirection.Forward
            ? PipelineState.Completed
            : PipelineState.Compensated;
        return new PipelineTick(moved with { State = state }, Terminal(state), stage.Id, outcome.Detail);
    }

    private PipelineTick FoldFailed(PipelineJournal journal, IStage<TContext> stage, StageOutcome outcome)
    {
        var (entries, _) = Upsert(
            journal, stage.Id, journal.Direction, StageEntryState.Failed, outcome.Detail, _time.GetUtcNow());
        var failed = journal with { Entries = entries };

        if (journal.Direction == PipelineDirection.Compensating)
        {
            return new PipelineTick(
                failed with { State = PipelineState.Failed },
                PipelineTickResult.Failed,
                stage.Id,
                outcome.Detail);
        }

        if (CanCompensate(failed, _pipeline, out var refusal))
        {
            return new PipelineTick(
                failed with { Direction = PipelineDirection.Compensating },
                PipelineTickResult.Waiting,
                stage.Id,
                outcome.Detail);
        }

        return new PipelineTick(
            failed with { State = PipelineState.Failed },
            PipelineTickResult.Failed,
            stage.Id,
            $"{outcome.Detail} ({refusal})");
    }
}

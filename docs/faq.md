# FAQ

## Known problems

### A stage ran twice
Cause: the caller did not persist the journal returned by a tick, so the next tick replayed the same stage
call. The library cannot detect this.
Fix: make every `UpAsync` and `DownAsync` idempotent, and store the returned journal before the next tick.

### `UnknownStage` is returned and nothing advances
Cause: the stored journal names a stage id that the resolved pipeline does not contain, because a stage was
renamed or removed while a run was sitting on it.
Fix: restore the id, or decide in the host what the stranded run means (abandon it, migrate its entries to
the new id) and write the corrected journal. The library never guesses.

### A failure ended the run as `Failed` instead of unwinding
Cause: a stage that was entered going forward declares `IrreversibleOnceEntered`, so compensation is
refused. The failure detail ends with the refusal, naming the stage and quoting its reason.
Fix: none at the library level; the run is forward-only past that stage. Use
`Pipeline<TContext>.FirstIrreversibleStageId` to warn operators before the run starts.

### The run is `Waiting` forever
Cause: a stage keeps answering `Working` and has no budget, or has a `KeepWaitingAndReport` budget, which
never fails the run.
Fix: give the stage a `FailAndCompensate` budget if it should be abandoned, or handle
`WaitingOverBudget` in the host.

### `Resolve` throws `PipelineDefinitionException`
Cause: the set of stages is empty, has a stage without an id, a duplicate id, an anchor that is not in the
set, no head or more than one `StagePlacement.First`, or the placements form a cycle. The message says
which; for a duplicate id, an unknown anchor, several heads or a cycle it also names the stages involved.
Fix: correct the declarations. `Resolve` is meant to run once, at startup.

### A stage threw and the run did not fail
Cause: an exception from `UpAsync` or `DownAsync` propagates out of `AdvanceAsync`. No journal is returned,
so nothing is recorded and the next tick calls the same stage again. A stage that keeps throwing is retried
on every tick, and its budget does not end it, because only `Working` answers are measured.
Fix: catch expected errors in the stage and return `StageOutcome.Failed(reason)` to fail the run, or
`StageOutcome.Working(detail)` to try again on the next tick.

## Questions

### Why does `Down` run on a stage that failed?
A stage that failed part-way through `UpAsync` may have applied part of its effect. The saga rule is that a
compensating action is defined over a step that may have run in part, so `DownAsync` must be safe on a stage
that never began.

### Why is the failed stage's `Down` not called in the same tick?
The tick that observes the failure makes its one stage call and turns the direction around. The unwind starts
on the next tick, which keeps the rule of one stage call per tick.

### Can I run stages in parallel or branch?
No. The pipeline is a line.

### Can I persist the journal in my own format?
Yes. The journal is an immutable record and the library ships no serialiser. Store the entries, direction and
state however your host stores things.

### What happens when a tick is called on a finished run?
Nothing. A terminal journal is returned unchanged.

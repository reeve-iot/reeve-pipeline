# Architecture

## Scope

`Reeve.Pipeline` is a small .NET library (`net11.0`) that walks a list of stages one step per tick, forwards
and then, if a stage fails, backwards. Each stage has an `Up` and a `Down`, and a journal records how far the
run got.

It does not persist, schedule, inject dependencies, branch, run in parallel, retry with backoff, raise events
or log. The caller owns the loop and the storage; the library is a pure function from a journal to a journal.
It is suited to hosts that already have a leader-elected loop and a durable row. The package has no
dependencies.

## Building blocks

| Type | Role |
|---|---|
| `IStage<TContext>` | One step: `Id`, `Placement`, `Reversibility`, `Budget`, `UpAsync`, `DownAsync`. |
| `StagePlacement` | `First`, `After(id, ordinal)` or `Before(id, ordinal)`. |
| `StageReversibility` | `Reversible`, or `IrreversibleOnceEntered(operatorReason)`. |
| `StageBudget` | `None`, or `Of(limit, StageTimeoutPolicy)`. |
| `StageOutcome` | `Working(detail)`, `Done` or `Failed(reason)`. |
| `Pipeline<TContext>` | The resolved, ordered set. `Resolve` validates; `Order`, `Find`, `FirstIrreversibleStageId` and `FirstIrreversibleReason` answer questions before anything runs. |
| `PipelineJournal` and `StageEntry` | The run's durable state: an immutable record the caller stores. |
| `PipelineRunner<TContext>` | `AdvanceAsync` makes at most one stage call and returns a `PipelineTick`; `CanCompensate` decides whether a run may be unwound. |
| `PipelineTick` | The new journal, a `PipelineTickResult`, the stage involved and its detail. |

`TContext` is the only extension point. It is passed to every stage untouched and never inspected.

## Runtime model

The caller resolves a `Pipeline<TContext>` once at startup. On each tick of its own loop it loads the
journal, calls `AdvanceAsync`, and stores the returned journal. Time enters only through the `TimeProvider`
passed to the runner. Nothing sleeps, no timer starts and no stage call is awaited across ticks.

### How a tick decides

1. A terminal journal (`Completed`, `Compensated`, `Failed`) is returned unchanged.
2. The current stage is the last journal entry, or the first stage in `Order` when the journal is empty. When
   a forward walk has finished the last stage, or a backward walk has nothing left to undo, the run becomes
   `Completed` or `Compensated`. A stage id the pipeline does not know ends the tick as `UnknownStage`; no
   stage is called and the journal is unchanged.
3. Exactly one call is made: `UpAsync` when walking forward, `DownAsync` when compensating.
4. The outcome is folded into the journal:
   - `Done` going forward records the entry and moves to the next stage in `Order`, or ends the run as
     `Completed`. `Done` while compensating moves to the previous entered stage, or ends as `Compensated`.
   - `Working` records the detail against the entry and returns `Waiting`. The first `Working` appends an entry;
     a repeated one replaces it and keeps the original entry instant, which is what a budget is measured from.
   - A `Working` stage past its budget returns `WaitingOverBudget` under `KeepWaitingAndReport`. Under
     `FailAndCompensate` it is treated as a failure whose reason names the budget.
   - `Failed` going forward: if `CanCompensate` allows, the direction turns around and the tick returns
     `Waiting`. Otherwise the run ends as `Failed` with the refusal appended to the reason. The failed
     stage's own `Down` runs on the next tick.
   - `Failed` while compensating ends the run as `Failed`, naming the stage.

### Ordering

`Pipeline<TContext>.Resolve` turns declared placements into one total order:

- Exactly one stage is `First`. It is the head, and it runs first even when another stage is declared
  `Before` something.
- Ready stages are sorted by `(ordinal, id)`, never by registration order, so the same set of stages
  resolves to the same order in every process.
- An empty set, a stage without an id, a duplicate id, an anchor that is not in the set, no head, more than
  one head and a cycle each throw `PipelineDefinitionException` at resolve time, never at tick time.

### Journal and position

The journal holds `StageEntry` rows (stage id, entry instant, direction, state, detail), the current
direction (`Forward` or `Compensating`) and the run state (`Running`, `Completed`, `Compensated`, `Failed`).
Compensation walks the entered stages newest first, skipping those already undone, and includes the stage
that failed part-way through its `Up`, because it may have applied something. A stage that was never entered
is never compensated.

## Invariants

- The library does no I/O: it opens no socket and touches no storage.
- At most one stage call is made per tick, whatever the outcome.
- The journal is immutable and the engine keeps no state of its own, so a tick replayed from the same
  journal after a failed save starts from exactly where the lost tick did.
- An exception from a stage propagates out of `AdvanceAsync` without a new journal, so the stored journal
  is untouched and the next tick calls that stage again.
- Stages must be idempotent, and `Down` must be safe on a stage that never started. The engine cannot enforce
  either.
- An irreversible stage forfeits compensation, and the refusal names the stage and quotes its reason.
- Unknown stage ids are reported as `UnknownStage`, not thrown.
- Every `await` in the library uses `ConfigureAwait(false)`, so it never captures a consumer's
  synchronisation context.

## Tests

The tests are plain unit tests: no database, clock or container. Time is a `FakeTimeProvider`.

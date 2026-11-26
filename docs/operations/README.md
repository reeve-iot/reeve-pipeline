# Using Reeve.Pipeline in a host

`Reeve.Pipeline` is a library, not a service. It has no settings, environment variables, ports or
background workers. This guide describes how a host runs a pipeline with it.

## Requirements

- .NET 11. The package ships `lib/net11.0` only.
- A loop that ticks, ideally leader-elected so only one process advances a run at a time.
- Somewhere to store the `PipelineJournal` per run: a database row, a document, a file.

## Install

```bash
dotnet add package Reeve.Pipeline
```

The package takes no dependency.

## Running a pipeline

1. Resolve once at startup. `Pipeline<TContext>.Resolve(stages)` validates the placements and throws
   `PipelineDefinitionException` for any mistake. Keep the resolved pipeline for the life of the process.
2. Tick from your own loop. Load the journal (`PipelineJournal.New()` for a new run), call
   `new PipelineRunner<TContext>(pipeline, TimeProvider.System).AdvanceAsync(journal, context, ct)`, and store
   `tick.Journal` before the next tick. Pass a `FakeTimeProvider` in tests.
3. Handle the result.

| `PipelineTickResult` | Meaning | What the host does |
|---|---|---|
| `Advanced` | A stage finished and the walk moved on. | Store the journal; tick again soon. |
| `Waiting` | A stage is still working, and `tick.Detail` says what for. Also returned when a forward failure turns the run around: `tick.Journal.Direction` is then `Compensating` and `tick.Detail` is the failure reason. | Store the journal; tick again on the normal schedule. |
| `WaitingOverBudget` | Still working past a `KeepWaitingAndReport` budget. | Surface it to an operator; keep ticking. |
| `Completed` | Every forward stage is done. | Store the journal; stop ticking. |
| `Compensated` | The backward walk reached the beginning. | Store the journal; stop ticking. |
| `Failed` | A stage failed and could not be compensated, or an undo failed. `tick.StageId` and `tick.Detail` name it. | Alert; the run needs a person. |
| `UnknownStage` | The journal names a stage the pipeline does not have. Nothing changed. | Decide what the stranded run means. |

4. Ask before you start. `pipeline.FirstIrreversibleStageId` and `FirstIrreversibleReason` tell you which
   stage is the point of no return, so you can warn before the run reaches it.
5. Ticking a terminal journal is a no-op and returns it unchanged.

### Rules for stages

- `UpAsync` and `DownAsync` must be idempotent, because an unsaved tick is replayed.
- `DownAsync` must be safe on a stage that never started.
- A stage `Id` is stored in journals. Do not rename one while runs sit on it.
- Report progress with `StageOutcome.Working(detail)` and return; never block waiting for the world to
  change.
- Report a failure with `StageOutcome.Failed(reason)`. An exception thrown by `UpAsync` or `DownAsync`,
  including `OperationCanceledException`, propagates out of `AdvanceAsync` and no journal is returned, so the
  stored journal stays as it was and the next tick calls the same stage again. A stage that keeps throwing
  is retried on every tick and never runs out of budget, because only `Working` answers are measured.

## Configuration reference

There is none. Behaviour is configured in code through the stage declarations:

| Declaration | Effect |
|---|---|
| `Placement` | Position in the order: `First`, `After(id, ordinal)`, `Before(id, ordinal)`. Lower ordinal first; ties fall back to the id. |
| `Reversibility` | `Reversible`, or `IrreversibleOnceEntered(reason)`, which makes the run forward-only once the stage is entered. |
| `Budget` | `StageBudget.None`, or `Of(limit, policy)` with `FailAndCompensate` or `KeepWaitingAndReport`. The limit must be positive. |

## Upgrading

The package follows semantic versioning; removing or renaming a public member is a major version. Read the
[CHANGELOG](../../CHANGELOG.md) before upgrading. Stored journals are the host's format, so a library upgrade
does not migrate anything, and stage ids you keep stable remain valid across upgrades.

## Troubleshooting

See the [FAQ](../faq.md).

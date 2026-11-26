# Reeve.Pipeline

A small .NET library that walks an ordered set of stages one step per tick. Each stage has an `Up` and a
`Down`, so a run that fails part-way is unwound in reverse order from a journal you store. It is a routing
slip with compensation (the core of a saga).

The library has no persistence, no scheduler and no dependency injection, and the package takes no
dependencies. Workflow engines such as WorkflowCore, Elsa or MassTransit Courier want to own the loop and the
tables. If you already have a leader-elected loop and a row to write to, this library only supplies the state
machine.

## Installation

```bash
dotnet add package Reeve.Pipeline
```

The package targets .NET 11.

## Usage

```csharp
var pipeline = Pipeline<MyContext>.Resolve(stages);

var tick = await new PipelineRunner<MyContext>(pipeline, TimeProvider.System)
    .AdvanceAsync(journalYouLoaded, context, ct);

SaveYourRow(tick.Journal);
```

Resolve the pipeline once, at startup. `AdvanceAsync` makes at most one stage call and returns a new
journal. It never waits for a stage to finish: a stage that is still working says so, and the next tick asks
again. The journal is immutable and you store it in whatever format you like. The engine is a function from a
journal to a journal, so tests need no database and no clock.

### What a stage declares

| | |
|---|---|
| `Id` | Stable, and written into the journal. Renaming one strands runs that are sitting on it. |
| `Placement` | `First`, `After(id)` or `Before(id)`, plus an ordinal for ties. Order is data, not registration order. |
| `Reversibility` | `Reversible`, or `IrreversibleOnceEntered(reason)`. The reason is written for an operator to read. |
| `Budget` | `None`, or a time limit plus `FailAndCompensate` or `KeepWaitingAndReport`. |

`Up` and `Down` both return `Working(detail)`, `Done` or `Failed(reason)`.

### The stage contract

- `Up` and `Down` must be idempotent. A tick whose journal was not persisted is replayed, and the library
  cannot make a stage safe to replay.
- `Down` is called on a stage that failed part-way through `Up`, so it must be safe on a stage that never
  started.
- A stage declared `IrreversibleOnceEntered` makes the run forward-only once it is entered: any failure
  from then on, including the stage's own, is reported with that stage's reason instead of being unwound.
- A stage reports failure by returning `Failed(reason)`. An exception it throws propagates out of
  `AdvanceAsync` and nothing is recorded, so the next tick calls the same stage again. A stage that keeps
  throwing is retried on every tick and its budget never runs out, because only `Working` is measured.

## Documentation

- [Architecture](https://github.com/reeve-iot/reeve-pipeline/blob/main/docs/architecture/README.md): the tick model, ordering rules and invariants.
- [Using it in a host](https://github.com/reeve-iot/reeve-pipeline/blob/main/docs/operations/README.md): running a pipeline, handling tick results, upgrading.
- [FAQ](https://github.com/reeve-iot/reeve-pipeline/blob/main/docs/faq.md): replays, irreversible stages, budgets, renamed stages.
- [Changelog](https://github.com/reeve-iot/reeve-pipeline/blob/main/CHANGELOG.md)
- [Contributing](https://github.com/reeve-iot/reeve-pipeline/blob/main/CONTRIBUTING.md)

## Licence

MIT, see [LICENSE](https://github.com/reeve-iot/reeve-pipeline/blob/main/LICENSE).

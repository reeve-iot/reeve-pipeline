# Changelog

All notable changes to `Reeve.Pipeline` are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org/spec/v2.0.0.html). Removing or renaming a public member is a major
change.

## 1.0.1 - 2026-11-26

### Changed

- The documentation and the XML documentation describe what happens when a stage throws: the exception
  propagates out of `AdvanceAsync`, nothing is recorded, and the stage's budget is not checked. They also
  explain that `Waiting` is returned when a forward failure turns the run around to compensate.
- The package README links the licence with an absolute URL, so the link works on nuget.org.
- The package metadata includes the project URL.

## 1.0.0 - 2026-11-25

First release: one package for `net11.0` with XML documentation, and no dependencies.

### Added

- `IStage<TContext>`: a step with `UpAsync` and `DownAsync`, each polled once per tick and answering
  `Working`, `Done` or `Failed`. A stage declares its `Id`, `StagePlacement`, `StageReversibility` and
  `StageBudget`.
- `Pipeline<TContext>.Resolve`: turns declared placements into one deterministic order. It rejects an empty
  set, a stage without an id, a duplicate id, an unknown anchor, anything other than exactly one head, and a
  cycle.
  `FirstIrreversibleStageId` reports the point of no return before anything has run.
- `PipelineJournal`: the run's durable state. It is immutable and stored by the caller; no serialiser ships.
- `PipelineRunner<TContext>.AdvanceAsync`: one stage call per tick, returning a new journal. `CanCompensate`
  names the stage that refuses an unwind and quotes its reason.
- Compensation runs in reverse order over the entered stages, including a stage that failed part-way through
  its `Up`.
- Budgets are measured from the journal's entry time, so a leader failover mid-stage does not reset them.

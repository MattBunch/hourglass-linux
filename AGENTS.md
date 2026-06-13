# AGENTS.md

## Purpose

Hourglass Linux should use a **functional core and imperative shell** architecture.

Prioritize:

1. Immutable domain state.
2. Pure, deterministic business logic.
3. Explicit state transitions.
4. Side effects isolated behind small interfaces at application boundaries.
5. Simple, idiomatic C# over clever abstractions.

These principles are defaults, not dogma. Avalonia data binding, `INotifyPropertyChanged`, commands, timers, serialization frameworks, and Linux integrations may require controlled mutation. Keep that mutation local and prevent it from leaking into domain logic.

## Repository Scope

- The modern Linux solution is `Hourglass.Linux.sln` and targets .NET 10.
- New Linux work belongs under `src/` and its corresponding projects under `tests/`.
- Treat the legacy Windows WPF solution and source as reference material unless a task explicitly asks for changes there.
- Do not modify the legacy `Hourglass.sln`, WPF projects, WiX projects, bundle projects, or Windows-specific behavior merely to make the Linux architecture cleaner.
- Preserve existing observable behavior unless the task explicitly changes requirements.

## Architectural Direction

Use this dependency direction:

```text
Hourglass.Core
    <- Hourglass.Platform
    <- Hourglass.Linux.Services
    <- Hourglass.Linux.Avalonia
```

`Hourglass.Core` must remain platform-neutral. It must not depend on Avalonia, WPF, Linux APIs, DBus, desktop notification libraries, filesystem details, wall-clock globals, or UI dispatchers.

Structure features as:

```text
input/event
    -> pure validation or parsing
    -> pure state transition
    -> immutable next state plus explicit effects
    -> boundary adapter performs effects
    -> UI publishes the resulting snapshot
```

## Immutability Rules

### Domain models

- Prefer `sealed record` for immutable reference-based domain values.
- Prefer `readonly record struct` for small value types when copying is cheap and value semantics are correct.
- Prefer constructor parameters, positional record members, `required` members, and `init` accessors over public setters.
- Prefer `with` expressions to produce modified values.
- Keep fields `readonly` unless they are part of a deliberately stateful boundary object.
- Never expose mutable collections owned by an object.
- Return immutable snapshots, arrays copied for ownership, or read-only abstractions that cannot be mutated through another reference.
- Do not return `List<T>`, `Dictionary<TKey, TValue>`, or mutable collection fields from public APIs.
- Avoid shared mutable static state and mutable singletons.

### Necessary mutable DTOs

Serialization frameworks may require parameterless constructors and public setters. Do not break XML or settings compatibility simply to convert every DTO into a record.

When mutable DTOs are required:

- Keep them in the serialization or infrastructure boundary.
- Treat them as transport shapes, not domain models.
- Map them immediately to immutable domain values after deserialization.
- Map immutable domain values back to DTOs only when persisting.
- Do not pass mutable serialization DTOs through core business logic.

### UI state

Avalonia view models may implement `INotifyPropertyChanged`, but their mutation should be shallow and centralized.

Prefer one immutable UI snapshot:

```csharp
public sealed record TimerViewState(
    string TimerInput,
    string RemainingTime,
    string StatusText,
    string PauseResumeText,
    bool IsInputEnabled,
    bool IsRunning,
    TimerState State);
```

The view model may replace a private `TimerViewState` field and raise notifications for changed public properties. Avoid maintaining several independent mutable fields that can drift out of sync.

## Functional Programming Rules

### Pure functions

Core functions should:

- Return the same output for the same input.
- Receive time, elapsed duration, configuration, and external data as parameters.
- Avoid reading `DateTime.Now`, `DateTimeOffset.Now`, `Stopwatch`, environment variables, files, or process state directly.
- Avoid modifying arguments or captured variables.
- Avoid raising events, logging, filesystem access, notifications, audio playback, or UI updates.

Use small functions for:

- Parsing timer input.
- Validating commands.
- Calculating elapsed, remaining, and expired time.
- Formatting timer values.
- Converting between persistence DTOs and domain models.
- Deriving button text, enabled state, and status text from timer state.

### Explicit transitions

Model timer behavior as immutable state transitions where practical.

Prefer an API shaped like:

```csharp
public static CountdownTransition Start(
    CountdownState current,
    TimerStartRequest request,
    DateTime wallClockNow,
    TimeSpan monotonicNow);

public static CountdownTransition Tick(
    CountdownState current,
    TimeSpan monotonicNow);
```

A transition should contain the next immutable state and any explicit effects or signals:

```csharp
public sealed record CountdownTransition(
    CountdownState State,
    CountdownEffects Effects);
```

Effects may describe facts such as `Started`, `Paused`, `Resumed`, `Stopped`, or `Expired`. Boundary code decides how to publish events, play audio, show notifications, or update the UI.

Do not hide domain state changes inside event handlers.

### Expected failures

- Do not use exceptions for normal parsing or validation failures.
- Idiomatic `Try...` methods are acceptable.
- Use a small result type or a sealed record hierarchy only when it makes several call sites clearer.
- Do not add a large functional-programming library merely to obtain `Option`, `Either`, or `Result`.
- Use exceptions for programming errors, invalid constructor invariants, and genuinely exceptional infrastructure failures.

### Composition and collections

- Prefer expressions, pattern matching, switch expressions, and small composable functions when they improve clarity.
- Use LINQ for transformations, not for hidden side effects.
- Avoid repeated enumeration and unnecessary allocations in the timer tick path.
- A straightforward loop is preferable to an opaque LINQ chain.
- Do not introduce recursion where a loop is safer or clearer.

## Side-Effect Boundaries

Side effects belong in `Hourglass.Platform`, `Hourglass.Linux.Services`, the Avalonia application layer, or dedicated adapters.

Examples include:

- Wall-clock and monotonic time.
- Dispatcher timers.
- Files and settings persistence.
- Linux desktop notifications.
- Tray/status notifier integration.
- Audio playback.
- Suspend inhibition and wake scheduling.
- Process startup and single-instance behavior.

Rules:

- Depend on narrow interfaces.
- Inject implementations rather than constructing them in core logic.
- Prefer existing clock abstractions unless a deliberate migration to `TimeProvider` simplifies the whole design.
- Async infrastructure APIs should accept `CancellationToken` where cancellation is meaningful.
- Avoid service-locator patterns and ambient globals.
- Dispose or unsubscribe from long-lived events deterministically.

## Current Refactoring Priorities

When touching the current codebase, prioritize these improvements:

1. Replace the mutable timer data spread across `CountdownEngine` properties and fields with an immutable `CountdownState` snapshot.
2. Extract timer calculations and state transitions into pure functions or a reducer-like static component.
3. Keep a small orchestration adapter only where clock access and event publication are required.
4. Replace implicit event-driven domain mutation with explicit transition results.
5. Replace duplicated mutable display fields in `MainWindowViewModel` with one immutable view-state snapshot.
6. Move display formatting and derived UI values into pure functions.
7. Keep `RelayCommand`, `DispatcherTimer`, and `INotifyPropertyChanged` as UI-shell concerns.
8. Isolate mutable XML/settings DTOs and map them to immutable domain records.

Do this incrementally. Do not rewrite unrelated legacy code or perform a speculative framework redesign.

## C# Style

- Nullable reference types must remain enabled.
- Do not suppress nullability warnings with `!` unless the invariant is documented and unavoidable.
- Prefer `ArgumentNullException.ThrowIfNull` for constructor and public API guards.
- Prefer precise types over primitive flags when a value has domain meaning.
- Use pattern matching and switch expressions for state-dependent behavior.
- Keep methods small enough that inputs, outputs, and side effects are obvious.
- Avoid boolean parameters that obscure intent; prefer named request types or separate methods.
- Avoid inheritance unless substitutability is genuinely required. Prefer composition and sealed types.
- Do not create generic abstractions until at least two real call sites justify them.
- Do not add dependencies without a concrete benefit that cannot be achieved cleanly with the BCL or existing packages.
- Preserve existing naming and formatting conventions in untouched code. Avoid mass style-only edits.

## Testing Requirements

Every behavior-changing refactor must retain or improve test coverage.

Prioritize tests for:

- Pure transition functions for start, tick, pause, resume, stop, restart, and expiry.
- Boundary times: zero duration, negative input rejection, exact expiry, and time beyond expiry.
- Monotonic-time calculations independent of wall-clock changes.
- Parsing and validation failures without exceptions.
- Serialization DTO/domain round trips.
- Immutable state: an earlier state value must not change after a later transition.
- View-model state projection and command availability.
- Expiry effects emitted exactly once.

Tests must not use real delays, sleep calls, or the real system clock.

## Required Workflow

Before editing:

1. Read this file and any more specific nested `AGENTS.md` files.
2. Inspect the current implementation and tests.
3. Identify the smallest useful refactor boundary.
4. Run the existing tests or record why they cannot run.

During editing:

1. Add or adjust characterization tests before risky behavior-preserving changes.
2. Separate pure logic before changing orchestration code.
3. Keep commits and diffs focused.
4. Avoid mixing architecture changes with unrelated UI polish.

Before finishing, run:

```bash
dotnet restore Hourglass.Linux.sln
dotnet build Hourglass.Linux.sln --configuration Release --no-restore
dotnet test Hourglass.Linux.sln --configuration Release --no-build --verbosity normal
```

Also run formatting or analyzer checks already configured by the repository. Do not introduce a new formatting regime as part of an unrelated refactor.

## Definition of Done

A change is complete when:

- Domain behavior is represented by immutable values where practical.
- New core logic is deterministic and testable without real infrastructure.
- Side effects are visible at the boundary.
- Mutable framework requirements are contained.
- Serialization compatibility is preserved or intentionally migrated with tests.
- Existing user-visible behavior remains correct.
- Build and tests pass.
- The final summary explains the state model, side-effect boundaries, tests added, and any intentionally retained mutation.

# DOF Production Manager — instructions for Claude Code

- The governing document is `docs/TECHNICAL_SPECIFICATION.md`. **Part 11 (development instructions) and
  Part 10 (phase order) apply to all work.** Read the relevant parts before starting a phase.
- Check `docs/DEVELOPMENT_ROADMAP.md` for the current phase. Do not start a phase until the previous one
  has passed its acceptance criteria.
- This is a standalone project: never reference or reuse code, schema or the `dofhub` database from DOF Hub.
- Keep `docs/TECHNICAL_ARCHITECTURE.md`, `docs/DATABASE_SCHEMA.md`, `docs/API_SPECIFICATION.md` and
  the roadmap up to date as part of every phase.

## Commands

```bash
dotnet build                     # warnings are errors
dotnet test                      # needs local PostgreSQL; integration tests use dof_production_manager_test
dotnet run --project src/DOF.Api
dotnet run --project src/DOF.Desktop   # desktop app; API address in src/DOF.Desktop/appsettings.json
dotnet ef migrations add <Name> --project src/DOF.Infrastructure --startup-project src/DOF.Api --output-dir Persistence/Migrations
dotnet ef database update --project src/DOF.Infrastructure --startup-project src/DOF.Api
```

## Conventions

- Package versions live only in `Directory.Packages.props`; stay on .NET 10-compatible (10.0.x) Microsoft packages.
- New entities derive from `Entity`; use `IVersioned` for editable records and `IArchivable` instead of deletes.
- Review every generated migration before applying; no destructive migrations without explicit approval.
- API routes under `/api/v1`, success bodies in `ApiResponse<T>`, errors as ProblemDetails.
- Calendar: never copy a date into `calendar_events`. A record type that owns dates adds an `ICalendarSource`
  (register it in `Application/DependencyInjection.cs`, add its type to `CalendarValues.SourceTypes`) and the
  shell's `OpenRecordAsync` learns to open it.
- Record important changes with `ActivityRecorder` in the same unit of work. `activity_logs` is append-only (DB trigger).
- Permission checks for projects, milestones and tasks live in `WorkAccess`; never rely on the desktop hiding buttons.
- Desktop: new pages derive from `PageViewModel`, call the API only through `ApiClient`, and get a
  `DataTemplate` in `App.axaml`. Use theme tokens (`Brush.*`), never hard-coded colours.
- Desktop UI tests: wrap test bodies with `OnUiThread` (it uses the `Func<Task<T>>` Dispatch overload
  so the test is actually awaited, with a 90 s limit). Review the screenshots in `ui-snapshots/` after UI changes.
- A UI test that seems to hang the run is usually an unclosed window or a picker feedback loop, not a slow test.

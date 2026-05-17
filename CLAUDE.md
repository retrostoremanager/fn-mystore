# fn-mystore — Azure Functions Backend

## Build & Test

```bash
dotnet restore MyStore.sln
dotnet build MyStore.sln --configuration Release --no-restore
dotnet test MyStore.Tests/MyStore.Tests.csproj --no-build
```

## Project Structure

- `MyStore.Functions/` — Azure Function classes (HTTP triggers, one class per domain)
- `MyStore.Services/` — Business logic (interfaces + implementations)
- `MyStore.Repositories/` — Data access layer (SQL Server via Dapper)
- `MyStore.Models/` — Shared DTOs and domain models
- `MyStore.Tests/` — xUnit + Moq + FluentAssertions

Entry point / DI registration: `MyStore.Functions/Program.cs`

## Coding Standards

Read `AGENTS.md` for the full coding standards, architecture patterns, and step-by-step guide for adding new features.

## Key Conventions

- All HTTP functions use `HttpRequestData` / `HttpResponseData` (isolated worker model)
- Multi-tenant: always extract company ID via `CompanyHelper.GetCompanyIdRequired(req)`
- Wrap all responses in `ApiResponse<T>`
- Repository → Service → Function layering; keep functions thin
- Never use `.Result` / `.Wait()` — async all the way

## When Opening PRs

Target the `development` branch. Include a brief summary of what changed and why.

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

## Code Review Mode

When your session starts with "Code review mode. Review PR #N", complete ALL steps and execute the final GitHub command. Do not stop at analysis — you must call `gh pr review` or `gh pr merge`.

### Step 1 — Check retry count
```bash
RETRIES=$(gh pr view N --json labels --jq '[.labels[].name | select(startswith("review-retry-"))] | length')
```
If RETRIES >= 3: run `gh pr edit N --add-label "needs-human-review"` and `gh pr comment N --body "Max revision cycles reached. Human review required."` then stop.

### Step 2 — Get PR diff ONLY (do not read the whole codebase)
```bash
gh pr view N --json title,body,headRefName,number
gh pr diff N
```
Use Read only on files that appear in the diff output.

### Step 3 — Classify issues introduced by this PR's changes
- **MAJOR**: security vulnerabilities, auth bypass, data loss, breaking API changes, wrong business logic
- **MODERATE**: significant bugs, missing error handling, wrong HTTP status codes
- **MINOR**: style, naming — do not block merge

### Step 4 — APPROVE: run these exact commands
```bash
gh pr review N --approve --body "Code review passed. No major or moderate issues."
gh pr merge N --squash --delete-branch
ISSUE_N=$(gh pr view N --json body --jq '.body' | grep -oP 'orchestrator-mystore#\K[0-9]+')
gh issue edit $ISSUE_N --repo sbranham314/orchestrator-mystore --remove-label in-progress --add-label done
```

### Step 5 — REQUEST CHANGES: run these exact commands
```bash
NEXT=$((RETRIES + 1))
HEAD=$(gh pr view N --json headRefName --jq '.headRefName')
gh pr edit N --add-label "review-retry-$NEXT"
gh pr review N --request-changes --body "## Review Findings (attempt $NEXT/3)

[your detailed findings with file:line references]"
ORIGINAL=$(gh pr view N --json body --jq '.body')
jq -n --arg p "REVISION REQUEST for $HEAD (attempt $NEXT/3).

Original task: $ORIGINAL

Review feedback: [your findings]

Push fixes to EXISTING branch $HEAD. Do NOT create a new branch." \
  --arg b "$HEAD" '{"ref":"main","inputs":{"prompt":$p,"branch":$b}}' | \
GH_TOKEN="$GH_DISPATCH_TOKEN" gh api \
  repos/sbranham314/fn-mystore/actions/workflows/claude-code.yml/dispatches \
  --method POST --input -
```

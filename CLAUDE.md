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

When your session starts with "Code review mode. Review PR #N", follow these steps exactly.

### Step 1 — Check retry count
```bash
gh pr view N --json labels --jq '.labels[].name' | grep '^review-retry-' | wc -l
```
If the count is **3 or more**:
- `gh pr edit N --add-label "needs-human-review"`
- `gh pr comment N --body "Max revision cycles (3) reached. Human review required."`
- Stop. Do nothing else.

### Step 2 — Gather context
```bash
gh pr view N --json title,body,headRefName,number
gh pr diff N
```
Read each changed file in full using the Read tool.

### Step 3 — Classify issues
- **MAJOR**: security vulnerabilities, auth bypass, data loss, breaking API changes, wrong business logic
- **MODERATE**: significant bugs, missing error handling for common failures, wrong HTTP status codes
- **MINOR**: style, naming, missing comments — acceptable, do not block merge

### Step 4 — APPROVE (no major or moderate issues)
```bash
gh pr review N --approve --body "LGTM. No major or moderate issues."
gh pr merge N --squash --delete-branch
```
Then close the orchestrator issue. Extract the issue number from the PR body (look for `Closes sbranham314/orchestrator-mystore#N`):
```bash
gh issue edit ISSUE_N --repo sbranham314/orchestrator-mystore --remove-label in-progress --add-label done
```

### Step 5 — REQUEST CHANGES (major or moderate issues found)
1. Count existing `review-retry-*` labels to determine next retry number (start at 1).
2. `gh pr edit N --add-label "review-retry-NEXT"`
3. `gh pr review N --request-changes --body "[detailed list of issues with file paths, line numbers, and specific fixes required]"`
4. Write a revision prompt to a temp file:
```bash
cat > /tmp/revision-prompt.txt << 'ENDOFPROMPT'
REVISION REQUEST for BRANCH_NAME (attempt NEXT/3).

Original task:
ORIGINAL_TASK_TEXT

Review feedback:
YOUR_DETAILED_FEEDBACK

IMPORTANT: Push your fixes to the EXISTING branch BRANCH_NAME. Do NOT create a new branch. Commit and push, then the review agent will re-run automatically.
ENDOFPROMPT
```
5. Re-dispatch the developer agent:
```bash
jq -n --arg prompt "$(cat /tmp/revision-prompt.txt)" \
  '{"ref":"main","inputs":{"prompt":$prompt,"branch":"development"}}' | \
GH_TOKEN="$GH_DISPATCH_TOKEN" gh api \
  repos/sbranham314/fn-mystore/actions/workflows/claude-code.yml/dispatches \
  --method POST --input -
```

# fn-mystore — Azure Functions Backend

## Efficiency Rules (READ FIRST)

**These rules exist to prevent hitting spending caps. Follow them strictly.**

1. **Never run Glob, LS, or Find.** The full file map is below — use it.
2. **Read each file at most once per session.** If you've read it, you have the content — don't re-read it.
3. **For new features:** read only the interface file(s) for the domain you're touching, plus `Program.cs` to check DI registration. Do NOT read every existing service/repo "for context."
4. **For bug fixes:** read the diff (`gh pr diff N`) first. Only `Read` files that appear in the diff.
5. **Before your first tool call:** write a one-sentence plan stating which files you'll read and why. Commit to it.
6. **If the task requires >5 files read:** stop and reconsider — you're probably exploring instead of implementing.

## Complete File Map

Do not Glob or LS. Every file is listed here.

### MyStore.Functions/
- `Program.cs` — DI registration (always check here before adding new services)
- `Functions/AccountFunctions.cs` — login, register, email verification, password reset
- `Functions/BillingFunctions.cs` — Stripe webhooks, payment methods, trial status, subscription
- `Functions/CompanyProfileFunctions.cs` — company profile get/update, logo upload
- `Functions/CustomerFunctions.cs` — customer CRUD
- `Functions/GameFunctions.cs` — game search and catalog
- `Functions/HealthFunctions.cs` — health check endpoint
- `Functions/InventoryFunctions.cs` — inventory CRUD
- `Functions/PermissionFunctions.cs` — RBAC permission queries
- `Functions/RoleFunctions.cs` — role CRUD
- `Functions/SalesFunctions.cs` — sales transactions
- `Functions/TrialConversionFunctions.cs` — timer trigger for trial-to-paid conversion
- `Functions/TrialNotificationFunctions.cs` — timer trigger for trial expiry emails
- `Functions/TrialSuspensionFunctions.cs` — timer trigger for suspending expired trials
- `Functions/UserFunctions.cs` — user management (invite, deactivate, roles)
- `Helpers/CompanyHelper.cs` — `GetCompanyIdRequired(req)` multi-tenant extraction
- `Middleware/CorsMiddleware.cs`, `JwtAuthenticationMiddleware.cs`, `CompanyAccessMiddleware.cs`, `RbacMiddleware.cs` — request pipeline
- `Attributes/RequirePermissionAttribute.cs` — RBAC decoration
- `Services/LogoStorageService.cs` — Azure Blob logo storage

### MyStore.Services/
Interfaces: `ICompanyService`, `ICustomerService`, `IEmailService`, `IGameService`, `IIgdbService`, `IInventoryService`, `IPaymentService`, `IPermissionService`, `ISalesService`, `ISubscriptionChangeService`, `ISubscriptionService`, `ITrialConversionService`, `ITrialSuspensionService`, `IUserService`

Implementations: matching `*Service.cs` files for each interface above, plus:
- `StripeOptions.cs` — Stripe config (SecretKey, WebhookSecret, PriceId*)
- `StripeErrorMapper.cs` — maps Stripe exceptions to user-friendly messages

### MyStore.Repositories/
Interfaces: `ICompanyRepository`, `ICustomerRepository`, `IGameRepository`, `IInventoryRepository`, `ILocationRepository`, `IPaymentRepository`, `IRoleRepository`, `ISalesRepository`, `ISubscriptionRepository`, `IUserRepository`

Implementations: matching `*Repository.cs` for each above. All use Dapper + NpgsqlConnection. Connection string from env `ConnectionStrings__DefaultConnection`.

### Database Schema (retrostoremanager/dbproj-mystore, development branch)

**MANDATORY: Before writing any SQL query, check the exact column names in the schema.**

```bash
# Read the full schema for a specific table (example: sale table)
GH_TOKEN="$GH_DISPATCH_TOKEN" gh api \
  repos/retrostoremanager/dbproj-mystore/contents/PostgreSQL/006_create_sale_table.sql?ref=development \
  --jq '.content' | base64 -d
```

Key migration files and the tables they define:
- `001_create_company_table.sql` — company
- `002_create_customer_table.sql` — customer
- `004_create_game_table.sql` — game_encyclopedia (renamed in 034)
- `005_create_inventory_item_table.sql` — game_inventory / inventory_item (renamed in 034)
- `006_create_sale_table.sql` — sale (columns: id, company_id, customer_id, employee_id, **subtotal**, **tax**, **total**, payment_method, sale_date, notes)
- `007_create_sale_item_table.sql` — sale_item (columns: id, sale_id, inventory_item_id, quantity, unit_price, **total_price**)
- `015_create_payment_method_table.sql` — payment_method
- `020_create_subscription_table.sql` — subscription
- `025_create_location_table.sql` — location
- `031_add_user_role_permission_schema.sql` — user, role, permission, role_permission, user_role

### MyStore.Models/
- `ApiResponse.cs` — `ApiResponse<T>` wrapper with `SuccessResponse(data)` / `ErrorResponse(msg)`
- `Company.cs` — Company, TrialStatusResponse, CompanyProfile, TrialConversionCandidate, SubscriptionStatusResponse
- `Customer.cs`, `Game.cs`, `InventoryItem.cs`, `PaymentMethod.cs`, `Role.cs`, `Sale.cs`, `Subscription.cs`, `SubscriptionChangeModels.cs`, `User.cs`

### MyStore.Tests/
- `Functions/` — BillingFunctionsTests, AccountFunctionsTests, TrialConversionFunctionsTests, TrialNotificationFunctionsTests
- `Services/` — CompanyServiceTests, InventoryServiceTests, StripeErrorMapperTests, SubscriptionServiceTests, TrialConversionServiceTests
- `Repositories/` — CompanyRepositoryTests
- `Helpers/TestHelpers.cs` — `MockHttpRequestData`, `MockHttpResponseData`, `CreateHttpRequestDataWithRawBody`, `ReadResponseBody`
- `Helpers/StripeTestHelpers.cs` — `CreateSubscriptionObjectPayload`, `WrapInEventPayload`, `CreateSignedPayloadAndHeader`
- `Helpers/CompanyHelperTests.cs`

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
- **Before writing SQL:** read the relevant migration file in `retrostoremanager/dbproj-mystore` (development branch) to confirm exact column names — do not guess

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
gh issue edit $ISSUE_N --repo retrostoremanager/orchestrator-mystore --remove-label code-review --add-label in-test
```

### Step 5 — REQUEST CHANGES: run these exact commands
```bash
NEXT=$((RETRIES + 1))
HEAD=$(gh pr view N --json headRefName --jq '.headRefName')
gh pr edit N --add-label "review-retry-$NEXT"
gh pr review N --request-changes --body "## Review Findings (attempt $NEXT/3)

[your detailed findings with file:line references]"
ORIGINAL=$(gh pr view N --json body --jq '.body')
REVISION_PROMPT="REVISION REQUEST for $HEAD (attempt $NEXT/3).

Original task: $ORIGINAL

Review feedback: [your findings]

Push fixes to EXISTING branch $HEAD. Do NOT create a new branch."
cat > /tmp/revision-payload.json << PAYLOAD_EOF
$(jq -n --arg p "$REVISION_PROMPT" --arg b "$HEAD" '{"ref":"main","inputs":{"prompt":$p,"branch":$b}}')
PAYLOAD_EOF
GH_TOKEN="$GH_DISPATCH_TOKEN" gh api \
  repos/retrostoremanager/fn-mystore/actions/workflows/claude-code.yml/dispatches \
  --method POST --input /tmp/revision-payload.json
```

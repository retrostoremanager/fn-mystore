# Agent Guidelines: Azure Functions App

## Token Efficiency (MANDATORY — read before anything else)

Every tool call costs money. This project has a spending cap. Violating these rules causes the run to fail mid-task, wasting all work done so far.

**File reading rules:**
- The complete file map is in `CLAUDE.md`. Do NOT use Glob, LS, or Find — the map is authoritative.
- Read each file **at most once**. After reading, you have the content in context — do not re-read it.
- For a new endpoint: read the relevant interface + implementation (e.g., `IBillingFunctions.cs`, `BillingFunctions.cs`) and `Program.cs`. That's it.
- For tests: read the existing test file for that domain once to understand the mock pattern, then write new tests. Do not read all test files.

**Planning rule:**
Before your first tool call, write: "I will read: [list exact files]. I will NOT read: [what you're skipping and why]." Hold yourself to this.

**Turn budget:**
You have ~50 turns. A typical feature (service + endpoint + tests + PR) should take 20–30 turns. If you're past 35 turns and haven't started writing code, you're over-exploring — stop and implement.

## Pre-PR Validation (MANDATORY)

Before creating any PR, you MUST run and pass both of these commands:

```bash
dotnet build MyStore.sln --configuration Release
dotnet test MyStore.Tests/MyStore.Tests.csproj --configuration Release --no-build --logger "console;verbosity=normal"
```

**Rules — no exceptions:**
- **Zero build errors.** Fix compile errors before creating the PR. A PR with build failures will be rejected automatically.
- **Zero test failures.** Fix or update tests before creating the PR. Do not create the PR if any test fails.
- **New functionality = new tests.** For every new endpoint, service method, or middleware change, write at least one unit test covering the happy path.
- **Auth/middleware changes require a round-trip test.** If you touch `JwtAuthenticationMiddleware`, `CompanyService.GenerateJwtToken`, or any config key lookup for JWT, write a test that: (1) signs a token with the configured key, and (2) validates that same token through the middleware or validator using the exact same key name.
- **Config key consistency.** Always use colon notation (`JwtAuthentication:SecretKey`) in `_configuration["..."]` calls — never `JwtAuthentication__SecretKey`. The double-underscore form is the Azure env var name; the .NET config provider maps it to the colon form before your code sees it.

## Critical First Step: Check for Existing Code AND Database Schema

**BEFORE implementing any new functionality, you MUST:**
1. Search the codebase for existing implementations that handle similar functionality
2. Check for existing helper classes, utilities, or shared code that can be reused
3. Review existing function patterns to maintain consistency
4. Look for existing service interfaces and implementations
5. Check for existing repository patterns and data access code
6. **DO NOT reinvent the wheel** - reuse existing code patterns and utilities
7. **If writing SQL:** read the relevant schema file from `retrostoremanager/dbproj-mystore` (development branch) before writing any query. Never guess column names.

### Reading the Database Schema

The authoritative schema is in `retrostoremanager/dbproj-mystore`, development branch, under `PostgreSQL/`.

```bash
# List all migration files
GH_TOKEN="$GH_DISPATCH_TOKEN" gh api \
  "repos/retrostoremanager/dbproj-mystore/contents/PostgreSQL?ref=development" \
  --jq '[.[] | .name]'

# Read a specific table's schema (replace filename as needed)
GH_TOKEN="$GH_DISPATCH_TOKEN" gh api \
  "repos/retrostoremanager/dbproj-mystore/contents/PostgreSQL/006_create_sale_table.sql?ref=development" \
  --jq '.content' | base64 -d
```

Key tables and their migration files (from `CLAUDE.md`):
- **sale**: `006_create_sale_table.sql` — has subtotal, tax, total, payment_method, sale_date
- **sale_item**: `007_create_sale_item_table.sql` — has quantity, unit_price, total_price
- **customer**: `002_create_customer_table.sql`
- **inventory_item** (was game_inventory): `005` + `034`
- **user/role/permission**: `031_add_user_role_permission_schema.sql`

## Architecture Patterns

### Project Structure
- **Functions/**: Contains Azure Function classes (one class per domain area)
- **Helpers/**: Contains shared utility classes and helper methods
- **Services/**: Contains business logic services (registered in Program.cs)
- **Repositories/**: Contains data access layer (registered in Program.cs)
- **Models/**: Contains data models and DTOs

### Dependency Injection
- All dependencies are registered in `Program.cs`
- Use constructor injection for all dependencies
- Services and repositories should be registered as `Scoped`
- Follow the existing pattern: Repository → Service → Function

### Function Class Structure
```csharp
public class DomainFunctions
{
    private readonly IDomainService _domainService;
    private readonly ILogger _logger;

    public DomainFunctions(IDomainService domainService, ILoggerFactory loggerFactory)
    {
        _domainService = domainService;
        _logger = loggerFactory.CreateLogger<DomainFunctions>();
    }

    [Function("FunctionName")]
    public async Task<HttpResponseData> FunctionName(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "route")] HttpRequestData req)
    {
        // Implementation
    }
}
```

## Coding Standards

### HTTP Functions
- Use `HttpRequestData` and `HttpResponseData` for HTTP operations
- Use `AuthorizationLevel.Anonymous` for functions (authentication handled at application level)
- Use RESTful route naming: `resource`, `resource/{id}`, `resource/{id}/subresource`
- HTTP methods should match their purpose:
  - `GET` for retrieval
  - `POST` for creation
  - `PUT` for full updates
  - `PATCH` for partial updates
  - `DELETE` for deletion

### Error Handling
- Use try-catch blocks for exception handling
- Catch specific exceptions (e.g., `UnauthorizedAccessException`)
- Return appropriate HTTP status codes:
  - `200 OK` for successful GET requests
  - `201 Created` for successful POST requests
  - `400 BadRequest` for validation errors
  - `401 Unauthorized` for authentication errors
  - `404 NotFound` for missing resources
  - `500 InternalServerError` for unexpected errors
- Always wrap responses in `ApiResponse<T>` format

### Response Format
- Use the `ApiResponse<T>` wrapper class for all responses
- Use `CreateHttpResponse` helper method pattern for consistent responses
- Set `Content-Type` header to `application/json; charset=utf-8`
- Use camelCase JSON serialization (`PropertyNamingPolicy = JsonNamingPolicy.CamelCase`)

### Logging
- Use `ILogger` with structured logging
- Include relevant context in log messages (e.g., companyId, entity IDs)
- Use appropriate log levels:
  - `LogInformation` for normal operations
  - `LogWarning` for recoverable issues
  - `LogError` for errors
  - `LogCritical` for critical failures

### Company/Organization Context
- Always extract company ID using `CompanyHelper.GetCompanyIdRequired(req)`
- Pass company ID to service methods for multi-tenant data isolation
- Handle `UnauthorizedAccessException` when company ID is missing or invalid

### Input Validation
- Validate required query parameters and request body
- Return `400 BadRequest` with descriptive error messages for invalid input
- Use proper date/time parsing with error handling
- Validate entity IDs and relationships

### Async/Await
- All function methods should be `async Task<HttpResponseData>`
- Use `await` for all asynchronous operations
- Do not use `.Result` or `.Wait()` - always use async/await

## Service Layer Patterns

### Service Interface
- Define interfaces in the Services project (e.g., `IDomainService`)
- Methods should return `Task<ApiResponse<T>>`
- Include company ID parameter for multi-tenant operations

### Service Implementation
- Implement business logic in service classes
- Call repository methods for data access
- Handle business rules and validation
- Return `ApiResponse<T>` with success/error status

## Repository Layer Patterns

### Repository Interface
- Define interfaces in the Repositories project (e.g., `IDomainRepository`)
- Use async methods returning `Task<T>` or `Task<IEnumerable<T>>`

### Repository Implementation
- Implement data access logic
- Use proper SQL parameterization to prevent SQL injection
- Handle database exceptions appropriately
- Return null or empty collections (not exceptions) for "not found" scenarios

## Helper Classes

### CompanyHelper
- Use `CompanyHelper.GetCompanyIdRequired(req)` to extract company ID from headers
- Throws `UnauthorizedAccessException` if company ID is missing

### Response Helpers
- Create reusable `CreateHttpResponse` methods for consistent response formatting
- Handle JSON serialization with consistent options

## Testing Considerations

- Functions should be testable by mocking services
- Services should be testable by mocking repositories
- Keep business logic in services, not in functions
- Functions should be thin - delegate to services

## Best Practices

1. **Single Responsibility**: Each function should do one thing well
2. **DRY (Don't Repeat Yourself)**: Extract common patterns into helpers
3. **Separation of Concerns**: Functions → Services → Repositories
4. **Error Handling**: Always handle errors gracefully with appropriate status codes
5. **Logging**: Log important operations and errors
6. **Validation**: Validate inputs early and return clear error messages
7. **Async All The Way**: Use async/await throughout the call stack
8. **Consistent Naming**: Follow existing naming conventions
9. **Code Reuse**: Check for existing helpers, utilities, and patterns before writing new code

## Common Patterns to Reuse

Before implementing new functionality, check for:
- Existing `CreateHttpResponse` helper methods
- Existing validation patterns
- Existing error handling patterns
- Existing logging patterns
- Existing company ID extraction patterns
- Existing service/repository patterns for similar entities

## Example: Adding a New Function

1. **Check existing code**: Look for similar functions, services, and repositories
2. **Create/Update Service Interface**: Add method to service interface if needed
3. **Implement Service Method**: Add business logic in service implementation
4. **Create/Update Repository**: Add data access methods if needed
5. **Create Function**: Add new function method following existing patterns
6. **Register Dependencies**: Ensure all dependencies are registered in `Program.cs`
7. **Test**: Verify the function works with proper error handling


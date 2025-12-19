# Agent Guidelines: Azure Functions App

## Critical First Step: Check for Existing Code

**BEFORE implementing any new functionality, you MUST:**
1. Search the codebase for existing implementations that handle similar functionality
2. Check for existing helper classes, utilities, or shared code that can be reused
3. Review existing function patterns to maintain consistency
4. Look for existing service interfaces and implementations
5. Check for existing repository patterns and data access code
6. **DO NOT reinvent the wheel** - reuse existing code patterns and utilities

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
- Always use `AuthorizationLevel.Anonymous` (authentication handled at APIM level)
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


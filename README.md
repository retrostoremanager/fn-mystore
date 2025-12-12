# fn-mystore

Backend Azure Functions for handling MyStore requests.

## Architecture

This project follows a layered architecture pattern:

- **Function Layer** (`MyStore.Functions`): HTTP-triggered Azure Functions that handle incoming requests
- **Service Layer** (`MyStore.Services`): Business logic and orchestration
- **Repository Layer** (`MyStore.Repositories`): Data access layer (currently using in-memory storage)
- **Models Layer** (`MyStore.Models`): DTOs, entities, and request/response models

## Technology Stack

- .NET 8.0
- Azure Functions v4 (Isolated Worker Process)
- Azure Functions Worker SDK

## Project Structure

```
fn-mystore/
├── MyStore.Functions/          # Azure Functions (HTTP triggers)
├── MyStore.Services/           # Business logic layer
├── MyStore.Repositories/        # Data access layer
└── MyStore.Models/             # Domain models and DTOs
```

## API Endpoints

### Inventory
- `GET /api/inventory` - Get all inventory items
- `GET /api/inventory/{id}` - Get inventory item by ID
- `POST /api/inventory` - Create new inventory item
- `PUT /api/inventory/{id}` - Update inventory item
- `DELETE /api/inventory/{id}` - Delete inventory item
- `GET /api/inventory/search?q={query}` - Search inventory

### Customers
- `GET /api/customers` - Get all customers
- `GET /api/customers/{id}` - Get customer by ID
- `POST /api/customers` - Create new customer
- `PUT /api/customers/{id}` - Update customer
- `DELETE /api/customers/{id}` - Delete customer
- `GET /api/customers/search?q={query}` - Search customers

### Employees
- `GET /api/employees` - Get all employees
- `GET /api/employees/{id}` - Get employee by ID
- `POST /api/employees` - Create new employee
- `PUT /api/employees/{id}` - Update employee
- `DELETE /api/employees/{id}` - Delete employee
- `GET /api/employees/search?q={query}` - Search employees

### Sales
- `GET /api/sales` - Get all sales
- `GET /api/sales/{id}` - Get sale by ID
- `GET /api/sales/customer/{customerId}` - Get sales by customer
- `GET /api/sales/date-range?startDate={date}&endDate={date}` - Get sales by date range
- `POST /api/sales` - Create new sale
- `DELETE /api/sales/{id}` - Delete sale

## Getting Started

### Prerequisites

- .NET 8.0 SDK
- Azure Functions Core Tools (optional, for local development)

### Running Locally

1. Restore dependencies:
   ```bash
   dotnet restore
   ```

2. Build the solution:
   ```bash
   dotnet build
   ```

3. Run the Functions app:
   ```bash
   cd MyStore.Functions
   func start
   ```

   Or using .NET CLI:
   ```bash
   cd MyStore.Functions
   dotnet run
   ```

### Configuration

The `local.settings.json` file contains local development settings. This file is excluded from source control.

## Data Storage

Currently, the application uses in-memory storage for all repositories. To persist data, you'll need to:

1. Replace the repository implementations with actual database access (e.g., Cosmos DB, SQL Server, Azure Table Storage)
2. Update the dependency injection configuration in `Program.cs` to use the new repository implementations

## Future Enhancements

- [ ] Replace in-memory storage with persistent database (Cosmos DB or SQL Server)
- [ ] Add authentication and authorization
- [ ] Integrate with game API (Price Charting/GameEye) for game data
- [ ] Add comprehensive error handling and logging
- [ ] Add unit tests
- [ ] Add integration tests
- [ ] Add API documentation (Swagger/OpenAPI)

## Notes

- The project is currently configured for .NET 8.0. To upgrade to .NET 10 when available, update the `TargetFramework` in all `.csproj` files.
- All API responses follow a consistent `ApiResponse<T>` format with `success`, `data`, `message`, and `errors` fields.

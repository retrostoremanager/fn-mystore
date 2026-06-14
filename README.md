# fn-mystore

Backend Azure Functions for handling MyStore requests.

## Deployment Status

✅ **Deployed to Azure** - This project is currently deployed and running on Azure Function Apps.

## Architecture

This project follows a layered architecture pattern:

- **Function Layer** (`MyStore.Functions`): HTTP-triggered Azure Functions that handle incoming requests
- **Service Layer** (`MyStore.Services`): Business logic and orchestration
- **Repository Layer** (`MyStore.Repositories`): Data access layer with PostgreSQL database
- **Models Layer** (`MyStore.Models`): DTOs, entities, and request/response models

## Technology Stack

- .NET 8.0
- Azure Functions v4 (Isolated Worker Process)
- Azure Functions Worker SDK
- PostgreSQL (Azure Database for PostgreSQL)

## Project Structure

```
fn-mystore/
├── MyStore.Functions/          # Azure Functions (HTTP triggers)
├── MyStore.Services/           # Business logic layer
├── MyStore.Repositories/        # Data access layer
└── MyStore.Models/             # Domain models and DTOs
```

## API Endpoints

### Accounts
- `POST /api/accounts/register` - Register a new account (company)

### Games
- `GET /api/games/search?q={query}` - Search games (local encyclopedia)

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

### Billing
- `POST /api/webhooks/stripe` - Stripe webhook (subscription events; no auth, signature validated)
- `POST /api/billing/payment-methods` - Store payment method
- `GET /api/billing/payment-methods` - Get payment methods
- `PATCH /api/billing/payment-methods/{id}/default` - Set default payment method
- `DELETE /api/billing/payment-methods/{id}` - Delete payment method

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

**Required local.settings.json configuration:**
```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "DatabaseConnectionString": "your-postgresql-connection-string"
  }
}
```

## Database

This application uses **Azure Database for PostgreSQL Flexible Server**. The database schema is managed in the `dbproj-mystore` repository.

## Deployment

The application is currently deployed to Azure using direct deployment from Visual Studio Code or Azure CLI. To deploy updates:

```bash
# Build the project
dotnet publish -c Release

# Deploy using Azure CLI (update with your resource names)
func azure functionapp publish <your-function-app-name>
```

## Future Enhancements

- [ ] Add authentication and authorization
- [ ] Integrate with game API (Price Charting/GameEye) for game data
- [ ] Add comprehensive error handling and logging
- [ ] Expand unit test coverage
- [ ] Add integration tests
- [ ] Add API documentation (Swagger/OpenAPI)

## Notes

- The project is currently configured for .NET 8.0
- All API responses follow a consistent `ApiResponse<T>` format with `success`, `data`, `message`, and `errors` fields
- API authentication is handled at the application level (no API Management layer currently)

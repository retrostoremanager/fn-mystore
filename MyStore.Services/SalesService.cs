using MyStore.Models;
using MyStore.Repositories;

namespace MyStore.Services;

public class SalesService : ISalesService
{
    private readonly ISalesRepository _salesRepository;
    private readonly ICustomerRepository _customerRepository;
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IInventoryRepository _inventoryRepository;

    public SalesService(
        ISalesRepository salesRepository,
        ICustomerRepository customerRepository,
        IEmployeeRepository employeeRepository,
        IInventoryRepository inventoryRepository)
    {
        _salesRepository = salesRepository;
        _customerRepository = customerRepository;
        _employeeRepository = employeeRepository;
        _inventoryRepository = inventoryRepository;
    }

    public async Task<ApiResponse<List<Sale>>> GetAllSalesAsync()
    {
        try
        {
            var sales = await _salesRepository.GetAllAsync();
            await LoadRelatedDataAsync(sales);
            return ApiResponse<List<Sale>>.SuccessResponse(sales);
        }
        catch (Exception ex)
        {
            return ApiResponse<List<Sale>>.ErrorResponse(
                "Failed to retrieve sales",
                new List<string> { ex.Message }
            );
        }
    }

    public async Task<ApiResponse<Sale>> GetSaleByIdAsync(int id)
    {
        try
        {
            var sale = await _salesRepository.GetByIdAsync(id);
            if (sale == null)
            {
                return ApiResponse<Sale>.ErrorResponse($"Sale with ID {id} not found");
            }

            await LoadRelatedDataAsync(new List<Sale> { sale });
            return ApiResponse<Sale>.SuccessResponse(sale);
        }
        catch (Exception ex)
        {
            return ApiResponse<Sale>.ErrorResponse(
                "Failed to retrieve sale",
                new List<string> { ex.Message }
            );
        }
    }

    public async Task<ApiResponse<List<Sale>>> GetSalesByCustomerIdAsync(int customerId)
    {
        try
        {
            var sales = await _salesRepository.GetByCustomerIdAsync(customerId);
            await LoadRelatedDataAsync(sales);
            return ApiResponse<List<Sale>>.SuccessResponse(sales);
        }
        catch (Exception ex)
        {
            return ApiResponse<List<Sale>>.ErrorResponse(
                "Failed to retrieve sales for customer",
                new List<string> { ex.Message }
            );
        }
    }

    public async Task<ApiResponse<List<Sale>>> GetSalesByDateRangeAsync(DateTime startDate, DateTime endDate)
    {
        try
        {
            var sales = await _salesRepository.GetByDateRangeAsync(startDate, endDate);
            await LoadRelatedDataAsync(sales);
            return ApiResponse<List<Sale>>.SuccessResponse(sales);
        }
        catch (Exception ex)
        {
            return ApiResponse<List<Sale>>.ErrorResponse(
                "Failed to retrieve sales for date range",
                new List<string> { ex.Message }
            );
        }
    }

    public async Task<ApiResponse<Sale>> CreateSaleAsync(CreateSaleRequest request)
    {
        try
        {
            // Validation
            if (request.Items == null || request.Items.Count == 0)
            {
                return ApiResponse<Sale>.ErrorResponse("Sale must have at least one item");
            }

            // Verify customer exists
            var customer = await _customerRepository.GetByIdAsync(request.CustomerId);
            if (customer == null)
            {
                return ApiResponse<Sale>.ErrorResponse($"Customer with ID {request.CustomerId} not found");
            }

            // Verify employee exists if provided
            if (request.EmployeeId.HasValue)
            {
                var employee = await _employeeRepository.GetByIdAsync(request.EmployeeId.Value);
                if (employee == null)
                {
                    return ApiResponse<Sale>.ErrorResponse($"Employee with ID {request.EmployeeId.Value} not found");
                }
            }

            var sale = new Sale
            {
                CustomerId = request.CustomerId,
                EmployeeId = request.EmployeeId,
                PaymentMethod = request.PaymentMethod,
                Notes = request.Notes,
                Tax = request.Tax
            };

            decimal subtotal = 0;

            // Process each sale item
            foreach (var itemRequest in request.Items)
            {
                // Verify inventory item exists and has sufficient quantity
                var inventoryItem = await _inventoryRepository.GetByIdAsync(itemRequest.InventoryItemId);
                if (inventoryItem == null)
                {
                    return ApiResponse<Sale>.ErrorResponse($"Inventory item with ID {itemRequest.InventoryItemId} not found");
                }

                if (inventoryItem.Quantity < itemRequest.Quantity)
                {
                    return ApiResponse<Sale>.ErrorResponse(
                        $"Insufficient quantity for {inventoryItem.Name}. Available: {inventoryItem.Quantity}, Requested: {itemRequest.Quantity}"
                    );
                }

                var saleItem = new SaleItem
                {
                    InventoryItemId = itemRequest.InventoryItemId,
                    Quantity = itemRequest.Quantity,
                    UnitPrice = itemRequest.UnitPrice,
                    TotalPrice = itemRequest.Quantity * itemRequest.UnitPrice
                };

                sale.Items.Add(saleItem);
                subtotal += saleItem.TotalPrice;

                // Update inventory quantity
                await _inventoryRepository.UpdateQuantityAsync(itemRequest.InventoryItemId, -itemRequest.Quantity);
            }

            sale.Subtotal = subtotal;
            sale.Total = subtotal + request.Tax;

            var created = await _salesRepository.CreateAsync(sale);
            await LoadRelatedDataAsync(new List<Sale> { created });

            return ApiResponse<Sale>.SuccessResponse(created, "Sale created successfully");
        }
        catch (Exception ex)
        {
            return ApiResponse<Sale>.ErrorResponse(
                "Failed to create sale",
                new List<string> { ex.Message }
            );
        }
    }

    public async Task<ApiResponse<bool>> DeleteSaleAsync(int id)
    {
        try
        {
            var result = await _salesRepository.DeleteAsync(id);
            if (!result)
            {
                return ApiResponse<bool>.ErrorResponse($"Sale with ID {id} not found");
            }

            return ApiResponse<bool>.SuccessResponse(true, "Sale deleted successfully");
        }
        catch (Exception ex)
        {
            return ApiResponse<bool>.ErrorResponse(
                "Failed to delete sale",
                new List<string> { ex.Message }
            );
        }
    }

    private async Task LoadRelatedDataAsync(List<Sale> sales)
    {
        foreach (var sale in sales)
        {
            // Load customer
            if (sale.CustomerId > 0)
            {
                sale.Customer = await _customerRepository.GetByIdAsync(sale.CustomerId);
            }

            // Load employee
            if (sale.EmployeeId.HasValue && sale.EmployeeId.Value > 0)
            {
                sale.Employee = await _employeeRepository.GetByIdAsync(sale.EmployeeId.Value);
            }

            // Load inventory items for sale items
            foreach (var saleItem in sale.Items)
            {
                if (saleItem.InventoryItemId > 0)
                {
                    saleItem.InventoryItem = await _inventoryRepository.GetByIdAsync(saleItem.InventoryItemId);
                }
            }
        }
    }
}


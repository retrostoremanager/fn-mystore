using MyStore.Models;
using MyStore.Repositories;

namespace MyStore.Services;

public class ConsignmentService : IConsignmentService
{
    private readonly IConsignmentRepository _repository;
    private readonly ISalesRepository? _salesRepository;
    private readonly IInventoryRepository? _inventoryRepository;

    public ConsignmentService(
        IConsignmentRepository repository,
        ISalesRepository? salesRepository = null,
        IInventoryRepository? inventoryRepository = null)
    {
        _repository = repository;
        _salesRepository = salesRepository;
        _inventoryRepository = inventoryRepository;
    }

    public async Task<ApiResponse<List<ConsignmentItem>>> GetAllAsync(int companyId, string? status = null)
    {
        try
        {
            var items = await _repository.GetAllAsync(companyId, status);
            return ApiResponse<List<ConsignmentItem>>.SuccessResponse(items);
        }
        catch (Exception ex)
        {
            return ApiResponse<List<ConsignmentItem>>.ErrorResponse(
                "Failed to retrieve consignment items",
                new List<string> { ex.Message }
            );
        }
    }

    public async Task<ApiResponse<ConsignmentItem>> GetByIdAsync(int id, int companyId)
    {
        try
        {
            var item = await _repository.GetByIdAsync(id, companyId);
            if (item == null)
            {
                return ApiResponse<ConsignmentItem>.ErrorResponse($"Consignment item with ID {id} not found");
            }

            return ApiResponse<ConsignmentItem>.SuccessResponse(item);
        }
        catch (Exception ex)
        {
            return ApiResponse<ConsignmentItem>.ErrorResponse(
                "Failed to retrieve consignment item",
                new List<string> { ex.Message }
            );
        }
    }

    public async Task<ApiResponse<ConsignmentItem>> CreateAsync(ConsignmentItem item, int companyId)
    {
        try
        {
            item.CompanyId = companyId;
            item.Status = "pending";
            item.CreatedAt = DateTime.UtcNow;

            var created = await _repository.CreateAsync(item);
            return ApiResponse<ConsignmentItem>.SuccessResponse(created, "Consignment item created successfully");
        }
        catch (Exception ex)
        {
            return ApiResponse<ConsignmentItem>.ErrorResponse(
                "Failed to create consignment item",
                new List<string> { ex.Message }
            );
        }
    }

    public async Task<ApiResponse<ConsignmentItem>> UpdateAsync(ConsignmentItem item, int companyId)
    {
        try
        {
            var existing = await _repository.GetByIdAsync(item.Id, companyId);
            if (existing == null)
            {
                return ApiResponse<ConsignmentItem>.ErrorResponse($"Consignment item with ID {item.Id} not found");
            }

            item.CompanyId = companyId;
            var updated = await _repository.UpdateAsync(item);
            if (updated == null)
            {
                return ApiResponse<ConsignmentItem>.ErrorResponse($"Failed to update consignment item with ID {item.Id}");
            }

            return ApiResponse<ConsignmentItem>.SuccessResponse(updated, "Consignment item updated successfully");
        }
        catch (Exception ex)
        {
            return ApiResponse<ConsignmentItem>.ErrorResponse(
                "Failed to update consignment item",
                new List<string> { ex.Message }
            );
        }
    }

    public async Task<ApiResponse<MarkSoldResponse>> MarkSoldAsync(int id, decimal salePrice, int companyId)
    {
        try
        {
            var existing = await _repository.GetByIdAsync(id, companyId);
            if (existing == null)
            {
                return ApiResponse<MarkSoldResponse>.ErrorResponse($"Consignment item with ID {id} not found");
            }

            if (existing.Status != "pending")
            {
                return ApiResponse<MarkSoldResponse>.ErrorResponse(
                    $"Cannot mark item as sold: current status is '{existing.Status}'. Only pending items can be marked as sold.");
            }

            var updated = await _repository.MarkSoldAsync(id, salePrice, companyId);
            if (updated == null)
            {
                return ApiResponse<MarkSoldResponse>.ErrorResponse($"Failed to mark consignment item {id} as sold");
            }

            var payoutAmount = Math.Round(salePrice * updated.SplitPercent / 100m, 2, MidpointRounding.AwayFromZero);
            var storeAmount = Math.Round(salePrice - payoutAmount, 2, MidpointRounding.AwayFromZero);

            if (_salesRepository is not null)
            {
                var sale = new Sale
                {
                    CompanyId = companyId,
                    CustomerId = updated.CustomerId,
                    Subtotal = salePrice,
                    Tax = 0m,
                    TaxAmount = 0m,
                    Total = salePrice,
                    PaymentMethod = "consignment",
                    SaleDate = DateTime.UtcNow,
                    Notes = $"Consignment item #{updated.Id}: {updated.Description}",
                };

                if (updated.InventoryItemId.HasValue)
                {
                    sale.Items.Add(new SaleItem
                    {
                        InventoryItemId = updated.InventoryItemId.Value,
                        Quantity = 1,
                        UnitPrice = salePrice,
                        TotalPrice = salePrice,
                    });
                }

                await _salesRepository.CreateAsync(sale);
            }

            if (_inventoryRepository is not null && updated.InventoryItemId.HasValue)
            {
                await _inventoryRepository.UpdateQuantityAsync(updated.InventoryItemId.Value, -1, companyId);
            }

            var response = new MarkSoldResponse
            {
                Item = updated,
                PayoutAmount = payoutAmount,
                StoreAmount = storeAmount
            };

            return ApiResponse<MarkSoldResponse>.SuccessResponse(
                response,
                $"Item marked as sold. Customer payout: {payoutAmount:C}, store keeps: {storeAmount:C}");
        }
        catch (Exception ex)
        {
            return ApiResponse<MarkSoldResponse>.ErrorResponse(
                "Failed to mark consignment item as sold",
                new List<string> { ex.Message }
            );
        }
    }

    public async Task<ApiResponse<ConsignmentPayout>> ProcessPayoutAsync(int itemId, string? notes, int companyId)
    {
        try
        {
            var item = await _repository.GetByIdAsync(itemId, companyId);
            if (item == null)
            {
                return ApiResponse<ConsignmentPayout>.ErrorResponse($"Consignment item with ID {itemId} not found");
            }

            if (item.Status != "sold")
            {
                return ApiResponse<ConsignmentPayout>.ErrorResponse(
                    $"Cannot process payout: item status is '{item.Status}'. Only sold items can receive a payout.");
            }

            var existingPayouts = await _repository.GetPayoutsAsync(itemId, companyId);
            if (existingPayouts.Count > 0)
            {
                return ApiResponse<ConsignmentPayout>.ErrorResponse(
                    $"Payout has already been processed for consignment item {itemId}");
            }

            if (item.SalePrice == null)
            {
                return ApiResponse<ConsignmentPayout>.ErrorResponse("Cannot process payout: item has no recorded sale price");
            }

            var payoutAmount = Math.Round(item.SalePrice.Value * item.SplitPercent / 100m, 2, MidpointRounding.AwayFromZero);

            var payout = new ConsignmentPayout
            {
                ConsignmentItemId = itemId,
                Amount = payoutAmount,
                PaidAt = DateTime.UtcNow,
                Notes = notes
            };

            var created = await _repository.CreatePayoutAsync(payout);
            return ApiResponse<ConsignmentPayout>.SuccessResponse(created, "Payout processed successfully");
        }
        catch (Exception ex)
        {
            return ApiResponse<ConsignmentPayout>.ErrorResponse(
                "Failed to process payout",
                new List<string> { ex.Message }
            );
        }
    }

    public async Task<ApiResponse<List<ConsignmentPayout>>> GetPayoutsAsync(int itemId, int companyId)
    {
        try
        {
            var item = await _repository.GetByIdAsync(itemId, companyId);
            if (item == null)
            {
                return ApiResponse<List<ConsignmentPayout>>.ErrorResponse($"Consignment item with ID {itemId} not found");
            }

            var payouts = await _repository.GetPayoutsAsync(itemId, companyId);
            return ApiResponse<List<ConsignmentPayout>>.SuccessResponse(payouts);
        }
        catch (Exception ex)
        {
            return ApiResponse<List<ConsignmentPayout>>.ErrorResponse(
                "Failed to retrieve consignment payouts",
                new List<string> { ex.Message }
            );
        }
    }

    public async Task<ApiResponse<ConsignmentItem>> ReturnToCustomerAsync(int id, int companyId)
    {
        try
        {
            var existing = await _repository.GetByIdAsync(id, companyId);
            if (existing == null)
            {
                return ApiResponse<ConsignmentItem>.ErrorResponse($"Consignment item with ID {id} not found");
            }

            if (existing.Status != "pending")
            {
                return ApiResponse<ConsignmentItem>.ErrorResponse(
                    $"Cannot return item: current status is '{existing.Status}'. Only pending items can be returned.");
            }

            existing.Status = "returned";
            existing.UpdatedAt = DateTime.UtcNow;

            var updated = await _repository.UpdateAsync(existing);
            if (updated == null)
            {
                return ApiResponse<ConsignmentItem>.ErrorResponse($"Failed to return consignment item with ID {id}");
            }

            return ApiResponse<ConsignmentItem>.SuccessResponse(updated, "Item returned to customer successfully");
        }
        catch (Exception ex)
        {
            return ApiResponse<ConsignmentItem>.ErrorResponse(
                "Failed to return consignment item",
                new List<string> { ex.Message }
            );
        }
    }
}

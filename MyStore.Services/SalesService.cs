using Microsoft.Extensions.Logging;
using MyStore.Models;
using MyStore.Repositories;

namespace MyStore.Services;

public class SalesService : ISalesService
{
    private readonly ISalesRepository _salesRepository;
    private readonly ICustomerRepository _customerRepository;
    private readonly IUserRepository _userRepository;
    private readonly IInventoryRepository _inventoryRepository;
    private readonly ICompanyRepository _companyRepository;
    private readonly ILoyaltyService? _loyaltyService;
    private readonly IPromotionService? _promotionService;
    private readonly ILogger<SalesService>? _logger;

    public SalesService(
        ISalesRepository salesRepository,
        ICustomerRepository customerRepository,
        IUserRepository userRepository,
        IInventoryRepository inventoryRepository,
        ICompanyRepository companyRepository,
        ILoyaltyService? loyaltyService = null,
        IPromotionService? promotionService = null,
        ILogger<SalesService>? logger = null)
    {
        _salesRepository = salesRepository;
        _customerRepository = customerRepository;
        _userRepository = userRepository;
        _inventoryRepository = inventoryRepository;
        _companyRepository = companyRepository;
        _loyaltyService = loyaltyService;
        _promotionService = promotionService;
        _logger = logger;
    }

    public async Task<ApiResponse<List<Sale>>> GetAllSalesAsync(int companyId)
    {
        try
        {
            var sales = await _salesRepository.GetAllAsync(companyId);
            await LoadRelatedDataAsync(sales, companyId);
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

    public async Task<ApiResponse<Sale>> GetSaleByIdAsync(int id, int companyId)
    {
        try
        {
            var sale = await _salesRepository.GetByIdAsync(id, companyId);
            if (sale == null)
            {
                return ApiResponse<Sale>.ErrorResponse($"Sale with ID {id} not found");
            }

            await LoadRelatedDataAsync(new List<Sale> { sale }, companyId);
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

    public async Task<ApiResponse<List<Sale>>> GetSalesByCustomerIdAsync(int customerId, int companyId)
    {
        try
        {
            var sales = await _salesRepository.GetByCustomerIdAsync(customerId, companyId);
            await LoadRelatedDataAsync(sales, companyId);
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

    public async Task<ApiResponse<List<Sale>>> GetSalesByDateRangeAsync(DateTime startDate, DateTime endDate, int companyId)
    {
        try
        {
            var sales = await _salesRepository.GetByDateRangeAsync(startDate, endDate, companyId);
            await LoadRelatedDataAsync(sales, companyId);
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

    public async Task<ApiResponse<Sale>> CreateSaleAsync(CreateSaleRequest request, int companyId)
    {
        try
        {
            // Validation
            if (request.Items == null || request.Items.Count == 0)
            {
                return ApiResponse<Sale>.ErrorResponse("Sale must have at least one item");
            }

            if (request.CustomerId == 0)
            {
                return ApiResponse<Sale>.ErrorResponse("customerId is required");
            }

            // Verify customer exists and belongs to company
            var customer = await _customerRepository.GetByIdAsync(request.CustomerId, companyId);
            if (customer == null)
            {
                return ApiResponse<Sale>.ErrorResponse($"Customer with ID {request.CustomerId} not found");
            }

            // Verify user exists if provided and belongs to company
            if (request.UserId.HasValue)
            {
                var user = await _userRepository.GetByIdAsync(request.UserId.Value, companyId);
                if (user == null)
                {
                    return ApiResponse<Sale>.ErrorResponse($"User with ID {request.UserId.Value} not found");
                }
            }

            var taxSettings = await _companyRepository.GetTaxSettingsAsync(companyId);

            var sale = new Sale
            {
                CompanyId = companyId,
                CustomerId = request.CustomerId,
                UserId = request.UserId,
                PaymentMethod = request.PaymentMethod,
                Notes = request.Notes,
            };

            decimal subtotal = 0;
            var cartItems = new List<CartItem>();
            var inventoryItemsMap = new Dictionary<int, InventoryItem>();

            // Process each sale item
            foreach (var itemRequest in request.Items)
            {
                // Verify inventory item exists, belongs to company, and has sufficient quantity
                var inventoryItem = await _inventoryRepository.GetByIdAsync(itemRequest.InventoryItemId, companyId);
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

                inventoryItemsMap[itemRequest.InventoryItemId] = inventoryItem;
                cartItems.Add(new CartItem
                {
                    InventoryItemId = itemRequest.InventoryItemId,
                    Quantity = itemRequest.Quantity,
                    UnitPrice = itemRequest.UnitPrice,
                    Category = inventoryItem.Category,
                });
            }

            // Apply promotions before computing totals
            var discountMap = new Dictionary<int, decimal>();
            var itemPromotionMap = new Dictionary<int, (int Id, string Name)>();
            var itemPromotionContributions = new Dictionary<int, List<LineDiscount>>();
            var promotionNames = new Dictionary<int, string>();
            if (_promotionService is not null)
            {
                var lineDiscounts = await _promotionService.ApplyPromotionsAsync(cartItems, companyId);
                foreach (var ld in lineDiscounts)
                {
                    if (ld.DiscountAmount <= 0m) continue;

                    discountMap[ld.ItemId] = discountMap.GetValueOrDefault(ld.ItemId) + ld.DiscountAmount;
                    if (!itemPromotionMap.ContainsKey(ld.ItemId))
                    {
                        itemPromotionMap[ld.ItemId] = (ld.PromotionId, ld.PromotionName);
                    }

                    if (!itemPromotionContributions.TryGetValue(ld.ItemId, out var contributions))
                    {
                        contributions = new List<LineDiscount>();
                        itemPromotionContributions[ld.ItemId] = contributions;
                    }
                    contributions.Add(ld);

                    if (!promotionNames.ContainsKey(ld.PromotionId))
                    {
                        promotionNames[ld.PromotionId] = ld.PromotionName;
                    }
                }
            }

            decimal discountTotal = 0m;
            var appliedPromotionsMap = new Dictionary<int, decimal>();
            foreach (var itemRequest in request.Items)
            {
                var lineTotal = itemRequest.Quantity * itemRequest.UnitPrice;
                var discount = discountMap.TryGetValue(itemRequest.InventoryItemId, out var d) ? d : 0m;
                var appliedDiscount = Math.Min(discount, lineTotal);
                var discountedTotal = lineTotal - appliedDiscount;

                var saleItem = new SaleItem
                {
                    InventoryItemId = itemRequest.InventoryItemId,
                    Quantity = itemRequest.Quantity,
                    UnitPrice = itemRequest.UnitPrice,
                    TotalPrice = discountedTotal,
                    DiscountAmount = appliedDiscount,
                };

                if (appliedDiscount > 0m && itemPromotionMap.TryGetValue(itemRequest.InventoryItemId, out var promo))
                {
                    saleItem.PromotionId = promo.Id;
                    saleItem.PromotionName = promo.Name;
                }

                // Scale per-promotion contributions to the capped applied discount so the
                // sum across applied promotions equals discountTotal.
                if (appliedDiscount > 0m &&
                    itemPromotionContributions.TryGetValue(itemRequest.InventoryItemId, out var contributions))
                {
                    if (discount > 0m && appliedDiscount < discount)
                    {
                        var ratio = appliedDiscount / discount;
                        decimal allocated = 0m;
                        for (var i = 0; i < contributions.Count; i++)
                        {
                            var contrib = contributions[i];
                            decimal scaled;
                            if (i == contributions.Count - 1)
                            {
                                scaled = appliedDiscount - allocated;
                            }
                            else
                            {
                                scaled = Math.Round(contrib.DiscountAmount * ratio, 2, MidpointRounding.AwayFromZero);
                                allocated += scaled;
                            }
                            appliedPromotionsMap[contrib.PromotionId] =
                                appliedPromotionsMap.GetValueOrDefault(contrib.PromotionId) + scaled;
                        }
                    }
                    else
                    {
                        foreach (var contrib in contributions)
                        {
                            appliedPromotionsMap[contrib.PromotionId] =
                                appliedPromotionsMap.GetValueOrDefault(contrib.PromotionId) + contrib.DiscountAmount;
                        }
                    }
                }

                sale.Items.Add(saleItem);
                subtotal += saleItem.TotalPrice;
                discountTotal += appliedDiscount;

                // Update inventory quantity
                await _inventoryRepository.UpdateQuantityAsync(itemRequest.InventoryItemId, -itemRequest.Quantity, companyId);
            }

            sale.DiscountTotal = discountTotal;
            sale.AppliedPromotions = appliedPromotionsMap
                .Select(kvp => new AppliedPromotion
                {
                    PromotionId = kvp.Key,
                    PromotionName = promotionNames.TryGetValue(kvp.Key, out var name) ? name : string.Empty,
                    DiscountAmount = kvp.Value,
                })
                .ToList();

            decimal taxAmount;
            decimal taxRate;
            string? taxLabel;

            if (taxSettings is { TaxEnabled: true })
            {
                taxRate = taxSettings.TaxRate;
                taxLabel = taxSettings.TaxLabel;
                taxAmount = Math.Round(subtotal * taxRate, 2, MidpointRounding.AwayFromZero);
            }
            else
            {
                taxRate = 0m;
                taxLabel = null;
                taxAmount = 0m;
            }

            sale.Subtotal = subtotal;
            sale.TaxAmount = taxAmount;
            sale.TaxRate = taxRate;
            sale.TaxLabel = taxLabel;
            sale.Tax = taxAmount;
            sale.Total = subtotal + taxAmount;

            var created = await _salesRepository.CreateAsync(sale);
            await LoadRelatedDataAsync(new List<Sale> { created }, companyId);

            if (_loyaltyService is not null)
            {
                try
                {
                    await _loyaltyService.EarnFromSaleAsync(request.CustomerId, companyId, sale.Total, created.Id);
                }
                catch (Exception loyaltyEx)
                {
                    _logger?.LogError(
                        loyaltyEx,
                        "Failed to award loyalty points for sale {SaleId} (customer {CustomerId}, company {CompanyId}); sale completion will not be rolled back",
                        created.Id,
                        request.CustomerId,
                        companyId);
                }
            }

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

    public async Task<ApiResponse<bool>> DeleteSaleAsync(int id, int companyId)
    {
        try
        {
            var result = await _salesRepository.DeleteAsync(id, companyId);
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

    private async Task LoadRelatedDataAsync(List<Sale> sales, int companyId)
    {
        TaxSettingsResponse? taxSettings = null;
        if (sales.Count > 0)
        {
            taxSettings = await _companyRepository.GetTaxSettingsAsync(companyId);
        }

        foreach (var sale in sales)
        {
            // Load customer (must belong to same company)
            if (sale.CustomerId > 0)
            {
                sale.Customer = await _customerRepository.GetByIdAsync(sale.CustomerId, companyId);
            }

            // Load user (must belong to same company)
            if (sale.UserId.HasValue && sale.UserId.Value > 0)
            {
                sale.User = await _userRepository.GetByIdAsync(sale.UserId.Value, companyId);
            }

            // Load inventory items for sale items (must belong to same company)
            foreach (var saleItem in sale.Items)
            {
                if (saleItem.InventoryItemId > 0)
                {
                    saleItem.InventoryItem = await _inventoryRepository.GetByIdAsync(saleItem.InventoryItemId, companyId);
                }
            }

            if (taxSettings is { TaxEnabled: true })
            {
                sale.TaxRate = taxSettings.TaxRate;
                sale.TaxLabel = taxSettings.TaxLabel;
            }
            else
            {
                sale.TaxRate = 0m;
                sale.TaxLabel = null;
            }
        }
    }
}


using MyStore.Models;
using MyStore.Repositories;

namespace MyStore.Services;

public class ReceiptService : IReceiptService
{
    private readonly ISalesRepository _salesRepository;
    private readonly ICompanyRepository _companyRepository;
    private readonly IInventoryRepository _inventoryRepository;
    private readonly IUserRepository _userRepository;
    private readonly IEmailService _emailService;

    public ReceiptService(
        ISalesRepository salesRepository,
        ICompanyRepository companyRepository,
        IInventoryRepository inventoryRepository,
        IUserRepository userRepository,
        IEmailService emailService)
    {
        _salesRepository = salesRepository;
        _companyRepository = companyRepository;
        _inventoryRepository = inventoryRepository;
        _userRepository = userRepository;
        _emailService = emailService;
    }

    public async Task<ApiResponse<ReceiptResponse>> GetReceiptAsync(int saleId, int companyId)
    {
        try
        {
            var sale = await _salesRepository.GetByIdAsync(saleId, companyId);
            if (sale == null)
            {
                return ApiResponse<ReceiptResponse>.ErrorResponse($"Sale with ID {saleId} not found");
            }

            var profile = await _companyRepository.GetProfileAsync(companyId);

            string? employeeName = null;
            if (sale.UserId.HasValue && sale.UserId.Value > 0)
            {
                var user = await _userRepository.GetByIdAsync(sale.UserId.Value, companyId, CancellationToken.None);
                if (user != null)
                    employeeName = $"{user.FirstName} {user.LastName}".Trim();
            }

            var lineItems = new List<ReceiptLineItem>();
            foreach (var saleItem in sale.Items)
            {
                var name = saleItem.InventoryItemId.ToString();
                if (saleItem.InventoryItemId > 0)
                {
                    var invItem = await _inventoryRepository.GetByIdAsync(saleItem.InventoryItemId, companyId);
                    if (invItem != null) name = invItem.Name;
                }

                lineItems.Add(new ReceiptLineItem
                {
                    Name = name,
                    Qty = saleItem.Quantity,
                    UnitPrice = saleItem.UnitPrice,
                    LineTotal = saleItem.TotalPrice
                });
            }

            var storeAddress = BuildStoreAddress(profile);

            var receipt = new ReceiptResponse
            {
                ReceiptNumber = sale.Id.ToString("D6"),
                Date = sale.SaleDate,
                StoreName = profile?.CompanyName ?? string.Empty,
                StoreAddress = storeAddress,
                StorePhone = profile?.CompanyPhone,
                Items = lineItems,
                Subtotal = sale.Subtotal,
                TaxLabel = sale.TaxLabel,
                TaxRate = sale.TaxRate,
                TaxAmount = sale.TaxAmount,
                Total = sale.Total,
                PaymentMethod = sale.PaymentMethod,
                EmployeeName = employeeName
            };

            return ApiResponse<ReceiptResponse>.SuccessResponse(receipt);
        }
        catch (Exception ex)
        {
            return ApiResponse<ReceiptResponse>.ErrorResponse(
                "Failed to retrieve receipt",
                new List<string> { ex.Message }
            );
        }
    }

    public async Task<ApiResponse<bool>> SendReceiptEmailAsync(int saleId, int companyId, string toEmail)
    {
        try
        {
            var receiptResponse = await GetReceiptAsync(saleId, companyId);
            if (!receiptResponse.Success || receiptResponse.Data == null)
            {
                return ApiResponse<bool>.ErrorResponse(receiptResponse.Message ?? $"Sale with ID {saleId} not found");
            }

            var result = await _emailService.SendReceiptEmailAsync(toEmail, receiptResponse.Data);
            if (!result.Success)
            {
                return ApiResponse<bool>.ErrorResponse(result.ErrorMessage ?? "Failed to send receipt email");
            }

            return ApiResponse<bool>.SuccessResponse(true, "Receipt email sent successfully");
        }
        catch (Exception ex)
        {
            return ApiResponse<bool>.ErrorResponse(
                "Failed to send receipt email",
                new List<string> { ex.Message }
            );
        }
    }

    private static string? BuildStoreAddress(CompanyProfile? profile)
    {
        if (profile == null) return null;

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(profile.CompanyAddress)) parts.Add(profile.CompanyAddress);

        var cityStateZip = string.Join(", ", new[]
        {
            profile.CompanyCity,
            string.IsNullOrWhiteSpace(profile.CompanyState) ? null
                : (string.IsNullOrWhiteSpace(profile.CompanyZipCode) ? profile.CompanyState : $"{profile.CompanyState} {profile.CompanyZipCode}")
        }.Where(s => !string.IsNullOrWhiteSpace(s)));

        if (!string.IsNullOrWhiteSpace(cityStateZip)) parts.Add(cityStateZip);

        return parts.Count > 0 ? string.Join(", ", parts) : null;
    }
}

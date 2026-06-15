using MyStore.Models;

namespace MyStore.Services;

public interface IPromotionService
{
    Task<ApiResponse<List<Promotion>>> GetAllAsync(int companyId);
    Task<ApiResponse<Promotion>> GetByIdAsync(int id, int companyId);
    Task<ApiResponse<Promotion>> CreateAsync(CreatePromotionRequest request, int companyId);
    Task<ApiResponse<Promotion>> UpdateAsync(int id, UpdatePromotionRequest request, int companyId);
    Task<ApiResponse<bool>> DeleteAsync(int id, int companyId);
    Task<ApiResponse<List<Promotion>>> GetActivePromotionsAsync(int companyId);
    Task<IEnumerable<LineDiscount>> ApplyPromotionsAsync(IEnumerable<CartItem> cartItems, int companyId);
}

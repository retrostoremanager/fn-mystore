using MyStore.Models;

namespace MyStore.Services;

public interface ICompanyService
{
    Task<ApiResponse<RegisterAccountResponse>> RegisterAccountAsync(RegisterAccountRequest request);
}

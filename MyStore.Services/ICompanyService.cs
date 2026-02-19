using MyStore.Models;

namespace MyStore.Services;

public interface ICompanyService
{
    Task<ApiResponse<RegisterAccountResponse>> RegisterAccountAsync(RegisterAccountRequest request);
    Task<ApiResponse<VerifyEmailResponse>> VerifyEmailAsync(string token);
    Task<ApiResponse<ResendVerificationEmailResponse>> ResendVerificationEmailAsync(ResendVerificationEmailRequest request);
}

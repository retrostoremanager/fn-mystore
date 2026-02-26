using MyStore.Models;

namespace MyStore.Services;

public interface ICompanyService
{
    Task<ApiResponse<LoginResponse>> LoginAsync(LoginRequest request);
    Task<ApiResponse<RegisterAccountResponse>> RegisterAccountAsync(RegisterAccountRequest request);
    Task<ApiResponse<VerifyEmailResponse>> VerifyEmailAsync(string token);
    Task<ApiResponse<ResendVerificationEmailResponse>> ResendVerificationEmailAsync(ResendVerificationEmailRequest request);
}

using MyStore.Models;

namespace MyStore.Services;

public interface ICompanyService
{
    Task<ApiResponse<CompanyBySlugResponse?>> GetCompanyBySlugAsync(string slug);
    Task<ApiResponse<LoginResponse>> LoginAsync(LoginRequest request);
    Task<ApiResponse<RegisterAccountResponse>> RegisterAccountAsync(RegisterAccountRequest request);
    Task<ApiResponse<VerifyEmailResponse>> VerifyEmailAsync(string token);
    Task<ApiResponse<ResendVerificationEmailResponse>> ResendVerificationEmailAsync(ResendVerificationEmailRequest request);
    Task<ApiResponse<ForgotPasswordResponse>> ForgotPasswordAsync(ForgotPasswordRequest request);
    Task<ApiResponse<ResetPasswordResponse>> ResetPasswordAsync(ResetPasswordRequest request);
}

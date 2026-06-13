using System.Security.Claims;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using MyStore.Functions.Middleware;

namespace MyStore.Functions.Helpers;

public static class CompanyHelper
{
    private const string UserEmailHeader = "X-User-Email";

    /// <summary>
    /// Extracts the company ID for the current tenant from the validated JWT (set by
    /// JwtAuthenticationMiddleware). The client-supplied X-Company-Id header and companyId
    /// query parameter are intentionally NOT trusted: trusting them allowed any authenticated
    /// user to operate on another company's data by spoofing the header (cross-tenant IDOR).
    /// </summary>
    /// <param name="request">The HTTP request data</param>
    /// <returns>The company ID if the JWT carries one, otherwise null</returns>
    public static int? GetCompanyId(HttpRequestData request)
    {
        return GetCompanyIdFromJwt(request);
    }

    /// <summary>
    /// Attempts to get company ID from JWT claims (set by JwtAuthenticationMiddleware).
    /// </summary>
    private static int? GetCompanyIdFromJwt(HttpRequestData request)
    {
        var context = request.FunctionContext;
        if (context?.Features == null)
            return null;
        var feature = context.Features.Get<JwtPrincipalFeature>();
        return feature?.CompanyId;
    }

    /// <summary>
    /// Extracts the company ID for the current tenant from the validated JWT and throws when
    /// it is absent. This is the single source of truth for tenant scoping on authenticated
    /// endpoints. The company ID is taken ONLY from the JWT (set by JwtAuthenticationMiddleware),
    /// never from the client-supplied X-Company-Id header, so a caller cannot reach another
    /// company's data by spoofing the header.
    /// </summary>
    /// <param name="request">The HTTP request data</param>
    /// <returns>The company ID</returns>
    /// <exception cref="UnauthorizedAccessException">Thrown when the JWT does not carry a company ID</exception>
    public static int GetCompanyIdRequired(HttpRequestData request)
    {
        var companyId = GetCompanyIdFromJwt(request);
        if (companyId.HasValue)
            return companyId.Value;

        throw new UnauthorizedAccessException(
            "Company ID is required and must be derived from the authenticated token.");
    }

    /// <summary>
    /// Gets the user email from JWT claims (set by JwtAuthenticationMiddleware).
    /// Checks ClaimTypes.Email, "email", and "sub" claims.
    /// </summary>
    public static string? GetEmailFromJwt(HttpRequestData request)
    {
        var context = request.FunctionContext;
        return GetEmailFromContext(context);
    }

    /// <summary>
    /// Gets the user email from context (JWT claims) or X-User-Email header.
    /// The header is a fallback when the frontend has email from login but JWT claim extraction fails.
    /// Only used when the request is already authenticated (JWT validated by middleware).
    /// </summary>
    public static string? GetEmailFromRequest(HttpRequestData request, FunctionContext? context)
    {
        var email = GetEmailFromContext(context) ?? GetEmailFromJwt(request);
        if (!string.IsNullOrEmpty(email))
            return email;
        if (request.Headers.TryGetValues(UserEmailHeader, out var values))
        {
            var value = values.FirstOrDefault();
            if (!string.IsNullOrEmpty(value) && value.Contains('@', StringComparison.Ordinal))
                return value.Trim();
        }
        return null;
    }

    /// <summary>
    /// Gets the user email from JWT claims in the function context.
    /// Use when FunctionContext is available directly (e.g. from function parameter).
    /// Checks standard claim types, then falls back to any claim with an email-like value.
    /// </summary>
    public static string? GetEmailFromContext(FunctionContext? context)
    {
        if (context?.Features == null)
            return null;
        var feature = context.Features.Get<JwtPrincipalFeature>();
        var principal = feature?.Principal;
        if (principal == null)
            return null;

        // Standard claim types (JWT, OIDC, .NET)
        var email = principal.FindFirst(ClaimTypes.Email)?.Value
            ?? principal.FindFirst("email")?.Value
            ?? principal.FindFirst("sub")?.Value
            ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? principal.FindFirst("preferred_username")?.Value
            ?? principal.FindFirst("upn")?.Value;

        // Fallback: any claim with a value that looks like an email (handles claim type mapping quirks)
        if (string.IsNullOrEmpty(email))
        {
            email = principal.Claims
                .FirstOrDefault(c => !string.IsNullOrEmpty(c.Value) && c.Value.Contains('@', StringComparison.Ordinal))?
                .Value;
        }

        return string.IsNullOrEmpty(email) ? null : email;
    }
}


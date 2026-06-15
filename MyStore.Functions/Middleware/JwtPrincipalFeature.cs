using System.Security.Claims;

namespace MyStore.Functions.Middleware;

/// <summary>
/// Feature that holds the authenticated JWT principal and extracted claims for the current request.
/// Set by JwtAuthenticationMiddleware after successful token validation.
/// </summary>
public class JwtPrincipalFeature
{
    /// <summary>
    /// The validated claims principal from the JWT.
    /// </summary>
    public ClaimsPrincipal Principal { get; }

    /// <summary>
    /// The company ID extracted from token claims (optional claim for multi-tenant isolation).
    /// Null if the claim is not present.
    /// </summary>
    public int? CompanyId { get; }

    public JwtPrincipalFeature(ClaimsPrincipal principal, int? companyId = null)
    {
        Principal = principal;
        CompanyId = companyId;
    }
}

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using MyStore.Functions.Middleware;

namespace MyStore.Functions.Helpers;

public static class CompanyHelper
{
    private const string CompanyIdHeader = "X-Company-Id";
    private const string CompanyIdQueryParam = "companyId";

    /// <summary>
    /// Extracts the company ID from the HTTP request.
    /// Checks JWT claims first (from auth middleware), then headers (X-Company-Id), then query parameters (companyId).
    /// </summary>
    /// <param name="request">The HTTP request data</param>
    /// <returns>The company ID if found, otherwise null</returns>
    public static int? GetCompanyId(HttpRequestData request)
    {
        var companyId = GetCompanyIdFromJwt(request);
        if (companyId.HasValue)
            return companyId;

        if (request.Headers.TryGetValues(CompanyIdHeader, out var headerValues))
        {
            var headerValue = headerValues.FirstOrDefault();
            if (!string.IsNullOrEmpty(headerValue) && int.TryParse(headerValue, out var headerCompanyId))
                return headerCompanyId;
        }

        var queryValue = request.Query[CompanyIdQueryParam];
        if (!string.IsNullOrEmpty(queryValue) && int.TryParse(queryValue, out var queryCompanyId))
            return queryCompanyId;

        return null;
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
    /// Extracts the company ID from the HTTP request and throws an exception if not found.
    /// </summary>
    /// <param name="request">The HTTP request data</param>
    /// <returns>The company ID</returns>
    /// <exception cref="UnauthorizedAccessException">Thrown when company ID is not provided</exception>
    public static int GetCompanyIdRequired(HttpRequestData request)
    {
        var companyId = GetCompanyId(request);
        if (!companyId.HasValue)
        {
            throw new UnauthorizedAccessException(
                "Company ID is required. Provide a valid JWT with CompanyId claim, or X-Company-Id header, or companyId query parameter.");
        }

        return companyId.Value;
    }
}


using Microsoft.Azure.Functions.Worker.Http;

namespace MyStore.Functions.Helpers;

public static class CompanyHelper
{
    private const string CompanyIdHeader = "X-Company-Id";
    private const string CompanyIdQueryParam = "companyId";

    /// <summary>
    /// Extracts the company ID from the HTTP request.
    /// Checks headers first (X-Company-Id), then query parameters (companyId).
    /// </summary>
    /// <param name="request">The HTTP request data</param>
    /// <returns>The company ID if found, otherwise null</returns>
    public static int? GetCompanyId(HttpRequestData request)
    {
        // Try to get from header first
        if (request.Headers.TryGetValues(CompanyIdHeader, out var headerValues))
        {
            var headerValue = headerValues.FirstOrDefault();
            if (!string.IsNullOrEmpty(headerValue) && int.TryParse(headerValue, out var companyId))
            {
                return companyId;
            }
        }

        // Try to get from query parameter
        var queryValue = request.Query[CompanyIdQueryParam];
        if (!string.IsNullOrEmpty(queryValue) && int.TryParse(queryValue, out var queryCompanyId))
        {
            return queryCompanyId;
        }

        return null;
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
            throw new UnauthorizedAccessException("Company ID is required. Please provide X-Company-Id header or companyId query parameter.");
        }

        return companyId.Value;
    }
}


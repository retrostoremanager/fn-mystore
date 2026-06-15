using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Configuration;

namespace MyStore.Functions.Middleware;

/// <summary>
/// Handles CORS preflight (OPTIONS) and adds CORS headers to responses.
/// Required because Azure Functions platform CORS can fail for credentialed requests.
/// </summary>
public class CorsMiddleware : IFunctionsWorkerMiddleware
{
    private readonly IConfiguration _configuration;

    public CorsMiddleware(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        var httpRequest = await context.GetHttpRequestDataAsync();
        if (httpRequest == null)
        {
            await next(context);
            return;
        }

        var origin = GetOrigin(httpRequest);
        var allowedOrigins = GetAllowedOrigins();

        if (httpRequest.Method.Equals("OPTIONS", System.StringComparison.OrdinalIgnoreCase))
        {
            var response = httpRequest.CreateResponse(HttpStatusCode.NoContent);
            AddCorsHeaders(response, origin, allowedOrigins);
            context.GetInvocationResult().Value = response;
            return;
        }

        await next(context);

        var result = context.GetInvocationResult().Value;
        if (result is HttpResponseData responseData)
        {
            AddCorsHeaders(responseData, origin, allowedOrigins);
        }
    }

    private static string? GetOrigin(HttpRequestData request)
    {
        if (request.Headers.TryGetValues("Origin", out var values))
        {
            return values?.FirstOrDefault();
        }
        return null;
    }

    private HashSet<string> GetAllowedOrigins()
    {
        var corsSetting = _configuration["Cors__AllowedOrigins"] ?? _configuration["CORS"];
        if (string.IsNullOrWhiteSpace(corsSetting))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return corsSetting
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => !string.IsNullOrEmpty(s))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static void AddCorsHeaders(HttpResponseData response, string? origin, HashSet<string> allowedOrigins)
    {
        if (string.IsNullOrEmpty(origin) || allowedOrigins.Count == 0)
            return;

        if (allowedOrigins.Contains("*"))
        {
            response.Headers.Add("Access-Control-Allow-Origin", "*");
            response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, PUT, PATCH, DELETE, OPTIONS");
            response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization, X-Company-Id");
            response.Headers.Add("Access-Control-Max-Age", "86400");
            return;
        }

        if (!allowedOrigins.Contains(origin))
            return;

        response.Headers.Add("Access-Control-Allow-Origin", origin);
        response.Headers.Add("Access-Control-Allow-Credentials", "true");
        response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, PUT, PATCH, DELETE, OPTIONS");
        response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization, X-Company-Id");
        response.Headers.Add("Access-Control-Max-Age", "86400");
    }
}

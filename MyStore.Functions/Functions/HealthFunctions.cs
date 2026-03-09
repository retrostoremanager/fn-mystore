using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
namespace MyStore.Functions;

/// <summary>Health and diagnostic endpoints. No auth required.</summary>
public class HealthFunctions
{
    private readonly IConfiguration _configuration;
    private readonly ILogger _logger;

    public HealthFunctions(IConfiguration configuration, ILoggerFactory loggerFactory)
    {
        _configuration = configuration;
        _logger = loggerFactory.CreateLogger<HealthFunctions>();
    }

    [Function("Health")]
    public async Task<HttpResponseData> Health(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequestData req)
    {
        var clientId = _configuration["IGDB_CLIENT_ID"] ?? Environment.GetEnvironmentVariable("IGDB_CLIENT_ID");
        var hasSecret = !string.IsNullOrWhiteSpace(
            _configuration["IGDB_CLIENT_SECRET"] ?? Environment.GetEnvironmentVariable("IGDB_CLIENT_SECRET"));
        var configured = !string.IsNullOrWhiteSpace(clientId)
            && !string.Equals(clientId, "not-configured", StringComparison.OrdinalIgnoreCase)
            && hasSecret;

        var body = new { igdbConfigured = configured };
        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await response.WriteStringAsync(JsonSerializer.Serialize(body, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        return response;
    }
}

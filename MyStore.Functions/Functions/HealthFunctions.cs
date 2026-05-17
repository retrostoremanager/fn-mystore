using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
namespace MyStore.Functions;

/// <summary>Health and diagnostic endpoints. No auth required.</summary>
public class HealthFunctions
{
    private readonly ILogger _logger;

    public HealthFunctions(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<HealthFunctions>();
    }

    [Function("Health")]
    public async Task<HttpResponseData> Health(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequestData req)
    {
        _logger.LogInformation("Health check requested");

        var body = new { status = "healthy", version = "1.0" };
        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await response.WriteStringAsync(JsonSerializer.Serialize(body, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        return response;
    }
}

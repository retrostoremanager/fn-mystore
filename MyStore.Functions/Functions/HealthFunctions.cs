using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
namespace MyStore.Functions;

/// <summary>Health and diagnostic endpoints. No auth required.</summary>
public class HealthFunctions
{
    private readonly ILogger _logger;
    private readonly IConfiguration _configuration;

    public HealthFunctions(ILoggerFactory loggerFactory, IConfiguration configuration)
    {
        _logger = loggerFactory.CreateLogger<HealthFunctions>();
        _configuration = configuration;
    }

    [Function("Health")]
    public async Task<HttpResponseData> Health(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequestData req)
    {
        _logger.LogInformation("Health check requested");

        var connectionString = _configuration["ConnectionStrings__DefaultConnection"];

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            _logger.LogError("Health check failed: ConnectionStrings__DefaultConnection is not configured");
            var degradedBody = new { status = "degraded", version = "1.0", database = "connection string not configured" };
            var degradedResponse = req.CreateResponse(HttpStatusCode.ServiceUnavailable);
            degradedResponse.Headers.Add("Content-Type", "application/json; charset=utf-8");
            await degradedResponse.WriteStringAsync(JsonSerializer.Serialize(degradedBody, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            return degradedResponse;
        }

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await connection.CloseAsync();

            var body = new { status = "healthy", version = "1.0", database = "connected" };
            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/json; charset=utf-8");
            await response.WriteStringAsync(JsonSerializer.Serialize(body, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            return response;
        }
        catch (PostgresException pgEx) when (pgEx.SqlState == "3D000")
        {
            _logger.LogError(pgEx, "Health check failed: database does not exist. SqlState={SqlState}", pgEx.SqlState);
            var errorBody = new { status = "unhealthy", version = "1.0", database = $"database does not exist — create the database and run migrations. ({pgEx.SqlState}: {pgEx.MessageText})" };
            var errorResponse = req.CreateResponse(HttpStatusCode.ServiceUnavailable);
            errorResponse.Headers.Add("Content-Type", "application/json; charset=utf-8");
            await errorResponse.WriteStringAsync(JsonSerializer.Serialize(errorBody, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            return errorResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed: database connection error");
            var errorBody = new { status = "unhealthy", version = "1.0", database = $"connection failed: {ex.Message}" };
            var errorResponse = req.CreateResponse(HttpStatusCode.ServiceUnavailable);
            errorResponse.Headers.Add("Content-Type", "application/json; charset=utf-8");
            await errorResponse.WriteStringAsync(JsonSerializer.Serialize(errorBody, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            return errorResponse;
        }
    }
}

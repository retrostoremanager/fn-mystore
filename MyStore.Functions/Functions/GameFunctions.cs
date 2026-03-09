using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using MyStore.Functions.Attributes;
using MyStore.Functions.Helpers;
using MyStore.Models;
using MyStore.Services;

namespace MyStore.Functions;

[RequirePermission("inventory.view")] // Games search used when adding inventory
public class GameFunctions
{
    private readonly IGameService _gameService;
    private readonly ILogger _logger;

    public GameFunctions(IGameService gameService, ILoggerFactory loggerFactory)
    {
        _gameService = gameService;
        _logger = loggerFactory.CreateLogger<GameFunctions>();
    }

    [Function("SearchGames")]
    public async Task<HttpResponseData> SearchGames(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "games/search")] HttpRequestData req)
    {
        try
        {
            _ = CompanyHelper.GetCompanyIdRequired(req); // Require auth
            var query = req.Query["q"] ?? "";
            _logger.LogInformation("Searching games with query: {Query}", query);

            var response = await _gameService.SearchGamesAsync(query);
            return await CreateHttpResponse(req, response);
        }
        catch (UnauthorizedAccessException ex)
        {
            var errorResponse = ApiResponse<List<Game>>.ErrorResponse(ex.Message);
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.Unauthorized);
        }
    }

    private static async Task<HttpResponseData> CreateHttpResponse<T>(
        HttpRequestData req,
        ApiResponse<T> apiResponse,
        HttpStatusCode? statusCode = null)
    {
        var response = req.CreateResponse(statusCode ?? (apiResponse.Success ? HttpStatusCode.OK : HttpStatusCode.BadRequest));
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");

        var json = JsonSerializer.Serialize(apiResponse, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await response.WriteStringAsync(json);
        return response;
    }
}

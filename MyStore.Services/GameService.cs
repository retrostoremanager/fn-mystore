using Microsoft.Extensions.Logging;
using MyStore.Models;
using MyStore.Repositories;

namespace MyStore.Services;

public class GameService : IGameService
{
    private readonly IGameRepository _gameRepository;
    private readonly ILogger<GameService> _logger;

    public GameService(IGameRepository gameRepository, ILogger<GameService> logger)
    {
        _gameRepository = gameRepository;
        _logger = logger;
    }

    public async Task<ApiResponse<List<Game>>> SearchGamesAsync(string query)
    {
        try
        {
            var searchTerm = query ?? "";
            var localGames = await _gameRepository.SearchAsync(searchTerm);
            _logger.LogInformation(
                "Game search \"{Query}\": local={LocalCount}",
                searchTerm, localGames.Count);
            return ApiResponse<List<Game>>.SuccessResponse(localGames);
        }
        catch (Exception ex)
        {
            return ApiResponse<List<Game>>.ErrorResponse(
                "Failed to search games",
                new List<string> { ex.Message }
            );
        }
    }
}

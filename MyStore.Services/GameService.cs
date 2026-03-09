using MyStore.Models;
using MyStore.Repositories;

namespace MyStore.Services;

public class GameService : IGameService
{
    private readonly IGameRepository _gameRepository;
    private readonly IIgdbService _igdbService;

    public GameService(IGameRepository gameRepository, IIgdbService igdbService)
    {
        _gameRepository = gameRepository;
        _igdbService = igdbService;
    }

    public async Task<ApiResponse<List<Game>>> SearchGamesAsync(string query)
    {
        try
        {
            var localGames = await _gameRepository.SearchAsync(query ?? "");

            // If game is in encyclopedia, return local results
            if (localGames.Count > 0)
                return ApiResponse<List<Game>>.SuccessResponse(localGames);

            // Not in encyclopedia: search IGDB; game will be added when user adds to inventory
            var igdbGames = await _igdbService.SearchAsync(query ?? "", limit: 20);
            return ApiResponse<List<Game>>.SuccessResponse(igdbGames);
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

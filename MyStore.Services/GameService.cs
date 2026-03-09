using MyStore.Models;
using MyStore.Repositories;

namespace MyStore.Services;

public class GameService : IGameService
{
    private readonly IGameRepository _gameRepository;

    public GameService(IGameRepository gameRepository)
    {
        _gameRepository = gameRepository;
    }

    public async Task<ApiResponse<List<Game>>> SearchGamesAsync(string query)
    {
        try
        {
            var games = await _gameRepository.SearchAsync(query ?? "");
            return ApiResponse<List<Game>>.SuccessResponse(games);
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

using Microsoft.Extensions.Logging;
using MyStore.Models;
using MyStore.Repositories;

namespace MyStore.Services;

public class GameService : IGameService
{
    private readonly IGameRepository _gameRepository;
    private readonly IIgdbService _igdbService;
    private readonly ILogger<GameService> _logger;

    public GameService(IGameRepository gameRepository, IIgdbService igdbService, ILogger<GameService> logger)
    {
        _gameRepository = gameRepository;
        _igdbService = igdbService;
        _logger = logger;
    }

    public async Task<ApiResponse<List<Game>>> SearchGamesAsync(string query)
    {
        try
        {
            var searchTerm = query ?? "";
            var localGames = await _gameRepository.SearchAsync(searchTerm);
            var igdbGames = await _igdbService.SearchAsync(searchTerm, limit: 20);

            // Merge: local encyclopedia first (games already added), then IGDB, deduped by id
            var localIds = new HashSet<string>(localGames.Select(g => g.Id), StringComparer.OrdinalIgnoreCase);
            var merged = localGames.ToList();
            foreach (var g in igdbGames)
            {
                if (!string.IsNullOrEmpty(g.Id) && !localIds.Contains(g.Id))
                {
                    merged.Add(g);
                    localIds.Add(g.Id);
                }
            }
            _logger.LogInformation(
                "Game search \"{Query}\": local={LocalCount}, igdb={IgdbCount}, merged={MergedCount}",
                searchTerm, localGames.Count, igdbGames.Count, merged.Count);
            return ApiResponse<List<Game>>.SuccessResponse(merged);
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

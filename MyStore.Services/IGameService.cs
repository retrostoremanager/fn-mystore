using MyStore.Models;

namespace MyStore.Services;

public interface IGameService
{
    Task<ApiResponse<List<Game>>> SearchGamesAsync(string query);
}

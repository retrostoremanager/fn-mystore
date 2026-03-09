using MyStore.Models;

namespace MyStore.Repositories;

public interface IGameRepository
{
    /// <summary>Inserts or updates a game. Ensures the game exists for game_inventory FK.</summary>
    Task UpsertAsync(Game game);

    /// <summary>Searches games by title, console, or genre. Returns only games that exist in the DB.</summary>
    Task<List<Game>> SearchAsync(string query);
}

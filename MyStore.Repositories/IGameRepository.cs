using MyStore.Models;

namespace MyStore.Repositories;

public interface IGameRepository
{
    /// <summary>Inserts or updates a game. Ensures the game exists for inventory item FK.</summary>
    Task UpsertAsync(Game game);
}

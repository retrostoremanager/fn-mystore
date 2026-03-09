using MyStore.Models;

namespace MyStore.Services;

/// <summary>IGDB (Internet Game Database) integration via Twitch API. Used when game is not in local encyclopedia.</summary>
public interface IIgdbService
{
    /// <summary>Searches IGDB for games. Returns empty list if not configured or on error.</summary>
    Task<List<Game>> SearchAsync(string query, int limit = 20);
}

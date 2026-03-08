using Dapper;
using Npgsql;
using MyStore.Models;

namespace MyStore.Repositories;

public class GameRepository : IGameRepository
{
    private readonly string _connectionString;

    public GameRepository()
    {
        _connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? Environment.GetEnvironmentVariable("PostgresConnectionString")
            ?? throw new InvalidOperationException("Connection string environment variable is not set");
    }

    public async Task UpsertAsync(Game game)
    {
        if (string.IsNullOrWhiteSpace(game.Id) || string.IsNullOrWhiteSpace(game.Title) || string.IsNullOrWhiteSpace(game.Console))
            throw new ArgumentException("Game Id, Title, and Console are required");

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.ExecuteAsync(
            @"INSERT INTO game (id, title, console, release_date, publisher, genre, image_url)
              VALUES (@p_id, @p_title, @p_console, @p_release_date, @p_publisher, @p_genre, @p_image_url)
              ON CONFLICT (id) DO UPDATE SET
                title = EXCLUDED.title,
                console = EXCLUDED.console,
                release_date = EXCLUDED.release_date,
                publisher = EXCLUDED.publisher,
                genre = EXCLUDED.genre,
                image_url = EXCLUDED.image_url",
            new
            {
                p_id = game.Id,
                p_title = game.Title,
                p_console = game.Console,
                p_release_date = game.ReleaseDate?.Date,
                p_publisher = game.Publisher,
                p_genre = game.Genre,
                p_image_url = game.ImageUrl
            });
    }
}

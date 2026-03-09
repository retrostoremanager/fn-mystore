using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MyStore.Models;

namespace MyStore.Services;

/// <summary>IGDB API v4 integration. Requires Twitch Client ID and Secret (IGDB uses Twitch OAuth).</summary>
public class IgdbService : IIgdbService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<IgdbService> _logger;
    private readonly string? _clientId;
    private readonly string? _clientSecret;
    private string? _cachedToken;
    private DateTime _tokenExpiry = DateTime.MinValue;

    private const string TokenUrl = "https://id.twitch.tv/oauth2/token";
    private const string IgdbUrl = "https://api.igdb.com/v4/games";

    public IgdbService(HttpClient httpClient, ILogger<IgdbService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _clientId = Environment.GetEnvironmentVariable("IGDB_CLIENT_ID")
            ?? Environment.GetEnvironmentVariable("Twitch__ClientId");
        _clientSecret = Environment.GetEnvironmentVariable("IGDB_CLIENT_SECRET")
            ?? Environment.GetEnvironmentVariable("Twitch__ClientSecret");
    }

    private bool IsConfigured() =>
        !string.IsNullOrWhiteSpace(_clientId) && !string.IsNullOrWhiteSpace(_clientSecret)
        && !string.Equals(_clientId, "not-configured", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(_clientSecret, "not-configured", StringComparison.OrdinalIgnoreCase);

    public async Task<List<Game>> SearchAsync(string query, int limit = 20)
    {
        if (!IsConfigured())
        {
            _logger.LogDebug("IGDB not configured (missing or placeholder IGDB_CLIENT_ID/IGDB_CLIENT_SECRET)");
            return [];
        }

        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2)
            return [];

        try
        {
            var token = await GetAccessTokenAsync();
            if (string.IsNullOrEmpty(token))
                return [];

            var searchQuery = query.Trim().Replace("\"", "\\\"");
            // No category filter - IGDB search can miss games (e.g. Silent Hill) when filter is too restrictive
            var body = $"""
                search "{searchQuery}";
                fields id,name,first_release_date,genres.name,platforms.name,involved_companies.company.name,cover.url;
                limit {limit};
                """;

            using var request = new HttpRequestMessage(HttpMethod.Post, IgdbUrl);
            request.Headers.Add("Client-ID", _clientId);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = new StringContent(body, Encoding.UTF8, "text/plain");

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var igdbGames = JsonSerializer.Deserialize<List<IgdbGame>>(json);
            if (igdbGames == null || igdbGames.Count == 0)
                return [];

            return igdbGames.Select(MapToGame).Where(g => !string.IsNullOrEmpty(g.Title)).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "IGDB search failed for query {Query}", query);
            return [];
        }
    }

    private async Task<string?> GetAccessTokenAsync()
    {
        if (!string.IsNullOrEmpty(_cachedToken) && DateTime.UtcNow < _tokenExpiry)
            return _cachedToken;

        try
        {
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("client_id", _clientId!),
                new KeyValuePair<string, string>("client_secret", _clientSecret!),
                new KeyValuePair<string, string>("grant_type", "client_credentials")
            });

            var response = await _httpClient.PostAsync(TokenUrl, content);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(json);
            if (tokenResponse?.AccessToken == null)
                return null;

            _cachedToken = tokenResponse.AccessToken;
            _tokenExpiry = DateTime.UtcNow.AddSeconds(Math.Max(0, tokenResponse.ExpiresIn - 60));
            return _cachedToken;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get IGDB/Twitch access token");
            return null;
        }
    }

    private static Game MapToGame(IgdbGame ig)
    {
        var platform = ig.Platforms?.FirstOrDefault()?.Name ?? "Unknown";
        var genre = ig.Genres?.FirstOrDefault()?.Name;
        var publisher = ig.InvolvedCompanies?.FirstOrDefault()?.Company?.Name;
        var imageUrl = ig.Cover?.Url != null
            ? (ig.Cover.Url.StartsWith("//") ? "https:" + ig.Cover.Url : ig.Cover.Url)
            : null;

        return new Game
        {
            Id = "igdb_" + ig.Id,
            Title = ig.Name ?? "",
            Console = platform,
            ReleaseDate = ig.FirstReleaseDate.HasValue
                ? DateTimeOffset.FromUnixTimeSeconds(ig.FirstReleaseDate.Value).UtcDateTime
                : null,
            Publisher = publisher,
            Genre = genre,
            ImageUrl = imageUrl
        };
    }

    private sealed class TokenResponse
    {
        public string? AccessToken { get; set; }
        public int ExpiresIn { get; set; }
    }

    private sealed class IgdbGame
    {
        [System.Text.Json.Serialization.JsonPropertyName("id")]
        public int Id { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("name")]
        public string? Name { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("first_release_date")]
        public long? FirstReleaseDate { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("genres")]
        public List<IgdbGenre>? Genres { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("platforms")]
        public List<IgdbPlatform>? Platforms { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("involved_companies")]
        public List<IgdbInvolvedCompany>? InvolvedCompanies { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("cover")]
        public IgdbCover? Cover { get; set; }
    }

    private sealed class IgdbGenre { [System.Text.Json.Serialization.JsonPropertyName("name")] public string? Name { get; set; } }
    private sealed class IgdbPlatform { [System.Text.Json.Serialization.JsonPropertyName("name")] public string? Name { get; set; } }
    private sealed class IgdbCover { [System.Text.Json.Serialization.JsonPropertyName("url")] public string? Url { get; set; } }
    private sealed class IgdbInvolvedCompany { [System.Text.Json.Serialization.JsonPropertyName("company")] public IgdbCompany? Company { get; set; } }
    private sealed class IgdbCompany { [System.Text.Json.Serialization.JsonPropertyName("name")] public string? Name { get; set; } }
}

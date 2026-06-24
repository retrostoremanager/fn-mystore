using System.Collections.Specialized;
using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Moq;
using MyStore.Functions;
using MyStore.Models;
using MyStore.Services;
using MyStore.Tests.Helpers;
using Xunit;

namespace MyStore.Tests.Functions;

public class GameFunctionsTests
{
    private readonly Mock<IGameService> _gameServiceMock;
    private readonly Mock<ILoggerFactory> _loggerFactoryMock;
    private readonly Mock<ILogger<GameFunctions>> _loggerMock;
    private readonly GameFunctions _functions;

    public GameFunctionsTests()
    {
        _gameServiceMock = new Mock<IGameService>();
        _loggerMock = new Mock<ILogger<GameFunctions>>();
        _loggerFactoryMock = new Mock<ILoggerFactory>();

        _loggerFactoryMock
            .Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(_loggerMock.Object);

        _functions = new GameFunctions(_gameServiceMock.Object, _loggerFactoryMock.Object);
    }

    private static HttpRequestData CreateAuthenticatedRequest(NameValueCollection? query = null)
    {
        var context = TestHelpers.CreateMockFunctionContextWithJwt(1);
        var headers = new Dictionary<string, string> { ["X-Company-Id"] = "1" };
        return TestHelpers.CreateHttpRequestData(context, null, headers, query);
    }

    private static List<Game> CreateSampleGames() =>
    [
        new Game
        {
            Id = "igdb-1",
            Title = "Super Mario Bros",
            Console = "NES",
            ReleaseDate = new DateTime(1985, 9, 13),
            Publisher = "Nintendo",
            Genre = "Platformer",
            ImageUrl = "https://example.com/mario.jpg"
        },
        new Game
        {
            Id = "igdb-2",
            Title = "Zelda: A Link to the Past",
            Console = "SNES",
            ReleaseDate = new DateTime(1991, 11, 21),
            Publisher = "Nintendo",
            Genre = "Action-Adventure",
            ImageUrl = "https://example.com/zelda.jpg"
        }
    ];

    #region SearchGames — authenticated requests

    [Fact]
    public async Task SearchGames_WithResults_Returns200OK()
    {
        var games = CreateSampleGames();
        var apiResponse = ApiResponse<List<Game>>.SuccessResponse(games);

        _gameServiceMock
            .Setup(s => s.SearchGamesAsync(It.IsAny<string>()))
            .ReturnsAsync(apiResponse);

        var query = new NameValueCollection { ["q"] = "mario" };
        var httpRequestData = CreateAuthenticatedRequest(query);

        var result = await _functions.SearchGames(httpRequestData);

        result.StatusCode.Should().Be(HttpStatusCode.OK);

        var responseBody = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<List<Game>>>(
            responseBody,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized.Should().NotBeNull();
        deserialized!.Success.Should().BeTrue();
        deserialized.Data.Should().NotBeNull();
        deserialized.Data!.Should().HaveCount(2);
        deserialized.Data![0].Title.Should().Be("Super Mario Bros");
    }

    [Fact]
    public async Task SearchGames_EmptyQuery_Returns200WithEmptyList()
    {
        var apiResponse = ApiResponse<List<Game>>.SuccessResponse(new List<Game>());

        _gameServiceMock
            .Setup(s => s.SearchGamesAsync(It.IsAny<string>()))
            .ReturnsAsync(apiResponse);

        var httpRequestData = CreateAuthenticatedRequest();

        var result = await _functions.SearchGames(httpRequestData);

        result.StatusCode.Should().Be(HttpStatusCode.OK);

        var responseBody = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<List<Game>>>(
            responseBody,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized.Should().NotBeNull();
        deserialized!.Success.Should().BeTrue();
        deserialized.Data.Should().NotBeNull();
        deserialized.Data!.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchGames_NoMatchingResults_Returns200WithEmptyList()
    {
        var apiResponse = ApiResponse<List<Game>>.SuccessResponse(new List<Game>());

        _gameServiceMock
            .Setup(s => s.SearchGamesAsync("nonexistentgametitle12345"))
            .ReturnsAsync(apiResponse);

        var query = new NameValueCollection { ["q"] = "nonexistentgametitle12345" };
        var httpRequestData = CreateAuthenticatedRequest(query);

        var result = await _functions.SearchGames(httpRequestData);

        result.StatusCode.Should().Be(HttpStatusCode.OK);

        var responseBody = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<List<Game>>>(
            responseBody,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized.Should().NotBeNull();
        deserialized!.Success.Should().BeTrue();
        deserialized.Data.Should().NotBeNull();
        deserialized.Data!.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchGames_PassesQueryToService()
    {
        var apiResponse = ApiResponse<List<Game>>.SuccessResponse(new List<Game>());

        _gameServiceMock
            .Setup(s => s.SearchGamesAsync("zelda"))
            .ReturnsAsync(apiResponse);

        var query = new NameValueCollection { ["q"] = "zelda" };
        var httpRequestData = CreateAuthenticatedRequest(query);

        await _functions.SearchGames(httpRequestData);

        _gameServiceMock.Verify(s => s.SearchGamesAsync("zelda"), Times.Once);
    }

    [Fact]
    public async Task SearchGames_ServiceReturnsError_Returns400BadRequest()
    {
        var apiResponse = ApiResponse<List<Game>>.ErrorResponse("Search service unavailable");

        _gameServiceMock
            .Setup(s => s.SearchGamesAsync(It.IsAny<string>()))
            .ReturnsAsync(apiResponse);

        var query = new NameValueCollection { ["q"] = "mario" };
        var httpRequestData = CreateAuthenticatedRequest(query);

        var result = await _functions.SearchGames(httpRequestData);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var responseBody = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<List<Game>>>(
            responseBody,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized.Should().NotBeNull();
        deserialized!.Success.Should().BeFalse();
        deserialized.Message.Should().Contain("Search service unavailable");
    }

    #endregion

    #region SearchGames — unauthenticated / unauthorized

    [Fact]
    public async Task SearchGames_NoCompanyId_Returns401Unauthorized()
    {
        var context = new Mock<FunctionContext>();
        var query = new NameValueCollection { ["q"] = "mario" };
        var httpRequestData = TestHelpers.CreateHttpRequestData(context.Object, null, null, query);

        var result = await _functions.SearchGames(httpRequestData);

        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var responseBody = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<List<Game>>>(
            responseBody,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized.Should().NotBeNull();
        deserialized!.Success.Should().BeFalse();
    }

    [Fact]
    public async Task SearchGames_Unauthenticated_DoesNotCallService()
    {
        var context = new Mock<FunctionContext>();
        var query = new NameValueCollection { ["q"] = "mario" };
        var httpRequestData = TestHelpers.CreateHttpRequestData(context.Object, null, null, query);

        await _functions.SearchGames(httpRequestData);

        _gameServiceMock.Verify(s => s.SearchGamesAsync(It.IsAny<string>()), Times.Never);
    }

    #endregion

    #region SearchGames — response shape

    [Fact]
    public async Task SearchGames_ResponseHasCorrectContentType()
    {
        var apiResponse = ApiResponse<List<Game>>.SuccessResponse(CreateSampleGames());

        _gameServiceMock
            .Setup(s => s.SearchGamesAsync(It.IsAny<string>()))
            .ReturnsAsync(apiResponse);

        var httpRequestData = CreateAuthenticatedRequest();

        var result = await _functions.SearchGames(httpRequestData);

        result.Headers.Should().ContainKey("Content-Type");
        result.Headers.GetValues("Content-Type").Should().Contain("application/json; charset=utf-8");
    }

    [Fact]
    public async Task SearchGames_ResponseUsesCamelCase()
    {
        var apiResponse = ApiResponse<List<Game>>.SuccessResponse(CreateSampleGames());

        _gameServiceMock
            .Setup(s => s.SearchGamesAsync(It.IsAny<string>()))
            .ReturnsAsync(apiResponse);

        var httpRequestData = CreateAuthenticatedRequest();

        var result = await _functions.SearchGames(httpRequestData);

        var responseBody = await TestHelpers.ReadResponseBody(result);
        responseBody.Should().Contain("success");
        responseBody.Should().Contain("data");
        responseBody.Should().NotContain("\"Success\"");
        responseBody.Should().NotContain("\"Data\"");
    }

    [Fact]
    public async Task SearchGames_GameFieldsAreCamelCase()
    {
        var apiResponse = ApiResponse<List<Game>>.SuccessResponse(CreateSampleGames());

        _gameServiceMock
            .Setup(s => s.SearchGamesAsync(It.IsAny<string>()))
            .ReturnsAsync(apiResponse);

        var httpRequestData = CreateAuthenticatedRequest();

        var result = await _functions.SearchGames(httpRequestData);

        var responseBody = await TestHelpers.ReadResponseBody(result);
        responseBody.Should().Contain("\"title\"");
        responseBody.Should().Contain("\"console\"");
        responseBody.Should().NotContain("\"Title\"");
        responseBody.Should().NotContain("\"Console\"");
    }

    [Fact]
    public async Task SearchGames_ResultWithNullableFields_SerializesCorrectly()
    {
        var games = new List<Game>
        {
            new Game
            {
                Id = "igdb-99",
                Title = "Obscure Homebrew",
                Console = "Atari 2600",
                ReleaseDate = null,
                Publisher = null,
                Genre = null,
                ImageUrl = null
            }
        };
        var apiResponse = ApiResponse<List<Game>>.SuccessResponse(games);

        _gameServiceMock
            .Setup(s => s.SearchGamesAsync(It.IsAny<string>()))
            .ReturnsAsync(apiResponse);

        var query = new NameValueCollection { ["q"] = "obscure" };
        var httpRequestData = CreateAuthenticatedRequest(query);

        var result = await _functions.SearchGames(httpRequestData);

        result.StatusCode.Should().Be(HttpStatusCode.OK);

        var responseBody = await TestHelpers.ReadResponseBody(result);
        var deserialized = JsonSerializer.Deserialize<ApiResponse<List<Game>>>(
            responseBody,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized.Should().NotBeNull();
        deserialized!.Success.Should().BeTrue();
        deserialized.Data.Should().NotBeNull();
        deserialized.Data!.Should().HaveCount(1);
        deserialized.Data![0].Publisher.Should().BeNull();
        deserialized.Data![0].Genre.Should().BeNull();
        deserialized.Data![0].ImageUrl.Should().BeNull();
        deserialized.Data![0].ReleaseDate.Should().BeNull();
    }

    #endregion

    #region SearchGames — logging

    [Fact]
    public async Task SearchGames_LogsSearchQuery()
    {
        var apiResponse = ApiResponse<List<Game>>.SuccessResponse(new List<Game>());

        _gameServiceMock
            .Setup(s => s.SearchGamesAsync(It.IsAny<string>()))
            .ReturnsAsync(apiResponse);

        var query = new NameValueCollection { ["q"] = "sonic" };
        var httpRequestData = CreateAuthenticatedRequest(query);

        await _functions.SearchGames(httpRequestData);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("sonic")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion
}

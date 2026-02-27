using System.Collections.Specialized;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Moq;

namespace MyStore.Tests.Helpers;

public static class TestHelpers
{
    public static HttpRequestData CreateHttpRequestData(FunctionContext? context = null, object? body = null)
    {
        var functionContext = context ?? CreateMockFunctionContext();
        var request = new MockHttpRequestData(functionContext);
        
        if (body != null)
        {
            var json = JsonSerializer.Serialize(body);
            var bytes = Encoding.UTF8.GetBytes(json);
            request.SetBody(bytes);
        }
        
        return request;
    }

    /// <summary>
    /// Creates HttpRequestData with optional headers and query parameters.
    /// </summary>
    public static HttpRequestData CreateHttpRequestData(
        FunctionContext? context,
        object? body,
        IReadOnlyDictionary<string, string>? headers,
        NameValueCollection? query = null)
    {
        var functionContext = context ?? CreateMockFunctionContext();
        var request = new MockHttpRequestData(functionContext, headers, query);
        
        if (body != null)
        {
            var json = JsonSerializer.Serialize(body);
            var bytes = Encoding.UTF8.GetBytes(json);
            request.SetBody(bytes);
        }
        
        return request;
    }

    /// <summary>
    /// Creates HttpRequestData with raw body string and optional headers.
    /// Use for webhooks where the body is not JSON-serialized from an object.
    /// </summary>
    public static HttpRequestData CreateHttpRequestDataWithRawBody(
        string rawBody,
        IReadOnlyDictionary<string, string>? headers = null,
        FunctionContext? context = null)
    {
        var functionContext = context ?? CreateMockFunctionContext();
        var request = new MockHttpRequestData(functionContext, headers, null);
        var bytes = Encoding.UTF8.GetBytes(rawBody);
        request.SetBody(bytes);
        return request;
    }

    public static FunctionContext CreateMockFunctionContext()
    {
        var mock = new Mock<FunctionContext>();
        return mock.Object;
    }

    public static async Task<string> ReadResponseBody(HttpResponseData response)
    {
        if (response is MockHttpResponseData mockResponse)
        {
            return mockResponse.BodyString;
        }
        
        response.Body.Position = 0;
        using var reader = new StreamReader(response.Body, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }
}

public class MockHttpRequestData : HttpRequestData
{
    private Stream _body;
    private readonly HttpHeadersCollection _headers;
    private readonly NameValueCollection _query;

    public MockHttpRequestData(FunctionContext functionContext) : this(functionContext, null, null)
    {
    }

    public MockHttpRequestData(
        FunctionContext functionContext,
        IReadOnlyDictionary<string, string>? headers = null,
        NameValueCollection? query = null) : base(functionContext)
    {
        _body = new MemoryStream();
        _headers = new HttpHeadersCollection();
        if (headers != null)
        {
            foreach (var (key, value) in headers)
                _headers.TryAddWithoutValidation(key, value);
        }
        _query = query ?? new NameValueCollection();
    }

    public override Stream Body => _body;
    
    public override HttpHeadersCollection Headers => _headers;
    
    public override IReadOnlyCollection<IHttpCookie> Cookies => new List<IHttpCookie>();
    
    public override Uri Url { get; } = new Uri("https://localhost/api/");
    
    public override NameValueCollection Query => _query;
    
    public override string Method { get; } = "POST";
    
    public override IEnumerable<ClaimsIdentity> Identities => new List<ClaimsIdentity>();

    public override HttpResponseData CreateResponse()
    {
        return new MockHttpResponseData(FunctionContext);
    }

    public void SetBody(byte[] data)
    {
        _body = new MemoryStream(data);
    }
}

public class MockHttpResponseData : HttpResponseData
{
    private Stream _body;
    private readonly HttpHeadersCollection _headers;

    public MockHttpResponseData(FunctionContext functionContext) : base(functionContext)
    {
        _body = new MemoryStream();
        _headers = new HttpHeadersCollection();
    }

    public override HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;
    
    public override HttpHeadersCollection Headers 
    { 
        get => _headers;
        set { } // Headers are read-only in our mock
    }
    
    public override Stream Body 
    { 
        get => _body;
        set => _body = value;
    }
    
    public override HttpCookies Cookies
    {
        get
        {
            // Create a mock HttpCookies using Moq
            var mockCookies = new Mock<HttpCookies>();
            return mockCookies.Object;
        }
    }
    
    public string BodyString
    {
        get
        {
            _body.Position = 0;
            using var reader = new StreamReader(_body, Encoding.UTF8);
            return reader.ReadToEnd();
        }
    }

    // WriteStringAsync is an extension method, not an override
    // We'll handle writing in our test helper
    public void WriteString(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        _body.Write(bytes, 0, bytes.Length);
        _body.Position = 0;
    }
}

// Use actual HttpHeadersCollection - it's a concrete class that can be instantiated
// We'll use it directly in the mock classes

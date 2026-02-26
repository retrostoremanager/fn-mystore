using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;

namespace MyStore.Functions.Middleware;

/// <summary>
/// Middleware that validates JWT Bearer tokens from Microsoft Entra External ID.
/// For protected endpoints: validates token, extracts claims, and stores principal in Features.
/// For [AllowAnonymous] endpoints: skips validation.
/// Returns 401 Unauthorized when token is missing or invalid on protected endpoints.
/// </summary>
public class JwtAuthenticationMiddleware : IFunctionsWorkerMiddleware
{
    private static readonly HashSet<string> AnonymousFunctionNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "RegisterAccount",
        "VerifyEmail",
        "ResendVerification",
        "Login",
        "ForgotPassword",
        "ResetPassword"
    };

    private readonly IConfiguration _configuration;
    private readonly ILogger<JwtAuthenticationMiddleware> _logger;
    private readonly JwtSecurityTokenHandler _tokenHandler;

    public JwtAuthenticationMiddleware(IConfiguration configuration, ILogger<JwtAuthenticationMiddleware> logger)
    {
        _configuration = configuration;
        _logger = logger;
        _tokenHandler = new JwtSecurityTokenHandler();
    }

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        var functionName = context.FunctionDefinition.Name;
        if (IsAnonymousFunction(context, functionName))
        {
            await next(context);
            return;
        }

        var httpRequest = await context.GetHttpRequestDataAsync();
        if (httpRequest == null)
        {
            await next(context);
            return;
        }

        if (!TryGetTokenFromRequest(context, out var token))
        {
            await ReturnUnauthorized(context, httpRequest, "Authorization header with Bearer token is required.");
            return;
        }

        if (!_tokenHandler.CanReadToken(token))
        {
            await ReturnUnauthorized(context, httpRequest, "Invalid token format.");
            return;
        }

        // Custom JWT (MyStore) - check first for MVP
        var customSecretKey = _configuration["JwtAuthentication__SecretKey"] ?? _configuration["JwtSecret"];
        if (!string.IsNullOrEmpty(customSecretKey) && !string.IsNullOrEmpty(token))
        {
            if (TryValidateCustomJwt(context, token!, customSecretKey))
            {
                await next(context);
                return;
            }
            await ReturnUnauthorized(context, httpRequest, "Invalid or expired token.");
            return;
        }

        // Entra External ID validation (when custom JWT not configured)
        var authority = _configuration["EntraExternalId__Authority"]
            ?? _configuration["AuthenticationAuthority"];
        var audience = _configuration["EntraExternalId__ClientId"]
            ?? _configuration["AuthenticationClientId"];
        var companyIdClaim = _configuration["EntraExternalId__CompanyIdClaim"] ?? "extension_CompanyId";

        if (string.IsNullOrEmpty(authority) || string.IsNullOrEmpty(audience))
        {
            _logger.LogWarning("JWT authentication not configured. Skipping validation for development.");
            await next(context);
            return;
        }

        var configManager = new ConfigurationManager<OpenIdConnectConfiguration>(
            $"{authority.TrimEnd('/')}/.well-known/openid-configuration",
            new OpenIdConnectConfigurationRetriever());

        var validationParameters = new TokenValidationParameters
        {
            ValidAudience = audience,
            ValidateAudience = true,
            ValidateIssuer = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(2)
        };

        try
        {
            var openIdConfig = await configManager.GetConfigurationAsync(context.CancellationToken);
            validationParameters.ValidIssuer = openIdConfig.Issuer;
            validationParameters.IssuerSigningKeys = openIdConfig.SigningKeys;

            var principal = _tokenHandler.ValidateToken(token, validationParameters, out _);
            var companyId = ExtractCompanyId(principal, companyIdClaim);

            context.Features.Set(new JwtPrincipalFeature(principal, companyId));
            await next(context);
        }
        catch (SecurityTokenExpiredException)
        {
            await ReturnUnauthorized(context, httpRequest, "Token has expired.");
        }
        catch (SecurityTokenException ex)
        {
            _logger.LogWarning(ex, "Token validation failed for function {FunctionName}", functionName);
            await ReturnUnauthorized(context, httpRequest, "Invalid or expired token.");
        }
    }

    private bool TryValidateCustomJwt(FunctionContext context, string token, string secretKey)
    {
        try
        {
            var key = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = key,
                ValidIssuer = "MyStore",
                ValidAudience = "MyStore",
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(2)
            };

            var principal = _tokenHandler.ValidateToken(token, validationParameters, out _);
            var companyIdClaim = _configuration["EntraExternalId__CompanyIdClaim"] ?? "extension_CompanyId";
            var companyId = ExtractCompanyId(principal, companyIdClaim);

            context.Features.Set(new JwtPrincipalFeature(principal, companyId));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsAnonymousFunction(FunctionContext context, string functionName)
    {
        if (AnonymousFunctionNames.Contains(functionName))
            return true;

        var method = GetTargetFunctionMethod(context);
        if (method?.GetCustomAttribute<AllowAnonymousAttribute>() != null)
            return true;
        if (method?.DeclaringType?.GetCustomAttribute<AllowAnonymousAttribute>() != null)
            return true;

        return false;
    }

    private static MethodInfo? GetTargetFunctionMethod(FunctionContext context)
    {
        var entryPoint = context.FunctionDefinition.EntryPoint;
        if (string.IsNullOrEmpty(entryPoint))
            return null;

        var path = context.FunctionDefinition.PathToAssembly;
        if (string.IsNullOrEmpty(path))
            return null;

        try
        {
            var assembly = Assembly.LoadFrom(path);
            var lastDot = entryPoint.LastIndexOf('.');
            if (lastDot < 0)
                return null;

            var typeName = entryPoint[..lastDot];
            var methodName = entryPoint[(lastDot + 1)..];
            var type = assembly.GetType(typeName);
            return type?.GetMethod(methodName);
        }
        catch
        {
            return null;
        }
    }

    private static bool TryGetTokenFromRequest(FunctionContext context, out string? token)
    {
        token = null;
        if (!context.BindingContext.BindingData.TryGetValue("Headers", out var headersObj) ||
            headersObj is not string headersStr)
            return false;

        var headers = JsonSerializer.Deserialize<Dictionary<string, string>>(headersStr);
        if (headers == null)
            return false;

        var normalized = headers.ToDictionary(h => h.Key.ToLowerInvariant(), h => h.Value);
        if (!normalized.TryGetValue("authorization", out var authHeader) ||
            string.IsNullOrEmpty(authHeader))
            return false;

        const string bearerPrefix = "bearer ";
        if (!authHeader.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        token = authHeader[bearerPrefix.Length..].Trim();
        return !string.IsNullOrEmpty(token);
    }

    private static int? ExtractCompanyId(ClaimsPrincipal principal, string claimName)
    {
        var claim = principal.FindFirst(claimName)
            ?? principal.FindFirst(c => string.Equals(c.Type, claimName, StringComparison.OrdinalIgnoreCase));

        if (claim == null || string.IsNullOrEmpty(claim.Value))
            return null;

        return int.TryParse(claim.Value, out var id) ? id : null;
    }

    private static async Task ReturnUnauthorized(
        FunctionContext context,
        Microsoft.Azure.Functions.Worker.Http.HttpRequestData request,
        string message)
    {
        var response = request.CreateResponse(HttpStatusCode.Unauthorized);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        response.Headers.Add("WWW-Authenticate", "Bearer");

        var body = JsonSerializer.Serialize(new { error = message });
        await response.WriteStringAsync(body);

        context.GetInvocationResult().Value = response;
    }
}

using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyStore.Functions.Attributes;
using MyStore.Functions.Helpers;
using MyStore.Repositories;
using MyStore.Services;

namespace MyStore.Functions.Middleware;

/// <summary>
/// Middleware that enforces permission checks for functions decorated with [RequirePermission].
/// Runs after JwtAuthenticationMiddleware and CompanyAccessMiddleware.
/// Returns 403 Forbidden when user lacks the required permission.
/// </summary>
public class RbacMiddleware : IFunctionsWorkerMiddleware
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RbacMiddleware> _logger;

    public RbacMiddleware(IServiceProvider serviceProvider, ILogger<RbacMiddleware> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        var permission = GetRequiredPermission(context);
        if (string.IsNullOrEmpty(permission))
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

        var companyId = CompanyHelper.GetCompanyId(httpRequest);
        var email = CompanyHelper.GetEmailFromJwt(httpRequest);

        // Fallback: when using X-Company-Id header (e.g. tests), resolve company owner email
        if (companyId.HasValue && string.IsNullOrEmpty(email))
        {
            using var scope2 = _serviceProvider.CreateScope();
            var companyRepo = scope2.ServiceProvider.GetRequiredService<ICompanyRepository>();
            var company = await companyRepo.GetByIdAsync(companyId.Value);
            email = company?.Email;
        }

        if (!companyId.HasValue || string.IsNullOrEmpty(email))
        {
            _logger.LogWarning("RBAC: Cannot check permission {Permission} - missing companyId or email", permission);
            await ReturnForbidden(context, httpRequest, "Authentication context is required for permission check.");
            return;
        }

        using var scope = _serviceProvider.CreateScope();
        var permissionService = scope.ServiceProvider.GetRequiredService<IPermissionService>();
        var hasPermission = await permissionService.HasPermissionAsync(companyId.Value, email, permission, context.CancellationToken);

        if (!hasPermission)
        {
            _logger.LogWarning("RBAC: User {Email} (company {CompanyId}) denied - missing permission {Permission}", email, companyId, permission);
            await ReturnForbidden(context, httpRequest, "You do not have permission to perform this action.");
            return;
        }

        await next(context);
    }

    private static string? GetRequiredPermission(FunctionContext context)
    {
        var method = GetTargetFunctionMethod(context);
        if (method == null)
            return null;

        var attr = method.GetCustomAttribute<RequirePermissionAttribute>()
            ?? method.DeclaringType?.GetCustomAttribute<RequirePermissionAttribute>();
        return attr?.Permission;
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

    private static async Task ReturnForbidden(FunctionContext context, HttpRequestData request, string message)
    {
        var response = request.CreateResponse(HttpStatusCode.Forbidden);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");

        var body = JsonSerializer.Serialize(new { error = message, code = "FORBIDDEN" });
        await response.WriteStringAsync(body);

        context.GetInvocationResult().Value = response;
    }
}

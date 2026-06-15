using System;

namespace MyStore.Functions.Attributes;

/// <summary>
/// Specifies that the function requires the given permission to be invoked.
/// When applied, RbacMiddleware checks the user has the permission before allowing the request.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
public class RequirePermissionAttribute : Attribute
{
    public string Permission { get; }

    public RequirePermissionAttribute(string permission)
    {
        Permission = permission ?? throw new ArgumentNullException(nameof(permission));
    }
}

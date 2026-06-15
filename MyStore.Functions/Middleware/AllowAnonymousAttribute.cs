namespace MyStore.Functions.Middleware;

/// <summary>
/// When applied to a function, JWT authentication is skipped.
/// Use for public endpoints such as account registration, email verification, and resend verification.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class AllowAnonymousAttribute : Attribute
{
}

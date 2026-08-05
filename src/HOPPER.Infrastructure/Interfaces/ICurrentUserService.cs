namespace HOPPER.Infrastructure.Interfaces
{
    /// <summary>The acting admin, resolved from the OIDC token. Declared here and implemented in the
    /// API so Infrastructure stays decoupled from ASP.NET Core.</summary>
    public interface ICurrentUserService
    {
        /// <summary>Display name from the "name" claim, or null outside an authenticated request
        /// (background work, tests, the design-time factory).</summary>
        string? Name { get; }
    }
}

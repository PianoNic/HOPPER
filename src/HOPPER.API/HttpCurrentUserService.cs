using System.Security.Claims;
using HOPPER.Infrastructure.Interfaces;

namespace HOPPER.API
{
    /// <summary>HttpContext-backed implementation. Reads preferred_username from the bearer token,
    /// falling back to name and then email. Scoped so it picks up the per-request HttpContext.</summary>
    public class HttpCurrentUserService(IHttpContextAccessor accessor) : ICurrentUserService
    {
        public string? Name
        {
            get
            {
                var user = accessor.HttpContext?.User;
                return user?.FindFirstValue("preferred_username")
                    ?? user?.FindFirstValue(ClaimTypes.Name)
                    ?? user?.FindFirstValue(ClaimTypes.Email);
            }
        }
    }
}

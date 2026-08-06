using System.Security.Claims;
using HOPPER.Infrastructure.Interfaces;

namespace HOPPER.API
{
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

using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HOPPER.Infrastructure.Services
{
    public static class UniqueViolation
    {
        public static bool IsUniqueViolation(this DbUpdateException exception) =>
            exception.InnerException is PostgresException { SqlState: "23505" };
    }
}

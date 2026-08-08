using HOPPER.Domain;

namespace HOPPER.Application
{
    public sealed class InvalidRequestException(string message) : RuleViolationException(message);
}

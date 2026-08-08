namespace HOPPER.Domain
{
    // The only hierarchy that answers 4xx, so an ArgumentException stays the 500 it is. In Domain
    // because Infrastructure raises these too and cannot see Application.
    public abstract class RuleViolationException(string message) : Exception(message);
}

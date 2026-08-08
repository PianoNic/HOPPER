namespace HOPPER.Domain
{
    // Every way a caller can be wrong, and the only hierarchy that answers 4xx. An ArgumentException
    // from the framework or from a contract guard stays the 500 it is.
    //
    // In Domain rather than Application because Infrastructure raises them too and cannot see
    // Application. Keeping one root is what makes the status table's subtype arms unreachable-checked
    // by the compiler instead of by memory.
    public abstract class RuleViolationException(string message) : Exception(message);
}

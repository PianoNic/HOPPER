namespace HOPPER.Application
{
    // ArgumentException-derived while UploadModsCommand and PackImporter still catch that to fail a
    // single file instead of the whole request. Once no bare throw is left, this can stand alone.
    public abstract class RuleViolationException(string message) : ArgumentException(message);
}

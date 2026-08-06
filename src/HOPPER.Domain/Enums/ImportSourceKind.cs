namespace HOPPER.Domain.Enums
{
    /// <summary>Where the bytes of a pack import came from. Persisted as an int.</summary>
    public enum ImportSourceKind
    {
        /// <summary>The admin uploaded the file; it is staged before the worker sees it.</summary>
        Upload = 0,

        /// <summary>The admin pasted a URL; the worker fetches it, not the request thread.</summary>
        Url = 1,
    }
}

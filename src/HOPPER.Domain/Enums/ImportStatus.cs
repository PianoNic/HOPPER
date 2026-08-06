namespace HOPPER.Domain.Enums
{
    /// <summary>Lifecycle of one import job. Persisted as an int.</summary>
    public enum ImportStatus
    {
        /// <summary>Row written, bytes staged, id handed to the worker queue.</summary>
        Queued = 0,

        /// <summary>The worker owns it. Counters move while this is the state, which is what the
        /// dashboard polls on.</summary>
        Running = 1,

        /// <summary>The worker finished. An import that produced pending entries is still completed:
        /// pendings are expected output, not failure.</summary>
        Completed = 2,

        /// <summary>Staging, detection or manifest parsing failed, so no per-file work was possible.
        /// Error carries the reason.</summary>
        Failed = 3,
    }
}

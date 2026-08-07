namespace HOPPER.Domain.Enums
{
    /// <summary>
    /// Which side is asking to be synced. Not the same thing as <see cref="ModSide"/>: a mod may
    /// belong on both, a caller is always exactly one.
    /// </summary>
    public enum SyncSide
    {
        Client = 0,

        Server = 1,
    }
}

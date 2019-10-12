namespace MinecraftVersionDownloader.All
{
    public enum VersionType
    {
        Snapshot,
        Release,
        Beta,
        Alpha,
#pragma warning disable CA1707 // Identifiers should not contain underscores
        Old_Beta = Beta,
        Old_Alpha = Alpha
#pragma warning restore CA1707 // Identifiers should not contain underscores
    }
}

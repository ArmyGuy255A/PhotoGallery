namespace PhotoGallery.Tests;

/// <summary>
/// Forces serial execution of test classes that mutate process-wide
/// environment variables consumed by <c>Program.cs</c> at host build time
/// (notably <c>BasePath</c> and <c>DISABLE_AUTH</c>). xUnit parallelizes
/// test classes by default; without this collection, two classes setting
/// <c>BasePath</c> to different values race and the loser sees the wrong
/// configured prefix when its WebApplicationFactory builds its host.
///
/// Membership:
///   - <see cref="BasePathRoutingTests"/> (Story S1)
///   - <see cref="SignalRHubPathBaseTests"/> (Story S2)
/// Any future test class that constructs a <c>WebApplicationFactory&lt;Program&gt;</c>
/// with a non-default <c>BasePath</c> env var MUST join this collection.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public class BasePathEnvVarsCollection
{
    public const string Name = "BasePathEnvVars";
}

namespace StarPush.Tests;

/// <summary>
/// The integration tests that share the throwaway App Database in <c>STARPUSH_TEST_APP_DATABASE</c>. One runs at a
/// time: a Disconnect in one class would otherwise delete the rows another is in the middle of using.
/// </summary>
[CollectionDefinition(Name)]
public sealed class AppDatabaseCollection {
    public const string Name = "App Database";
}

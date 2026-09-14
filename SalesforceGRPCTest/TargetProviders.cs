using Application.Targets;
using Database.Repositories.Interfaces;
using NSubstitute;

namespace SalesforceGRPCTest;

/// <summary>
/// Wraps a substituted Target Database repository in a provider that always hands it back, so tests written
/// against the repository keep asserting against the repository.
/// </summary>
internal static class TargetProviders {
    public static ITargetConnectionProvider Of(IRepository repository) {
        var provider = Substitute.For<ITargetConnectionProvider>();
        provider.GetRepositoryAsync(Arg.Any<CancellationToken>()).Returns(repository);
        return provider;
    }
}

using Avalonia;
using Avalonia.Headless.XUnit;
using Xunit.Sdk;
using Xunit.v3;

[assembly: TestCollectionOrderer(typeof(Filee.App.Tests.AvaloniaBootstrapCollectionOrderer))]

namespace Filee.App.Tests;

internal static class AvaloniaBootstrapCollection
{
    public const string Name = "Avalonia shared dispatcher bootstrap";
}

[CollectionDefinition(AvaloniaBootstrapCollection.Name, DisableParallelization = true)]
public sealed class AvaloniaBootstrapCollectionDefinition { }

/// <summary>
/// Avalonia.Headless.XUnit creates its per-assembly dispatcher lazily on the first AvaloniaFact.
/// Start that session before ordinary Facts can create AvaloniaObjects on a different worker thread.
/// </summary>
[Collection(AvaloniaBootstrapCollection.Name)]
public sealed class AvaloniaBootstrapTests
{
    [AvaloniaFact]
    public void Shared_headless_application_starts_before_other_test_collections()
    {
        Assert.NotNull(Application.Current);
    }
}

/// <summary>Runs the one headless bootstrap collection before any tests outside Avalonia's dispatcher.</summary>
public sealed class AvaloniaBootstrapCollectionOrderer : ITestCollectionOrderer
{
    public IReadOnlyCollection<TTestCollection> OrderTestCollections<TTestCollection>(
        IReadOnlyCollection<TTestCollection> testCollections)
        where TTestCollection : ITestCollection =>
        testCollections
            .OrderBy(collection => string.Equals(
                collection.TestCollectionDisplayName,
                AvaloniaBootstrapCollection.Name,
                StringComparison.Ordinal) ? 0 : 1)
            .ToArray();
}

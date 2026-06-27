using SearchLite.CosmosDB.Tests.Fixtures;

namespace SearchLite.CosmosDB.Tests;

/// <summary>
/// Runs the shared conformance suite (<see cref="SearchLite.Tests.IndexTests"/>) against a real
/// Cosmos DB emulator instance provided by <see cref="CosmosDbFixture"/>.
///
/// Requires Docker + the Cosmos emulator image to execute; see the fixture for details.
/// </summary>
public class IndexTests(CosmosDbFixture fixture)
    : SearchLite.Tests.IndexTests(fixture.Manager), IClassFixture<CosmosDbFixture>
{
}

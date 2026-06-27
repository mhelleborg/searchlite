using System.Diagnostics.CodeAnalysis;
using Microsoft.Azure.Cosmos;
using SearchLite.CosmosDB;
using Testcontainers.CosmosDb;

namespace SearchLite.CosmosDB.Tests.Fixtures;

/// <summary>
/// Spins up the Azure Cosmos DB emulator in a container and exposes a <see cref="SearchManager"/>
/// wired to it.
///
/// The emulator serves HTTPS with a self-signed certificate, so the client is built from the
/// container-provided <see cref="CosmosDbContainer.HttpClientFactory"/> (which trusts that cert)
/// rather than from a raw connection string.
///
/// Running this fixture requires a Docker daemon and the (large, slow-to-start) Cosmos emulator
/// image; it is intended for CI/integration runs, not the default local compile-only flow.
/// </summary>
[Collection("cosmosdb")]
public class CosmosDbFixture : IAsyncLifetime
{
    private readonly CosmosDbContainer _container;

    [field: AllowNull, MaybeNull]
    public SearchManager Manager => field ??= CreateManager();

    public CosmosDbFixture()
    {
        _container = new CosmosDbBuilder("mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:latest")
            .Build();
    }

    private SearchManager CreateManager()
    {
        var options = new CosmosClientOptions
        {
            // Route through the emulator-aware HttpClient so its self-signed certificate is accepted.
            HttpClientFactory = () => _container.HttpClient,
            ConnectionMode = ConnectionMode.Gateway,
            UseSystemTextJsonSerializerWithOptions = new System.Text.Json.JsonSerializerOptions(
                System.Text.Json.JsonSerializerDefaults.Web)
        };

        var client = new CosmosClient(_container.GetConnectionString(), options);
        return new SearchManager(client, databaseId: "searchlite_tests", ownsClient: true);
    }

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.StopAsync();
}

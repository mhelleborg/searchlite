using Microsoft.Extensions.DependencyInjection;

namespace SearchLite.CosmosDB;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Cosmos DB backed <see cref="ISearchEngineManager"/> from a connection string.
    /// </summary>
    public static IServiceCollection AddSearch<T>(this IServiceCollection services,
        string connectionString, string databaseId = "searchlite") where T : ISearchableDocument
    {
        services.AddSingleton<ISearchEngineManager>(_ => new SearchManager(connectionString, databaseId));
        return services;
    }
}

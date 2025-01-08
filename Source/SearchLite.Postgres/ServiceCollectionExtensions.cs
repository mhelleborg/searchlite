using Microsoft.Extensions.DependencyInjection;

namespace SearchLite.Postgres;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSearch<T>(this IServiceCollection services,
        string connectionString) where T : ISearchableDocument
    {
        services.AddSingleton<ISearchEngineManager>(_ => new SearchManager(connectionString));
        return services;
    }
}
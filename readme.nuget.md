# SearchLite

SearchLite is a lightweight .NET library for adding full-text search capabilities to SQLite and PostgreSQL databases. It offers a unified, strongly-typed, and async-friendly API, making it easy to index and query documents without the need for a dedicated search engine.

## Installation

```bash
dotnet add package SearchLite
```

## Quick Start

1. Define your searchable document:

```csharp
public class Product : ISearchableDocument
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public decimal Price { get; set; }
    public string GetSearchText() => $"{Id} {Name} {Description}";
}
```

2. Create a search manager and index your documents:

```csharp
// For PostgreSQL
var searchManager = new PgSearchManager("your_connection_string");

// For SQLite
var searchManager = new SqliteSearchManager("your_connection_string");

var productIndex = await searchManager.Get<Product>("products", CancellationToken.None);

await productIndex.IndexAsync(new Product 
{
    Id = "1",
    Name = "Gaming Laptop",
    Description = "High-performance gaming laptop with RTX 4080",
    Price = 1999.99m
});
```

3. Search for documents:

```csharp
var request = new SearchRequest<Product>
{
    Query = "gaming laptop",
    Options = new SearchOptions { Take = 10, MinScore = 0.5f }
}.Where(p => p.Price < 2000);

var results = await productIndex.SearchAsync(request);

foreach (var match in results.Matches)
{
    Console.WriteLine($"Found: {match.Document.Name} (Score: {match.Score})");
}
```

## Filtering

LINQ-style `Where` filters support nested document fields and collection membership, across
both backends:

```csharp
.Where(p => p.Maker.HeadOffice.City == "Oslo")   // nested fields, any depth
.Where(p => p.Tags.Contains("sale"))             // array/list membership
```

On PostgreSQL these compile to JSONB containment (`@>`) and use the GIN index for fast lookups.

## Learn More

For detailed documentation, advanced usage, and contributing guidelines, visit the [SearchLite GitHub Repository](https://github.com/mhelleborg/searchlite).

---

Licensed under the [MIT License](https://github.com/mhelleborg/searchlite/blob/main/LICENSE).
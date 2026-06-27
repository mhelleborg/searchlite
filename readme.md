# SearchLite

<img src="logo.svg" alt="SearchLite Logo" width="280">

SearchLite is a lightweight .NET library that provides full-text search capabilities on SQLite and PostgreSQL databases. It offers a simple, unified interface for creating, managing, and querying search indexes without the complexity of dedicated search engines.

## Features

- Unified search interface for SQLite and Postgres
- Strongly-typed document indexing and querying
- Flexible filtering with LINQ-style expressions
- Configurable search options including scoring and result limits
- Async-first API design
- Minimal dependencies

## Installation

```bash
dotnet add package SearchLite
```

## Quick Start

1. First, define your searchable document by implementing `ISearchableDocument`:

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

2. Create a search manager for your database:

```csharp
// For PostgreSQL
var searchManager = new PgSearchManager("your_connection_string");

// For SQLite
var searchManager = new SqliteSearchManager("your_connection_string");
```

3. Get or create a search index:

```csharp
var productIndex = await searchManager.Get<Product>("products", CancellationToken.None);
```

4. Index some documents:

```csharp
var product = new Product 
{
    Id = "1",
    Name = "Gaming Laptop",
    Description = "High-performance gaming laptop with RTX 4080",
    Price = 1999.99m
};

await productIndex.IndexAsync(product);
```

5. Search for documents:

```csharp
var request = new SearchRequest<Product>
{
    Query = "gaming laptop",
    Options = new SearchOptions
    {
        MaxResults = 10,
        MinScore = 0.5f
    }
}.Where(p => p.Price < 2000);

var results = await productIndex.SearchAsync(request);

foreach (var match in results.Matches)
{
    Console.WriteLine($"Found: {match.Document.Name} (Score: {match.Score})");
}
```

## Search Options

The `SearchOptions` class allows you to customize your search behavior:

- `MaxResults`: Maximum number of results to return (default: 100)
- `MinScore`: Minimum relevance score for matches (default: 0.0)
- `IncludeRawDocument`: Whether to include the full document in results (default: true)

## Filtering

SearchLite supports LINQ-style filtering using the `Where` method:

```csharp
var request = new SearchRequest<Product>
{
    Query = "laptop"
}
.Where(p => p.Price >= 1000 && p.Price <= 2000);
```

### Nested fields

Filters and ordering can reach into nested objects within a document, at any depth:

```csharp
// Documents can hold nested objects and collections
public class Product : ISearchableDocument
{
    public string Id { get; set; }
    public string Name { get; set; }
    public Manufacturer Maker { get; set; }   // nested object
    public List<string> Tags { get; set; }    // collection field
    public string GetSearchText() => $"{Id} {Name}";
}

public class Manufacturer
{
    public string Name { get; set; }
    public Address HeadOffice { get; set; }
}

// Filter and order by nested fields
var request = new SearchRequest<Product>()
    .Where(p => p.Maker.Name == "Acme" && p.Maker.HeadOffice.City == "Oslo")
    .OrderByAscending(p => p.Maker.Name);
```

### Collection (array) membership

Use `Contains` on a document's own collection field to match documents whose array
holds a given value:

```csharp
// Match products tagged "sale"
.Where(p => p.Tags.Contains("sale"))

// Negation works too
.Where(p => !p.Tags.Contains("discontinued"))
```

> This is distinct from `In`-style filters, where a fixed set is tested against a single
> field — e.g. `var ids = new[] { "1", "2" }; request.Where(p => ids.Contains(p.Id));`

### Efficient execution on PostgreSQL

On PostgreSQL, equality and collection-membership filters compile to JSONB containment
(`document @> '{...}'`), which is served by the `GIN(jsonb_path_ops)` index SearchLite
creates on every index — so these filters stay fast as collections grow. Range and string
comparisons use JSON path extraction. SQLite provides the same query capabilities using
`json_extract` and `json_each`.

## Advanced Usage

### Batch Indexing

For better performance when indexing multiple documents:

```csharp
var products = new List<Product> { /* ... */ };
await productIndex.IndexManyAsync(products);
```

### Index Management

```csharp
// Delete a single document
await productIndex.DeleteAsync("doc_id");

// Drop the entire index
await productIndex.DropIndexAsync();
```

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## License

[MIT License](LICENSE)
# SearchLite

<img src="logo.svg" alt="SearchLite Logo" width="280">

SearchLite is a lightweight .NET library that provides full-text search capabilities on SQLite and PostgreSQL databases. It offers a simple, unified interface for creating, managing, and querying search indexes without the complexity of dedicated search engines.

## Features

- Unified search interface for SQLite and Postgres
- Strongly-typed document indexing and querying
- Flexible filtering with LINQ-style expressions, including nested fields and collection predicates (`Any`, `All`, `Count`)
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
        Take = 10,
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

- `Take`: Maximum number of results to return (default: 100)
- `Skip`: Number of results to skip, for paging (default: 0)
- `MinScore`: Minimum relevance score for matches (default: 0.0). Not comparable across providers.
- `IncludeRawDocument`: Whether to include the full document in results (default: true)
- `IncludePartialMatches`: Whether the query matches on individual terms rather than the full query (default: true)

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

### Collection predicates (`Any` / `All`)

Filter on the elements of a collection, including collections of objects:

```csharp
public class Order : ISearchableDocument
{
    public string Id { get; set; }
    public List<OrderLine> Lines { get; set; }
    public List<string> Tags { get; set; }
    public string GetSearchText() => Id;
}

public class OrderLine
{
    public string Sku { get; set; }
    public int Quantity { get; set; }
    public decimal Price { get; set; }
    public bool Shipped { get; set; }
    public List<Note> Notes { get; set; }
}

// At least one line priced above 100
.Where(o => o.Lines.Any(l => l.Price > 100))

// All conditions must hold for the *same* element
.Where(o => o.Lines.Any(l => l.Sku == "ABC" && l.Quantity >= 2))

// Every line shipped (vacuously true for an empty collection)
.Where(o => o.Lines.All(l => l.Shipped))

// Non-empty / empty collections
.Where(o => o.Lines.Any())
.Where(o => !o.Tags.Any())

// Predicates over collections of scalars use the element directly
.Where(o => o.Tags.Any(t => t.StartsWith("promo-")))

// Nested collections, at any depth
.Where(o => o.Lines.Any(l => l.Notes.Any(n => n.Rating >= 4)))
```

Element predicates accept everything a top-level filter does: comparisons, string operators,
`Contains`, `In`-style sets, nested fields, nested `Any`/`All`, counts and negation. A predicate
cannot refer back to the enclosing document (`o.Lines.Any(l => l.Price > o.Budget)` throws
`NotSupportedException`). `List<T>.Exists` / `TrueForAll` are accepted as aliases for `Any` / `All`.

### Collection size

```csharp
.Where(o => o.Tags.Count > 2)                    // List.Count, array.Length or Count()
.Where(o => o.Lines.Count(l => l.Shipped) >= 1)  // count of matching elements
```

A null or missing collection counts as zero.

### Matching against a set of values

```csharp
var wanted = new[] { "sale", "clearance" };

.Where(o => o.Tags.Any(t => wanted.Contains(t)))   // has any of the values
.Where(o => wanted.Any(w => o.Tags.Contains(w)))   // same thing, written the other way round
.Where(o => wanted.All(w => o.Tags.Contains(w)))   // has all of the values
```

Quantifiers over a constant set are expanded at mapping time into an `OR` (`Any`) or `AND`
(`All`) of per-value filters.

### Negation and nullable fields

Any filter can be negated with `!`, including groups and collection predicates. Nullable
fields can be tested with `HasValue` / `Value`.

```csharp
.Where(o => !(o.Total > 100 || o.Tags.Contains("sale")))
.Where(o => !o.Lines.Any(l => l.Backordered))
.Where(o => o.ShippedAt.HasValue)
```

### Not supported

The following are rejected with a `NotSupportedException` rather than translated incorrectly:

- Comparing two document fields to each other (`o.Total > o.Budget`)
- Referencing the outer document inside a collection predicate
- Members of scalar types (`o.Name.Length`, `o.Created.Year`) and method calls on fields
  other than the documented ones (`o.Name.ToUpper() == "X"`)
- Dictionary indexers (`o.Attributes["color"]`) and case-insensitive `Equals`

### Efficient execution on PostgreSQL

On PostgreSQL, equality and collection-membership filters compile to JSONB containment
(`document @> '{...}'`), which is served by the `GIN(jsonb_path_ops)` index SearchLite
creates on every index — so these filters stay fast as collections grow. This includes
`Any` predicates made of equalities and membership checks: `o.Lines.Any(l => l.Sku == "ABC"
&& l.Quantity == 2)` becomes `document @> '{"Lines":[{"Sku":"ABC","Quantity":2}]}'`. Range
and string comparisons, `All`, and counts use `jsonb_array_elements` subqueries and JSON path
extraction. SQLite, DuckDB and MariaDB provide the same query capabilities using their JSON
functions (`json_each`, `unnest` and `JSON_TABLE` respectively).

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
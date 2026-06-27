using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace SearchLite.CosmosDB;

/// <summary>
/// The stored envelope for a searchable document inside a Cosmos container.
///
/// The original document is kept verbatim as a JSON object under <see cref="Doc"/> (so filtering can
/// address its fields with <c>c.doc["Field"]</c>), the full-text input is denormalized into
/// <see cref="SearchText"/> (the property the container's full-text index is built on), and
/// <see cref="Id"/> doubles as both the Cosmos item id and the partition key value.
/// </summary>
internal sealed class CosmosDocument<T> where T : ISearchableDocument
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("doc")]
    public JsonObject? Doc { get; set; }

    [JsonPropertyName("searchText")]
    public string SearchText { get; set; } = "";

    [JsonPropertyName("lastUpdated")]
    public DateTimeOffset LastUpdated { get; set; }

    public static CosmosDocument<T> From(T document, JsonSerializerOptions options)
    {
        var node = JsonSerializer.SerializeToNode(document, options) as JsonObject;
        return new CosmosDocument<T>
        {
            Id = document.Id,
            Doc = node,
            SearchText = document.GetSearchText(),
            LastUpdated = DateTimeOffset.UtcNow
        };
    }

    public T? Deserialize(JsonSerializerOptions options) =>
        Doc is null ? default : Doc.Deserialize<T>(options);
}

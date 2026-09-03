using FluentAssertions;

namespace SearchLite.Tests;

/// <summary>
/// Behavioral capability contract for collection-based filters: quantifiers (Any/All) over
/// collections of objects and scalars, nested quantifiers, counts, constant-set membership and
/// generic negation. Every case runs against each backend and is checked against in-memory LINQ.
/// </summary>
public abstract partial class IndexTests
{
    private static TestDocument[] CollectionDocs() =>
    [
        new TestDocument
        {
            Id = "1", Title = "Doc 1", Views = 1, Valid = true, CreatedAt = DateTime.UtcNow,
            Labels = ["red", "urgent"], Scores = [1, 2],
            Items =
            [
                new LineItem
                {
                    Sku = "A", Quantity = 2, Price = 10m, Shipped = true, Priority = Priority.High, Danger = DangerZone.High,
                    Tags = ["x", "y"], Notes = [new Note { Text = "good", Rating = 5 }]
                },
                new LineItem
                {
                    Sku = "B", Quantity = 1, Price = 25m, Shipped = false, Priority = Priority.Low, Danger = DangerZone.Low,
                    Tags = ["y"], Notes = []
                }
            ]
        },
        new TestDocument
        {
            Id = "2", Title = "Doc 2", Views = 2, Valid = false, CreatedAt = DateTime.UtcNow,
            Labels = ["red"], Scores = [2, 3],
            Items =
            [
                new LineItem
                {
                    Sku = "C", Quantity = 5, Price = 3m, Shipped = true, Priority = Priority.Medium, Danger = DangerZone.Medium,
                    Tags = [], Notes = [new Note { Text = "meh", Rating = 2 }, new Note { Text = "ok", Rating = 3 }]
                }
            ]
        },
        new TestDocument
        {
            Id = "3", Title = "Doc 3", Views = 3, CreatedAt = DateTime.UtcNow,
            Labels = [], Scores = [], Items = []
        },
        new TestDocument
        {
            Id = "4", Title = "Doc 4", Views = 4, CreatedAt = DateTime.UtcNow,
            Labels = ["green", "urgent"], Scores = [3],
            Items =
            [
                new LineItem
                {
                    Sku = "A", Quantity = 1, Price = 12m, Shipped = false, Priority = Priority.Low, Danger = DangerZone.Low,
                    Tags = ["z"], Notes = []
                },
                new LineItem
                {
                    Sku = "D", Quantity = 3, Price = 7m, Shipped = true, Priority = Priority.Critical, Danger = DangerZone.Archer,
                    Tags = ["x"], Notes = [new Note { Text = "great", Rating = 5 }]
                }
            ]
        },
        new TestDocument
        {
            Id = "5", Title = "Doc 5", Views = 5, CreatedAt = DateTime.UtcNow,
            Labels = ["blue"], Scores = [5, 2],
            Items =
            [
                new LineItem
                {
                    Sku = "E", Quantity = 1, Price = 100m, Shipped = true, Priority = Priority.High, Danger = DangerZone.Medium,
                    Tags = ["y", "z"], Notes = []
                }
            ]
        }
    ];

    // ------------------------------------------------------------------ Any over objects

    [Fact]
    public async Task Any_Equality_ShouldFilter()
    {
        var docs = CollectionDocs();
        await Index.IndexManyAsync(docs);
        await ShouldReturnSameResultsAsLinq(docs, d => d.Items.Any(i => i.Sku == "A"), 2);
    }

    [Fact]
    public async Task Any_Comparison_ShouldFilter()
    {
        var docs = CollectionDocs();
        await Index.IndexManyAsync(docs);
        await ShouldReturnSameResultsAsLinq(docs, d => d.Items.Any(i => i.Price > 20m), 2);
        await ShouldReturnSameResultsAsLinq(docs, d => d.Items.Any(i => i.Quantity >= 3), 2);
    }

    [Fact]
    public async Task Any_CompoundPredicate_MatchesWithinTheSameElement()
    {
        var docs = CollectionDocs();
        await Index.IndexManyAsync(docs);
        // Doc 4 has an "A" (qty 1) and a qty-3 item, but no single element with both.
        await ShouldReturnSameResultsAsLinq(docs, d => d.Items.Any(i => i.Sku == "A" && i.Quantity >= 2), 1);
        await ShouldReturnSameResultsAsLinq(docs, d => d.Items.Any(i => i.Sku == "A" && i.Price > 11m), 1);
    }

    [Fact]
    public async Task Any_OrPredicate_ShouldFilter()
    {
        var docs = CollectionDocs();
        await Index.IndexManyAsync(docs);
        await ShouldReturnSameResultsAsLinq(docs, d => d.Items.Any(i => i.Sku == "C" || i.Price >= 100m), 2);
    }

    [Fact]
    public async Task Any_BooleanElementField_ShouldFilter()
    {
        var docs = CollectionDocs();
        await Index.IndexManyAsync(docs);
        await ShouldReturnSameResultsAsLinq(docs, d => d.Items.Any(i => !i.Shipped), 2);
        await ShouldReturnSameResultsAsLinq(docs, d => d.Items.Any(i => i.Shipped), 4);
    }

    [Fact]
    public async Task Any_ElementEnum_WithPropertyLevelStringConverter_ShouldFilter()
    {
        var docs = CollectionDocs();
        await Index.IndexManyAsync(docs);
        await ShouldReturnSameResultsAsLinq(docs, d => d.Items.Any(i => i.Priority == Priority.High), 2);
        await ShouldReturnSameResultsAsLinq(docs, d => d.Items.Any(i => i.Priority != Priority.Low), 4);
    }

    [Fact]
    public async Task Any_ElementEnum_WithIntegerFormat_ShouldFilter()
    {
        var docs = CollectionDocs();
        await Index.IndexManyAsync(docs);
        await ShouldReturnSameResultsAsLinq(docs, d => d.Items.Any(i => i.Danger == DangerZone.Archer), 1);
        await ShouldReturnSameResultsAsLinq(docs, d => d.Items.Any(i => i.Danger >= DangerZone.High), 2);
    }

    [Fact]
    public async Task Any_WithoutPredicate_MatchesNonEmptyCollections()
    {
        var docs = CollectionDocs();
        await Index.IndexManyAsync(docs);
        await ShouldReturnSameResultsAsLinq(docs, d => d.Items.Any(), 4);
        await ShouldReturnSameResultsAsLinq(docs, d => !d.Items.Any(), 1);
        await ShouldReturnSameResultsAsLinq(docs, d => !d.Labels.Any(), 1);
    }

    [Fact]
    public async Task Any_CombinedWithTopLevelFilters_ShouldFilter()
    {
        var docs = CollectionDocs();
        await Index.IndexManyAsync(docs);
        await ShouldReturnSameResultsAsLinq(docs, d => d.Items.Any(i => i.Sku == "A") || d.Labels.Contains("blue"), 3);
        await ShouldReturnSameResultsAsLinq(docs, d => d.Items.Any(i => i.Shipped) && d.Views > 2, 2);
    }

    [Fact]
    public async Task NegatedAny_ShouldFilter()
    {
        var docs = CollectionDocs();
        await Index.IndexManyAsync(docs);
        await ShouldReturnSameResultsAsLinq(docs, d => !d.Items.Any(i => i.Sku == "A"), 3);
        await ShouldReturnSameResultsAsLinq(docs, d => d.Items.Any(i => i.Sku == "A") == false, 3);
    }

    // ------------------------------------------------------------------ All

    [Fact]
    public async Task All_IsVacuouslyTrueForEmptyCollections()
    {
        var docs = CollectionDocs();
        await Index.IndexManyAsync(docs);
        await ShouldReturnSameResultsAsLinq(docs, d => d.Items.All(i => i.Shipped), 3);
    }

    [Fact]
    public async Task All_CombinedWithAny_ExcludesEmptyCollections()
    {
        var docs = CollectionDocs();
        await Index.IndexManyAsync(docs);
        await ShouldReturnSameResultsAsLinq(docs, d => d.Items.Any() && d.Items.All(i => i.Shipped), 2);
    }

    [Fact]
    public async Task All_OverScalarCollection_ShouldFilter()
    {
        var docs = CollectionDocs();
        await Index.IndexManyAsync(docs);
        await ShouldReturnSameResultsAsLinq(docs, d => d.Labels.All(l => l != "red"), 3);
    }

    // ------------------------------------------------------------------ Any over scalars

    [Fact]
    public async Task Any_OverStringCollection_WithStringOperator_ShouldFilter()
    {
        var docs = CollectionDocs();
        await Index.IndexManyAsync(docs);
        await ShouldReturnSameResultsAsLinq(docs, d => d.Labels.Any(l => l.StartsWith("ur")), 2);
        await ShouldReturnSameResultsAsLinq(docs, d => d.Labels.Any(l => l.Contains("e")), 4);
    }

    [Fact]
    public async Task Any_OverStringCollection_WithEquality_ShouldFilter()
    {
        var docs = CollectionDocs();
        await Index.IndexManyAsync(docs);
        await ShouldReturnSameResultsAsLinq(docs, d => d.Labels.Any(l => l == "red"), 2);
    }

    [Fact]
    public async Task Any_OverIntCollection_WithComparison_ShouldFilter()
    {
        var docs = CollectionDocs();
        await Index.IndexManyAsync(docs);
        await ShouldReturnSameResultsAsLinq(docs, d => d.Scores.Any(s => s > 4), 1);
        await ShouldReturnSameResultsAsLinq(docs, d => d.Scores.Any(s => s >= 3), 3);
    }

    [Fact]
    public async Task Any_OverScalarCollection_WithInSet_ShouldFilter()
    {
        var docs = CollectionDocs();
        await Index.IndexManyAsync(docs);
        var wanted = new[] { "blue", "green" };
        await ShouldReturnSameResultsAsLinq(docs, d => d.Labels.Any(l => wanted.Contains(l)), 2);
    }

    // ------------------------------------------------------------------ constant sets

    [Fact]
    public async Task ConstantSet_Any_ContainsAnyOf_ShouldFilter()
    {
        var docs = CollectionDocs();
        await Index.IndexManyAsync(docs);
        var wanted = new[] { "blue", "green" };
        await ShouldReturnSameResultsAsLinq(docs, d => wanted.Any(w => d.Labels.Contains(w)), 2);
    }

    [Fact]
    public async Task ConstantSet_All_ContainsAllOf_ShouldFilter()
    {
        var docs = CollectionDocs();
        await Index.IndexManyAsync(docs);
        var required = new List<string> { "red", "urgent" };
        await ShouldReturnSameResultsAsLinq(docs, d => required.All(r => d.Labels.Contains(r)), 1);
    }

    // ------------------------------------------------------------------ nesting

    [Fact]
    public async Task NestedAny_ShouldFilter()
    {
        var docs = CollectionDocs();
        await Index.IndexManyAsync(docs);
        await ShouldReturnSameResultsAsLinq(docs, d => d.Items.Any(i => i.Notes.Any(n => n.Rating >= 5)), 2);
        await ShouldReturnSameResultsAsLinq(docs, d => d.Items.Any(i => i.Notes.Any(n => n.Text == "great")), 1);
    }

    [Fact]
    public async Task Any_WithCollectionContainsOnElement_ShouldFilter()
    {
        var docs = CollectionDocs();
        await Index.IndexManyAsync(docs);
        await ShouldReturnSameResultsAsLinq(docs, d => d.Items.Any(i => i.Tags.Contains("z")), 2);
        // Both tags must be on the same element: doc 1 has "y" on both items but "z" on none.
        await ShouldReturnSameResultsAsLinq(docs, d => d.Items.Any(i => i.Tags.Contains("y") && i.Tags.Contains("z")), 1);
    }

    [Fact]
    public async Task Any_WithCountOnElement_ShouldFilter()
    {
        var docs = CollectionDocs();
        await Index.IndexManyAsync(docs);
        await ShouldReturnSameResultsAsLinq(docs, d => d.Items.Any(i => i.Tags.Count > 1), 2);
        await ShouldReturnSameResultsAsLinq(docs, d => d.Items.Any(i => i.Notes.Count == 0 && i.Sku == "B"), 1);
    }

    // ------------------------------------------------------------------ counts

    [Fact]
    public async Task Count_Comparison_ShouldFilter()
    {
        var docs = CollectionDocs();
        await Index.IndexManyAsync(docs);
        await ShouldReturnSameResultsAsLinq(docs, d => d.Items.Count > 1, 2);
        await ShouldReturnSameResultsAsLinq(docs, d => d.Labels.Count == 0, 1);
        await ShouldReturnSameResultsAsLinq(docs, d => d.Labels.Count() >= 2, 2);
        await ShouldReturnSameResultsAsLinq(docs, d => 2 <= d.Scores.Count, 3);
    }

    [Fact]
    public async Task Count_WithPredicate_ShouldFilter()
    {
        var docs = CollectionDocs();
        await Index.IndexManyAsync(docs);
        await ShouldReturnSameResultsAsLinq(docs, d => d.Items.Count(i => i.Shipped) == 1, 4);
        await ShouldReturnSameResultsAsLinq(docs, d => d.Items.Count(i => i.Price > 5m) >= 2, 2);
    }

    [Fact]
    public async Task Count_Negated_ShouldFilter()
    {
        var docs = CollectionDocs();
        await Index.IndexManyAsync(docs);
        await ShouldReturnSameResultsAsLinq(docs, d => !(d.Scores.Count > 1), 2);
    }

    // ------------------------------------------------------------------ negation & nullable

    [Fact]
    public async Task NegatedGroup_ShouldFilter()
    {
        var docs = CollectionDocs();
        await Index.IndexManyAsync(docs);
        await ShouldReturnSameResultsAsLinq(docs, d => !(d.Labels.Contains("red") || d.Scores.Contains(5)), 2);
        await ShouldReturnSameResultsAsLinq(docs, d => !(d.Views > 3), 3);
    }

    [Fact]
    public async Task NullableHasValue_ShouldFilter()
    {
        var docs = CollectionDocs();
        await Index.IndexManyAsync(docs);
        await ShouldReturnSameResultsAsLinq(docs, d => d.Valid.HasValue, 2);
        await ShouldReturnSameResultsAsLinq(docs, d => !d.Valid.HasValue, 3);
        await ShouldReturnSameResultsAsLinq(docs, d => d.Valid.HasValue && d.Valid.Value, 1);
    }

    // ------------------------------------------------------------------ other operations

    [Fact]
    public async Task CountAsync_WithQuantifierFilter_ShouldCount()
    {
        var docs = CollectionDocs();
        await Index.IndexManyAsync(docs);

        var request = new SearchRequest<TestDocument>().Where(d => d.Items.Any(i => i.Sku == "A"));
        var count = await Index.CountAsync(request);

        count.Should().Be(2);
    }

    [Fact]
    public async Task DeleteWhereAsync_WithQuantifierFilter_ShouldDelete()
    {
        var docs = CollectionDocs();
        await Index.IndexManyAsync(docs);

        var request = new SearchRequest<TestDocument>().Where(d => d.Items.Any(i => i.Sku == "A"));
        await Index.DeleteWhereAsync(request);

        var remaining = await Index.SearchAsync(new SearchRequest<TestDocument>());
        remaining.Results.Select(r => r.Document!.Id).Should().BeEquivalentTo(["2", "3", "5"]);
    }
}

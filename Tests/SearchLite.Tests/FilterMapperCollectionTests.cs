using System.Linq.Expressions;
using System.Text.Json.Serialization;
using FluentAssertions;

namespace SearchLite.Tests;

/// <summary>
/// Mapper coverage for collection-based filters (Any/All/Count), generic negation, nullable
/// helpers and the error paths that used to produce silently wrong paths.
/// </summary>
public class FilterMapperCollectionTests
{
    public enum Level
    {
        Low = 1,
        High = 2
    }

    private class Doc
    {
        public string Name { get; set; } = "";
        public int Age { get; set; }
        public bool? Verified { get; set; }
        public DateTime Created { get; set; }
        public List<string> Labels { get; set; } = [];
        public int[] Scores { get; set; } = [];
        public List<bool> Flags { get; set; } = [];
        public List<Item> Items { get; set; } = [];
        public Level Level { get; set; }
    }

    private class Item
    {
        public string Sku { get; set; } = "";
        public int Qty { get; set; }
        public bool Shipped { get; set; }
        public Address? Address { get; set; }
        public List<string> Tags { get; set; } = [];
        public List<Note> Notes { get; set; } = [];

        // Property-level converter: the element's own property decides the format, not any
        // same-named property on the document.
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public Level Level { get; set; }
    }

    private class Address
    {
        public string City { get; set; } = "";
    }

    private class Note
    {
        public int Rating { get; set; }
    }

    private static FilterNode<Doc> Map(Expression<Func<Doc, bool>> predicate) => FilterMapper.Map(predicate);

    private static FilterNode<Doc>.Condition Condition(string path, Type type, Operator op, object value) =>
        new() { PropertyName = path, PropertyType = type, Operator = op, Value = value };

    // ---------------------------------------------------------------- quantifiers

    [Fact]
    public void Any_WithPredicate_ProducesQuantifierWithElementRelativePath()
    {
        var result = Map(d => d.Items.Any(i => i.Sku == "A"));

        result.Should().BeEquivalentTo(new FilterNode<Doc>.Quantifier
        {
            PropertyName = "Items",
            ElementType = typeof(Item),
            Kind = QuantifierKind.Any,
            Predicate = Condition("Sku", typeof(string), Operator.Equal, "A")
        });
    }

    [Fact]
    public void Any_WithoutPredicate_HasNullPredicate()
    {
        var result = Map(d => d.Items.Any());

        result.Should().BeEquivalentTo(new FilterNode<Doc>.Quantifier
        {
            PropertyName = "Items",
            ElementType = typeof(Item),
            Kind = QuantifierKind.Any,
            Predicate = null
        });
    }

    [Fact]
    public void All_ProducesAllQuantifier()
    {
        var result = Map(d => d.Items.All(i => i.Shipped));

        result.Should().BeEquivalentTo(new FilterNode<Doc>.Quantifier
        {
            PropertyName = "Items",
            ElementType = typeof(Item),
            Kind = QuantifierKind.All,
            Predicate = Condition("Shipped", typeof(bool), Operator.Equal, true)
        });
    }

    [Fact]
    public void ListExistsAndTrueForAll_MapToQuantifiers()
    {
        ((FilterNode<Doc>.Quantifier)Map(d => d.Items.Exists(i => i.Qty > 1))).Kind.Should().Be(QuantifierKind.Any);
        ((FilterNode<Doc>.Quantifier)Map(d => d.Items.TrueForAll(i => i.Qty > 1))).Kind.Should().Be(QuantifierKind.All);
    }

    [Fact]
    public void Any_OverScalarCollection_UsesEmptyPathForTheElement()
    {
        var result = Map(d => d.Labels.Any(l => l.StartsWith("ur")));

        result.Should().BeEquivalentTo(new FilterNode<Doc>.Quantifier
        {
            PropertyName = "Labels",
            ElementType = typeof(string),
            Kind = QuantifierKind.Any,
            Predicate = Condition("", typeof(string), Operator.StartsWith, "ur")
        });
    }

    [Fact]
    public void Any_OverScalarCollection_WithComparison_UsesEmptyPath()
    {
        var result = Map(d => d.Scores.Any(s => s > 3));

        ((FilterNode<Doc>.Quantifier)result).Predicate
            .Should().BeEquivalentTo(Condition("", typeof(int), Operator.GreaterThan, 3));
    }

    [Fact]
    public void Any_OverBooleanCollection_UsesBareElement()
    {
        var positive = (FilterNode<Doc>.Quantifier)Map(d => d.Flags.Any(f => f));
        positive.Predicate.Should().BeEquivalentTo(Condition("", typeof(bool), Operator.Equal, true));

        var negative = (FilterNode<Doc>.Quantifier)Map(d => d.Flags.Any(f => !f));
        negative.Predicate.Should().BeEquivalentTo(Condition("", typeof(bool), Operator.Equal, false));
    }

    [Fact]
    public void Any_WithInOnElement_ProducesInCondition()
    {
        var wanted = new[] { "a", "b" };
        var result = (FilterNode<Doc>.Quantifier)Map(d => d.Labels.Any(l => wanted.Contains(l)));

        result.Predicate.Should().BeEquivalentTo(Condition("", typeof(string), Operator.In, wanted));
    }

    [Fact]
    public void Any_WithNestedFieldInElement_ProducesDottedRelativePath()
    {
        var result = (FilterNode<Doc>.Quantifier)Map(d => d.Items.Any(i => i.Address!.City == "Oslo"));

        result.Predicate.Should().BeEquivalentTo(Condition("Address.City", typeof(string), Operator.Equal, "Oslo"));
    }

    [Fact]
    public void Any_WithCollectionContainsInElement_ProducesRelativeCollectionContains()
    {
        var result = (FilterNode<Doc>.Quantifier)Map(d => d.Items.Any(i => i.Tags.Contains("x")));

        result.Predicate.Should().BeEquivalentTo(Condition("Tags", typeof(List<string>), Operator.CollectionContains, "x"));
    }

    [Fact]
    public void NestedAny_ProducesNestedQuantifier()
    {
        var result = Map(d => d.Items.Any(i => i.Notes.Any(n => n.Rating >= 4)));

        result.Should().BeEquivalentTo(new FilterNode<Doc>.Quantifier
        {
            PropertyName = "Items",
            ElementType = typeof(Item),
            Kind = QuantifierKind.Any,
            Predicate = new FilterNode<Doc>.Quantifier
            {
                PropertyName = "Notes",
                ElementType = typeof(Note),
                Kind = QuantifierKind.Any,
                Predicate = Condition("Rating", typeof(int), Operator.GreaterThanOrEqual, 4)
            }
        });
    }

    [Fact]
    public void Any_WithCompoundPredicate_ProducesGroupInsideQuantifier()
    {
        var result = (FilterNode<Doc>.Quantifier)Map(d => d.Items.Any(i => i.Sku == "A" && i.Qty > 1));

        result.Predicate.Should().BeEquivalentTo(new FilterNode<Doc>.Group
        {
            Operator = LogicalOperator.And,
            Conditions =
            [
                Condition("Sku", typeof(string), Operator.Equal, "A"),
                Condition("Qty", typeof(int), Operator.GreaterThan, 1)
            ]
        });
    }

    [Fact]
    public void Any_ElementEnum_UsesTheElementPropertyFormat()
    {
        // Item.Level carries a string converter; Doc.Level (same name) does not.
        var element = (FilterNode<Doc>.Quantifier)Map(d => d.Items.Any(i => i.Level == Level.High));
        element.Predicate.Should().BeEquivalentTo(Condition("Level", typeof(Level), Operator.Equal, "High"));

        var document = Map(d => d.Level == Level.High);
        document.Should().BeEquivalentTo(Condition("Level", typeof(Level), Operator.Equal, 2));
    }

    // ---------------------------------------------------------------- constant sets

    [Fact]
    public void Any_OverConstantSet_ExpandsToOrGroup()
    {
        var wanted = new[] { "a", "b" };
        var result = Map(d => wanted.Any(w => d.Labels.Contains(w)));

        result.Should().BeEquivalentTo(new FilterNode<Doc>.Group
        {
            Operator = LogicalOperator.Or,
            Conditions =
            [
                Condition("Labels", typeof(List<string>), Operator.CollectionContains, "a"),
                Condition("Labels", typeof(List<string>), Operator.CollectionContains, "b")
            ]
        });
    }

    [Fact]
    public void All_OverConstantSet_ExpandsToAndGroup()
    {
        var required = new List<string> { "a", "b" };
        var result = Map(d => required.All(r => d.Labels.Contains(r)));

        result.Should().BeEquivalentTo(new FilterNode<Doc>.Group
        {
            Operator = LogicalOperator.And,
            Conditions =
            [
                Condition("Labels", typeof(List<string>), Operator.CollectionContains, "a"),
                Condition("Labels", typeof(List<string>), Operator.CollectionContains, "b")
            ]
        });
    }

    [Fact]
    public void Any_OverEmptyConstantSet_IsAlwaysFalse()
    {
        var wanted = Array.Empty<string>();
        var result = Map(d => wanted.Any(w => d.Labels.Contains(w)));

        result.Should().BeEquivalentTo(new FilterNode<Doc>.Group { Operator = LogicalOperator.Or, Conditions = [] });
    }

    [Fact]
    public void All_OverEmptyConstantSet_IsAlwaysTrue()
    {
        var required = Array.Empty<string>();
        var result = Map(d => required.All(r => d.Labels.Contains(r)));

        result.Should().BeEquivalentTo(new FilterNode<Doc>.Group { Operator = LogicalOperator.And, Conditions = [] });
    }

    // ---------------------------------------------------------------- counts

    [Fact]
    public void CountProperty_ProducesCollectionCount()
    {
        var result = Map(d => d.Labels.Count > 2);

        result.Should().BeEquivalentTo(new FilterNode<Doc>.CollectionCount
        {
            PropertyName = "Labels",
            ElementType = typeof(string),
            Predicate = null,
            Operator = Operator.GreaterThan,
            Value = 2
        });
    }

    [Fact]
    public void ArrayLength_ProducesCollectionCount()
    {
        var result = (FilterNode<Doc>.CollectionCount)Map(d => d.Scores.Length == 0);

        result.PropertyName.Should().Be("Scores");
        result.ElementType.Should().Be(typeof(int));
        result.Operator.Should().Be(Operator.Equal);
        result.Value.Should().Be(0);
    }

    [Fact]
    public void CountMethod_And_LongCount_ProduceCollectionCount()
    {
        ((FilterNode<Doc>.CollectionCount)Map(d => d.Labels.Count() >= 1)).Operator.Should().Be(Operator.GreaterThanOrEqual);
        ((FilterNode<Doc>.CollectionCount)Map(d => d.Labels.LongCount() < 5L)).Value.Should().Be(5);
    }

    [Fact]
    public void CountOnRightSide_FlipsTheOperator()
    {
        var result = (FilterNode<Doc>.CollectionCount)Map(d => 2 < d.Labels.Count);

        result.Operator.Should().Be(Operator.GreaterThan);
        result.Value.Should().Be(2);
    }

    [Fact]
    public void CountWithPredicate_CarriesElementPredicate()
    {
        var result = Map(d => d.Items.Count(i => i.Shipped) >= 2);

        result.Should().BeEquivalentTo(new FilterNode<Doc>.CollectionCount
        {
            PropertyName = "Items",
            ElementType = typeof(Item),
            Predicate = Condition("Shipped", typeof(bool), Operator.Equal, true),
            Operator = Operator.GreaterThanOrEqual,
            Value = 2
        });
    }

    [Fact]
    public void CountInsideQuantifier_IsRelativeToTheElement()
    {
        var result = (FilterNode<Doc>.Quantifier)Map(d => d.Items.Any(i => i.Tags.Count > 1));

        result.Predicate.Should().BeOfType<FilterNode<Doc>.CollectionCount>()
            .Which.PropertyName.Should().Be("Tags");
    }

    // ---------------------------------------------------------------- negation

    [Fact]
    public void NegatedQuantifier_WrapsInNot()
    {
        var result = Map(d => !d.Items.Any(i => i.Sku == "A"));

        result.Should().BeOfType<FilterNode<Doc>.Not>()
            .Which.Operand.Should().BeOfType<FilterNode<Doc>.Quantifier>();
    }

    [Fact]
    public void NegatedGroup_WrapsInNot()
    {
        var result = Map(d => !(d.Age > 5 || d.Name == "x"));

        result.Should().BeOfType<FilterNode<Doc>.Not>()
            .Which.Operand.Should().BeOfType<FilterNode<Doc>.Group>()
            .Which.Operator.Should().Be(LogicalOperator.Or);
    }

    [Fact]
    public void NegatedComparison_InvertsTheOperator()
    {
        Map(d => !(d.Age > 5)).Should().BeEquivalentTo(Condition("Age", typeof(int), Operator.LessThanOrEqual, 5));
        Map(d => !(d.Age == 5)).Should().BeEquivalentTo(Condition("Age", typeof(int), Operator.NotEqual, 5));
        Map(d => !(d.Age <= 5)).Should().BeEquivalentTo(Condition("Age", typeof(int), Operator.GreaterThan, 5));
    }

    [Fact]
    public void NegatedCount_InvertsTheOperator()
    {
        var result = (FilterNode<Doc>.CollectionCount)Map(d => !(d.Labels.Count > 2));

        result.Operator.Should().Be(Operator.LessThanOrEqual);
    }

    [Fact]
    public void DoubleNegation_Unwraps()
    {
        var result = Map(d => !!(d.Age > 5 && d.Name == "x"));

        result.Should().BeOfType<FilterNode<Doc>.Group>();
    }

    [Fact]
    public void BooleanExpressionComparedToConstant_IsMappedAndNegatedAsNeeded()
    {
        Map(d => d.Items.Any() == false).Should().BeOfType<FilterNode<Doc>.Not>();
        Map(d => d.Items.Any() == true).Should().BeOfType<FilterNode<Doc>.Quantifier>();
        Map(d => d.Items.Any() != true).Should().BeOfType<FilterNode<Doc>.Not>();
        Map(d => false == d.Items.Any()).Should().BeOfType<FilterNode<Doc>.Not>();
    }

    // ---------------------------------------------------------------- nullable helpers

    [Fact]
    public void HasValue_MapsToNullChecks()
    {
        Map(d => d.Verified.HasValue).Should().BeEquivalentTo(Condition("Verified", typeof(bool?), Operator.IsNotNull, true));
        Map(d => !d.Verified.HasValue).Should().BeEquivalentTo(Condition("Verified", typeof(bool?), Operator.IsNull, true));
    }

    [Fact]
    public void NullableValue_ReadsTheUnderlyingField()
    {
        Map(d => d.Verified!.Value).Should().BeEquivalentTo(Condition("Verified", typeof(bool), Operator.Equal, true));
        Map(d => d.Verified!.Value == false).Should().BeEquivalentTo(Condition("Verified", typeof(bool), Operator.Equal, false));
    }

    // ---------------------------------------------------------------- constants

    [Fact]
    public void CapturedBooleanWithoutDocumentReference_IsFoldedToAConstant()
    {
        var includeAll = true;
        var result = (FilterNode<Doc>.Group)Map(d => includeAll || d.Age > 5);

        result.Operator.Should().Be(LogicalOperator.Or);
        result.Conditions[0].Should().BeEquivalentTo(new FilterNode<Doc>.Group { Operator = LogicalOperator.And, Conditions = [] });
    }

    [Fact]
    public void ConstantFalse_IsAnEmptyOrGroup()
    {
        Map(d => false).Should().BeEquivalentTo(new FilterNode<Doc>.Group { Operator = LogicalOperator.Or, Conditions = [] });
    }

    // ---------------------------------------------------------------- errors

    [Fact]
    public void ReferencingTheOuterDocumentInsideAPredicate_Throws()
    {
        var act = () => Map(d => d.Items.Any(i => i.Qty > d.Age));

        act.Should().Throw<NotSupportedException>().WithMessage("*outer document*");
    }

    [Fact]
    public void ComparingTwoDocumentFields_Throws()
    {
        var act = () => Map(d => d.Age == d.Scores.Length);

        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void MembersOfScalarTypes_AreRejectedInsteadOfProducingBogusPaths()
    {
        var length = () => Map(d => d.Name.Length > 3);
        length.Should().Throw<NotSupportedException>().WithMessage("*Length*");

        var year = () => Map(d => d.Created.Year == 2024);
        year.Should().Throw<NotSupportedException>().WithMessage("*Year*");
    }

    [Fact]
    public void UnsupportedMethodOnAField_IsRejectedInsteadOfIgnored()
    {
        var act = () => Map(d => d.Name.ToUpper() == "X");

        act.Should().Throw<NotSupportedException>().WithMessage("*ToUpper*");
    }

    [Fact]
    public void CompareToOnTheRightSide_IsFlipped()
    {
        FilterMapper.Map<Doc>(d => 0 < d.Name.CompareTo("m"))
            .Should().BeEquivalentTo(Condition("Name", typeof(string), Operator.GreaterThan, "m"));
        FilterMapper.Map<Doc>(d => 0 == d.Age.CompareTo(3))
            .Should().BeEquivalentTo(Condition("Age", typeof(int), Operator.Equal, 3));
    }

    private class DocWithDictionary
    {
        public Dictionary<string, string> Attributes { get; set; } = new();
    }

    [Fact]
    public void Dictionaries_AreNotTreatedAsArrays()
    {
        var count = () => FilterMapper.Map<DocWithDictionary>(d => d.Attributes.Count > 0);
        count.Should().Throw<NotSupportedException>();

        var any = () => FilterMapper.Map<DocWithDictionary>(d => d.Attributes.Any(kv => kv.Key == "x"));
        any.Should().Throw<NotSupportedException>();

        var indexer = () => FilterMapper.Map<DocWithDictionary>(d => d.Attributes["color"] == "red");
        indexer.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void AnyOnANonCollectionValue_Throws()
    {
        var act = () => Map(d => d.Name.Any(c => c == 'x'));

        act.Should().Throw<NotSupportedException>();
    }
}

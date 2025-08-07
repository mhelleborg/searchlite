using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;

namespace SearchLite.Tests
{
    // Test enums with different configurations
    public enum PlainEnum
    {
        First,
        Second,
        Third
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum StringAttributedEnum
    {
        Alpha,
        Beta,
        Gamma
    }

    // Custom converters for testing
    public class CustomStringEnumConverter : JsonStringEnumConverter
    {
        public CustomStringEnumConverter() : base()
        {
        }
    }

    public class CustomIntegerEnumConverter<T> : JsonConverter<T> where T : struct, Enum
    {
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return (T)Enum.ToObject(typeof(T), reader.GetInt32());
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            writer.WriteNumberValue(Convert.ToInt32(value));
        }
    }

    [JsonConverter(typeof(CustomStringEnumConverter))]
    public enum CustomConverterEnum
    {
        One,
        Two,
        Three
    }
    
    [JsonConverter(typeof(CustomIntegerEnumConverter<CustomIntConverterEnum>))]
    public enum CustomIntConverterEnum
    {
        One,
        Two,
        Three
    }

    // Test classes with various property configurations
    public class TestModel
    {
        public PlainEnum PlainProperty { get; set; }

        public StringAttributedEnum StringProperty { get; set; }

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public PlainEnum OverriddenToString { get; set; }

        [JsonConverter(typeof(CustomIntegerEnumConverter<StringAttributedEnum>))]
        public StringAttributedEnum OverriddenToInteger { get; set; }

        public PlainEnum? NullablePlain { get; set; }

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public PlainEnum? NullableWithConverter { get; set; }

        public CustomConverterEnum CustomConverterProperty { get; set; }
    }

    public class EnumSerializationAnalyzerTests
    {
        public class TypeLevelTests
        {
            [Fact]
            public void PlainEnum_ShouldDefault_ToInteger()
            {
                // Act
                var format = EnumSerializationAnalyzer<PlainEnum>.DefaultFormat;

                // Assert
                format.Should().Be(EnumSerializationFormat.Integer);
            }

            [Fact]
            public void StringAttributedEnum_ShouldDefault_ToString()
            {
                // Act
                var format = EnumSerializationAnalyzer<StringAttributedEnum>.DefaultFormat;

                // Assert
                format.Should().Be(EnumSerializationFormat.String);
            }

            [Fact]
            public void CustomConverterEnum_ShouldRecognize_CustomStringConverter()
            {
                // Act
                var format = EnumSerializationAnalyzer<CustomConverterEnum>.DefaultFormat;

                // Assert
                format.Should().Be(EnumSerializationFormat.String);
            }
            
            [Fact]
            public void CustomConverterEnum_ShouldRecognize_CustomIntegerConverter()
            {
                // Act
                var format = EnumSerializationAnalyzer<CustomIntConverterEnum>.DefaultFormat;

                // Assert
                format.Should().Be(EnumSerializationFormat.Integer);
            }
        }

        public class PropertyLevelTests
        {
            [Fact]
            public void PlainProperty_WithoutOverride_ShouldUseTypeDefault()
            {
                // Arrange
                var propertyInfo = typeof(TestModel).GetProperty(nameof(TestModel.PlainProperty));

                // Act
                var format = EnumSerializationAnalyzer<PlainEnum>.GetPropertyFormat(propertyInfo!);

                // Assert
                format.Should().Be(EnumSerializationFormat.Integer);
            }

            [Fact]
            public void StringProperty_WithoutOverride_ShouldUseTypeDefault()
            {
                // Arrange
                var propertyInfo = typeof(TestModel).GetProperty(nameof(TestModel.StringProperty));

                // Act
                var format = EnumSerializationAnalyzer<StringAttributedEnum>.GetPropertyFormat(propertyInfo!);

                // Assert
                format.Should().Be(EnumSerializationFormat.String);
            }

            [Fact]
            public void PlainEnum_WithStringConverterAttribute_ShouldOverrideToString()
            {
                // Arrange
                var propertyInfo = typeof(TestModel).GetProperty(nameof(TestModel.OverriddenToString));

                // Act
                var format = EnumSerializationAnalyzer<PlainEnum>.GetPropertyFormat(propertyInfo!);

                // Assert
                format.Should().Be(EnumSerializationFormat.String);
            }

            [Fact]
            public void StringEnum_WithIntegerConverterAttribute_ShouldOverrideToInteger()
            {
                // Arrange
                var propertyInfo = typeof(TestModel).GetProperty(nameof(TestModel.OverriddenToInteger));

                // Act
                var format = EnumSerializationAnalyzer<StringAttributedEnum>.GetPropertyFormat(propertyInfo!);

                // Assert
                format.Should().Be(EnumSerializationFormat.Integer);
            }

            [Fact]
            public void GetPropertyFormat_WithPropertyName_ShouldWork()
            {
                // Act
                var format = EnumSerializationAnalyzer<PlainEnum>
                    .GetPropertyFormat<TestModel>("OverriddenToString");

                // Assert
                format.Should().Be(EnumSerializationFormat.String);
            }

            [Fact]
            public void GetPropertyFormat_WithInvalidPropertyName_ShouldThrow()
            {
                // Act & Assert
                Action act = () => EnumSerializationAnalyzer<PlainEnum>
                    .GetPropertyFormat<TestModel>("NonExistentProperty");

                act.Should().Throw<ArgumentException>()
                    .WithMessage("*not found*");
            }
        }

        public class NullableEnumTests
        {
            [Fact]
            public void NullableEnum_WithoutConverter_ShouldUseTypeDefault()
            {
                // Arrange
                var propertyInfo = typeof(TestModel).GetProperty(nameof(TestModel.NullablePlain));

                // Act
                var format = EnumSerializationAnalyzer<PlainEnum>.GetPropertyFormat(propertyInfo!);

                // Assert
                format.Should().Be(EnumSerializationFormat.Integer);
            }

            [Fact]
            public void NullableEnum_WithConverter_ShouldUseConverter()
            {
                // Arrange
                var propertyInfo = typeof(TestModel).GetProperty(nameof(TestModel.NullableWithConverter));

                // Act
                var format = EnumSerializationAnalyzer<PlainEnum>.GetPropertyFormat(propertyInfo!);

                // Assert
                format.Should().Be(EnumSerializationFormat.String);
            }
        }

        public class ValidationTests
        {
            [Fact]
            public void GetPropertyFormat_WithNull_ShouldThrow()
            {
                // Act & Assert
                Action act = () => EnumSerializationAnalyzer<PlainEnum>.GetPropertyFormat(null!);

                act.Should().Throw<ArgumentNullException>()
                    .WithParameterName("propertyInfo");
            }

            [Fact]
            public void GetPropertyFormat_WithWrongEnumType_ShouldThrow()
            {
                // Arrange
                var propertyInfo = typeof(TestModel).GetProperty(nameof(TestModel.StringProperty));

                // Act & Assert
                Action act = () => EnumSerializationAnalyzer<PlainEnum>.GetPropertyFormat(propertyInfo!);

                act.Should().Throw<ArgumentException>()
                    .WithMessage("*Property type must be PlainEnum or Nullable<PlainEnum>*");
            }
        }

        public class CachingTests
        {
            [Fact]
            public void TypeLevelFormat_ShouldBeCached()
            {
                // Act - Call multiple times
                var format1 = EnumSerializationAnalyzer<PlainEnum>.DefaultFormat;
                var format2 = EnumSerializationAnalyzer<PlainEnum>.DefaultFormat;
                var format3 = EnumSerializationAnalyzer<PlainEnum>.DefaultFormat;

                // Assert - All should be the same
                format1.Should().Be(format2);
                format2.Should().Be(format3);
                format1.Should().Be(EnumSerializationFormat.Integer);
            }

            [Fact]
            public void PropertyFormat_ShouldBeCached()
            {
                // Arrange
                var propertyInfo = typeof(TestModel).GetProperty(nameof(TestModel.OverriddenToString));

                // Act - Call multiple times
                var format1 = EnumSerializationAnalyzer<PlainEnum>.GetPropertyFormat(propertyInfo!);
                var format2 = EnumSerializationAnalyzer<PlainEnum>.GetPropertyFormat(propertyInfo!);
                var format3 = EnumSerializationAnalyzer<PlainEnum>.GetPropertyFormat(propertyInfo!);

                // Assert
                format1.Should().Be(format2);
                format2.Should().Be(format3);
                format1.Should().Be(EnumSerializationFormat.String);
            }
        }

        public class RuntimeAnalyzerTests
        {
            [Fact]
            public void RuntimeAnalyzer_WithPlainEnum_ShouldReturnInteger()
            {
                // Arrange
                var propertyInfo = typeof(TestModel).GetProperty(nameof(TestModel.PlainProperty));

                // Act
                var format = EnumSerializationAnalyzer.GetPropertyFormat(propertyInfo!);

                // Assert
                format.Should().Be(EnumSerializationFormat.Integer);
            }

            [Fact]
            public void RuntimeAnalyzer_WithStringEnum_ShouldReturnString()
            {
                // Arrange
                var propertyInfo = typeof(TestModel).GetProperty(nameof(TestModel.StringProperty));

                // Act
                var format = EnumSerializationAnalyzer.GetPropertyFormat(propertyInfo!);

                // Assert
                format.Should().Be(EnumSerializationFormat.String);
            }

            [Fact]
            public void RuntimeAnalyzer_WithOverriddenProperty_ShouldReturnOverride()
            {
                // Arrange
                var propertyInfo = typeof(TestModel).GetProperty(nameof(TestModel.OverriddenToString));

                // Act
                var format = EnumSerializationAnalyzer.GetPropertyFormat(propertyInfo!);

                // Assert
                format.Should().Be(EnumSerializationFormat.String);
            }

            [Fact]
            public void RuntimeAnalyzer_WithNonEnumProperty_ShouldThrow()
            {
                // Arrange
                var propertyInfo = typeof(string).GetProperty(nameof(string.Length));

                // Act & Assert
                Action act = () => EnumSerializationAnalyzer.GetPropertyFormat(propertyInfo!);

                act.Should().Throw<ArgumentException>()
                    .WithMessage("*must be an enum*");
            }

            [Fact]
            public void RuntimeAnalyzer_WithNullProperty_ShouldThrow()
            {
                // Act & Assert
                Action act = () => EnumSerializationAnalyzer.GetPropertyFormat(null!);

                act.Should().Throw<ArgumentNullException>()
                    .WithParameterName("propertyInfo");
            }

            [Fact]
            public void GetDefaultFormat_WithEnumType_ShouldWork()
            {
                // Act
                var plainFormat = EnumSerializationAnalyzer.GetDefaultFormat(typeof(PlainEnum));
                var stringFormat = EnumSerializationAnalyzer.GetDefaultFormat(typeof(StringAttributedEnum));

                // Assert
                plainFormat.Should().Be(EnumSerializationFormat.Integer);
                stringFormat.Should().Be(EnumSerializationFormat.String);
            }

            [Fact]
            public void GetDefaultFormat_WithNonEnumType_ShouldThrow()
            {
                // Act & Assert
                Action act = () => EnumSerializationAnalyzer.GetDefaultFormat(typeof(string));

                act.Should().Throw<ArgumentException>()
                    .WithMessage("*must be an enum*");
            }
        }

        public class IntegrationTests
        {
[Fact]
public void SerializedOutput_ShouldMatch_AnalyzerPrediction()
{
    // Arrange
    var model = new TestModel
    {
        PlainProperty = PlainEnum.Second,
        StringProperty = StringAttributedEnum.Beta,
        OverriddenToString = PlainEnum.Third,
        OverriddenToInteger = StringAttributedEnum.Gamma,
        NullablePlain = PlainEnum.First,
        NullableWithConverter = PlainEnum.Second,
        CustomConverterProperty = CustomConverterEnum.Two
    };

    var options = new JsonSerializerOptions { WriteIndented = false };

    // Act
    var json = JsonSerializer.Serialize(model, options);
    var deserialized = JsonSerializer.Deserialize<JsonDocument>(json, options);
    var root = deserialized!.RootElement;

    // Assert - Verify each property based on analyzer prediction

    // PlainProperty should be integer
    root.GetProperty(nameof(TestModel.PlainProperty)).ValueKind.Should().Be(JsonValueKind.Number);
    root.GetProperty(nameof(TestModel.PlainProperty)).GetInt32().Should().Be((int)PlainEnum.Second);

    // StringProperty should be string
    root.GetProperty(nameof(TestModel.StringProperty)).ValueKind.Should().Be(JsonValueKind.String);
    root.GetProperty(nameof(TestModel.StringProperty)).GetString().Should().Be(nameof(StringAttributedEnum.Beta));

    // OverriddenToString should be string
    root.GetProperty(nameof(TestModel.OverriddenToString)).ValueKind.Should().Be(JsonValueKind.String);
    root.GetProperty(nameof(TestModel.OverriddenToString)).GetString().Should().Be(nameof(PlainEnum.Third));

    // OverriddenToInteger should be integer
    root.GetProperty(nameof(TestModel.OverriddenToInteger)).ValueKind.Should().Be(JsonValueKind.Number);
    root.GetProperty(nameof(TestModel.OverriddenToInteger)).GetInt32().Should().Be((int)StringAttributedEnum.Gamma);

    // NullablePlain should be integer
    root.GetProperty(nameof(TestModel.NullablePlain)).ValueKind.Should().Be(JsonValueKind.Number);

    // NullableWithConverter should be string
    root.GetProperty(nameof(TestModel.NullableWithConverter)).ValueKind.Should().Be(JsonValueKind.String);

    // CustomConverterProperty should be string
    root.GetProperty(nameof(TestModel.CustomConverterProperty)).ValueKind.Should().Be(JsonValueKind.String);
    root.GetProperty(nameof(TestModel.CustomConverterProperty)).GetString().Should().Be(nameof(CustomConverterEnum.Two));
}
            [Fact]
            public void AnalyzerPredictions_ShouldMatch_ActualSerialization()
            {
                // Arrange
                var testCases = new[]
                {
                    (Property: nameof(TestModel.PlainProperty), Expected: EnumSerializationFormat.Integer),
                    (Property: nameof(TestModel.StringProperty), Expected: EnumSerializationFormat.String),
                    (Property: nameof(TestModel.OverriddenToString), Expected: EnumSerializationFormat.String),
                    (Property: nameof(TestModel.OverriddenToInteger), Expected: EnumSerializationFormat.Integer),
                    (Property: nameof(TestModel.CustomConverterProperty), Expected: EnumSerializationFormat.String)
                };

                // Act & Assert
                foreach (var testCase in testCases)
                {
                    var propertyInfo = typeof(TestModel).GetProperty(testCase.Property);
                    var predictedFormat = EnumSerializationAnalyzer.GetPropertyFormat(propertyInfo!);

                    predictedFormat.Should().Be(testCase.Expected,
                        $"property {testCase.Property} should have format {testCase.Expected}");
                }
            }
        }
    }
}
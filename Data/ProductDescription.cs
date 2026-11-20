using System.Text.Json.Serialization;

namespace ProductDescGen.Data;

public sealed record ProductDescription(
    string ProductTitle,
    string MetaTitle,
    string MetaDescription,
    string HtmlDescription);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(ProductDescription))]
internal partial class ProductDataGenerationContext : JsonSerializerContext;
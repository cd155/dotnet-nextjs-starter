using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace PlatformServer.Configuration;

/// <summary>
/// Lists which properties are always there, because the generator marks a property required only when it
/// has the <c>required</c> keyword or <c>[Required]</c>, so without this every field in the web's generated
/// types would be optional.
/// <list type="bullet">
/// <item>What the API sends has every property, a null one included, unless a <c>[JsonIgnore]</c>
/// condition leaves it out.</item>
/// <item>What it reads (a request body) needs every property but the nullable ones: the API reads a
/// missing one as null, which a push takes as "unchanged".</item>
/// </list>
/// Whether a property can be null still comes from its C# type (<c>string?</c> appears as
/// <c>["null", "string"]</c>).
/// </summary>
public sealed class RequiredPropertiesSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        if (schema.Properties is not { Count: > 0 } properties)
        {
            return Task.CompletedTask;
        }

        var isRequest = context.ParameterDescription is not null;
        schema.Required ??= new HashSet<string>();
        foreach (var property in context.JsonTypeInfo.Properties)
        {
            if (properties.ContainsKey(property.Name) && (isRequest ? !property.IsSetNullable : IsAlwaysWritten(property)))
            {
                schema.Required.Add(property.Name);
            }
        }

        return Task.CompletedTask;
    }

    private static bool IsAlwaysWritten(JsonPropertyInfo property) =>
        property.AttributeProvider?.GetCustomAttributes(typeof(JsonIgnoreAttribute), inherit: true)
            .OfType<JsonIgnoreAttribute>()
            .All(ignore => ignore.Condition == JsonIgnoreCondition.Never) ?? true;
}

using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace HOPPER.API.OpenApi
{
    internal sealed class EnumSchemaTransformer : IOpenApiSchemaTransformer
    {
        public static string? ReferenceId(JsonTypeInfo type) =>
            Underlying(type.Type) is { } enumType
                ? enumType.Name
                : OpenApiOptions.CreateDefaultSchemaReferenceId(type);

        public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
        {
            if (Underlying(context.JsonTypeInfo.Type) is not { } enumType)
                return Task.CompletedTask;

            var names = Enum.GetNames(enumType);
            var values = Enum.GetValuesAsUnderlyingType(enumType);

            schema.Type = JsonSchemaType.Integer;
            schema.Format = "int32";
            schema.Enum = [.. values.Cast<object>().Select(v => (JsonNode)Convert.ToInt32(v))];
            schema.Extensions ??= new Dictionary<string, IOpenApiExtension>();
            schema.Extensions["x-enum-varnames"] = new JsonNodeExtension(new JsonArray([.. names.Select(n => (JsonNode)n)]));

            return Task.CompletedTask;
        }

        private static Type? Underlying(Type type)
        {
            var bare = Nullable.GetUnderlyingType(type) ?? type;
            return bare.IsEnum ? bare : null;
        }
    }
}

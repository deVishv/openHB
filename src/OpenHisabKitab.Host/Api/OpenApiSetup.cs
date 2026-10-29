using Microsoft.OpenApi;
using OpenHisabKitab.Core.Amounts;
using OpenHisabKitab.Core.Gst;
using OpenHisabKitab.Host.Grpc;

namespace OpenHisabKitab.Host.Api;

/// <summary>The OpenAPI document at /api/v1/openapi.json (spec 4.1). It sits behind ApiGate, so it needs a token like any route.</summary>
public static class OpenApiSetup
{
    public const string MoneyPattern = @"^-?(0|[1-9][0-9]*)\.[0-9]{2}$";
    public const string QuantityPattern = @"^-?(0|[1-9][0-9]*)\.[0-9]{3}$";
    public const string RatePattern = @"^(0|[1-9][0-9]*)\.[0-9]{2}$";

    public static IServiceCollection AddApiDocument(this IServiceCollection services) =>
        services.AddOpenApi("v1", o =>
        {
            // Money, quantities, and rates are wire strings, never JSON numbers.
            o.AddSchemaTransformer((schema, context, _) =>
            {
                var type = context.JsonTypeInfo.Type;
                var pattern = type == typeof(Money) ? MoneyPattern
                    : type == typeof(Quantity) ? QuantityPattern
                    : type == typeof(GstRate) ? RatePattern
                    : null;
                if (pattern is not null)
                {
                    schema.Type = JsonSchemaType.String;
                    schema.Pattern = pattern;
                    schema.Properties?.Clear();
                    schema.Required?.Clear();
                }
                return Task.CompletedTask;
            });
            o.AddDocumentTransformer((document, _, _) =>
            {
                document.Info.Title = "Open Hisab Kitab";
                document.Info.Version = HostVersion.Current;
                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                document.Components.SecuritySchemes["bearer"] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    Description = "An API token created in Open Hisab Kitab.",
                };
                return Task.CompletedTask;
            });
        });
}

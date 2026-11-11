using Asp.Versioning;
using Microsoft.OpenApi;

namespace PlatformServer.Configuration;

public static class EndpointRouteBuilderExtensions
{
    /// <summary>
    /// The <c>/api/v1</c> route group the features map their endpoints on. The routes keep the
    /// <c>v{version:apiVersion}</c> segment the controllers they replaced used, so the URLs didn't change.
    /// </summary>
    public static RouteGroupBuilder MapApiV1(this IEndpointRouteBuilder app)
    {
        var versions = app.NewApiVersionSet()
            .HasApiVersion(new ApiVersion(1, 0))
            .Build();

        return app.MapGroup("api/v{version:apiVersion}")
            .WithApiVersionSet(versions)
            // The OpenAPI document lists only the parameters a Minimal API handler binds, and no
            // handler binds the version. A path's template parameters must all be described, so it's
            // added here.
            .AddOpenApiOperationTransformer((operation, context, cancellationToken) =>
            {
                operation.Parameters ??= [];
                operation.Parameters.Add(new OpenApiParameter
                {
                    Name = "version",
                    In = ParameterLocation.Path,
                    Required = true,
                    Schema = new OpenApiSchema { Type = JsonSchemaType.String },
                });
                return Task.CompletedTask;
            });
    }
}

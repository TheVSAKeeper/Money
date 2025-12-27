using Microsoft.AspNetCore.OpenApi;
using Microsoft.Net.Http.Headers;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

namespace Money.Api.Definitions;

public class OpenApiDefinition : AppDefinition
{
    public override void ConfigureServices(WebApplicationBuilder builder)
    {
        builder.Services.AddEndpointsApiExplorer();

        builder.Services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer((document, context, cancellationToken) =>
            {
                document.Info = new OpenApiInfo
                {
                    Title = "Money API",
                    Version = "v1",
                    Description = "API для управления финансами"
                };

                document.Components ??= new OpenApiComponents();

                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();

                document.Components.SecuritySchemes["oauth2"] = new OpenApiSecurityScheme
                {
                    Description = "OAuth2.0 Authorization",
                    Flows = new OpenApiOAuthFlows
                    {
                        Password = new OpenApiOAuthFlow
                        {
                            TokenUrl = new Uri("/connect/token", UriKind.Relative)
                        }
                    },
                    In = ParameterLocation.Header,
                    Name = HeaderNames.Authorization,
                    Type = SecuritySchemeType.OAuth2
                };

                document.Security ??= new List<OpenApiSecurityRequirement>();

                document.Security.Add(new OpenApiSecurityRequirement
                {
                    {
                        new OpenApiSecuritySchemeReference("oauth2", document),
                        []
                    }
                });

                return Task.CompletedTask;
            });
        });
    }

    public override void ConfigureApplication(WebApplication app)
    {
        var swaggerForEveryOneHome = true;

        if (!swaggerForEveryOneHome && !app.Environment.IsDevelopment())
        {
            return;
        }

        app.MapOpenApi();

        app.MapScalarApiReference(options =>
        {
            options.WithTitle("Money API")
                .WithTheme(ScalarTheme.Mars);

            options.Authentication = new ScalarAuthenticationOptions
            {
                PreferredSecuritySchemes = ["oauth2"]
            };
        });
    }
}

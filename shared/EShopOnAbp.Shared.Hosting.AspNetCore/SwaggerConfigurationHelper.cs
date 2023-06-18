using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using Volo.Abp.Modularity;

namespace EShopOnAbp.Shared.Hosting.AspNetCore;

public static class SwaggerConfigurationHelper
{
    public static void Configure(
        ServiceConfigurationContext context,
        string apiTitle
    )
    {
        context.Services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo {Title = apiTitle, Version = "v1"});
            options.DocInclusionPredicate((docName, description) => true);
            options.CustomSchemaIds(type => type.FullName);
        });
    }
    public static void ConfigureWithAuth(
        ServiceConfigurationContext context,
        string authority,
        Dictionary<string, string> scopes,
        string apiTitle,
        string apiVersion = "v1",
        string apiName = "v1"
    )
    {
        context.Services.AddAbpSwaggerGenWithOAuth(
            authority: authority,
            scopes: scopes,
            options =>
            {
                options.SwaggerDoc(apiName, new OpenApiInfo { Title = apiTitle, Version = apiVersion });
                options.DocInclusionPredicate((docName, description) => true);
                options.CustomSchemaIds(type => type.FullName);
            },
            authorizationEndpoint: "/protocol/openid-connect/auth",
            tokenEndpoint: "/protocol/openid-connect/token"
            );
    }

    /// <summary>
    /// Fix Issues : Swagger Authorize With Keycloak Get Message "we are sorry"
    /// The Issues : https://github.com/abpframework/eShopOnAbp/issues/145
    /// </summary>
    public static IServiceCollection AddAbpSwaggerGenWithOAuth(
        this IServiceCollection services,
        [NotNull] string authority,
        [NotNull] Dictionary<string, string> scopes,
        Action<SwaggerGenOptions> setupAction = null,
        string authorizationEndpoint = "/connect/authorize",
        string tokenEndpoint = "/connect/token")
    {
        var authorizationUrl = new Uri($"{authority.TrimEnd('/')}{authorizationEndpoint.EnsureStartsWith('/')}");
        var tokenUrl = new Uri($"{authority.TrimEnd('/')}{tokenEndpoint.EnsureStartsWith('/')}");

        return services
            .AddAbpSwaggerGen()
            .AddSwaggerGen(
                options =>
                {
                    options.AddSecurityDefinition("oauth2", new OpenApiSecurityScheme
                    {
                        Type = SecuritySchemeType.OAuth2,
                        Flows = new OpenApiOAuthFlows
                        {
                            AuthorizationCode = new OpenApiOAuthFlow
                            {
                                AuthorizationUrl = authorizationUrl,
                                Scopes = scopes,
                                TokenUrl = tokenUrl
                            }
                        }
                    });

                    options.AddSecurityRequirement(new OpenApiSecurityRequirement
                    {
                            {
                                new OpenApiSecurityScheme
                                {
                                    Reference = new OpenApiReference
                                    {
                                        Type = ReferenceType.SecurityScheme,
                                        Id = "oauth2"
                                    }
                                },
                                Array.Empty<string>()
                            }
                    });

                    setupAction?.Invoke(options);
                });
    }
}
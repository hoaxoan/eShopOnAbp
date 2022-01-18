﻿using EShopOnAbp.BasketService;
using EShopOnAbp.CatalogService;
using EShopOnAbp.Localization;
using EShopOnAbp.PaymentService;
using EShopOnAbp.PublicWeb.Menus;
using EShopOnAbp.Shared.Hosting.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using StackExchange.Redis;
using System;
using System.Net.Http.Headers;
using EShopOnAbp.OrderingService;
using Volo.Abp;
using Volo.Abp.Account;
using Volo.Abp.AspNetCore.Authentication.OpenIdConnect;
using Volo.Abp.AspNetCore.Mvc.Client;
using Volo.Abp.AspNetCore.Mvc.Localization;
using Volo.Abp.AspNetCore.Mvc.UI.Theme.Basic;
using Volo.Abp.AspNetCore.Mvc.UI.Theme.Shared;
using Volo.Abp.AspNetCore.Mvc.UI.Theme.Shared.Toolbars;
using Volo.Abp.AspNetCore.SignalR;
using Volo.Abp.AutoMapper;
using Volo.Abp.Caching;
using Volo.Abp.Caching.StackExchangeRedis;
using Volo.Abp.EventBus.RabbitMq;
using Volo.Abp.Http.Client.IdentityModel.Web;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.UI.Navigation;
using Volo.Abp.UI.Navigation.Urls;
using Yarp.ReverseProxy.Transforms;
using Volo.Abp.AspNetCore.Mvc.UI.Bundling;
using Volo.Abp.AspNetCore.Mvc.UI.Theme.Basic.Bundling;
using EShopOnAbp.PublicWeb.Components.Toolbar.Cart;
using EShopOnAbp.PublicWeb.PaymentMethods;
using EShopOnAbp.PaymentService.PaymentMethods;
using Microsoft.Extensions.Configuration;

namespace EShopOnAbp.PublicWeb
{
    [DependsOn(
        typeof(AbpCachingStackExchangeRedisModule),
        typeof(AbpEventBusRabbitMqModule),
        typeof(AbpAspNetCoreMvcClientModule),
        typeof(AbpAspNetCoreAuthenticationOpenIdConnectModule),
        typeof(AbpHttpClientIdentityModelWebModule),
        typeof(AbpAspNetCoreMvcUiBasicThemeModule),
        typeof(AbpAccountHttpApiClientModule),
        typeof(EShopOnAbpSharedHostingAspNetCoreModule),
        typeof(EShopOnAbpSharedLocalizationModule),
        typeof(CatalogServiceHttpApiClientModule),
        typeof(BasketServiceHttpApiClientModule),
        typeof(OrderingServiceHttpApiClientModule),
        typeof(AbpAspNetCoreSignalRModule),
        typeof(PaymentServiceHttpApiClientModule),
        typeof(AbpAutoMapperModule)
        )]
    public class EShopOnAbpPublicWebModule : AbpModule
    {
        public override void PreConfigureServices(ServiceConfigurationContext context)
        {
            context.Services.PreConfigure<AbpMvcDataAnnotationsLocalizationOptions>(options =>
            {
                options.AddAssemblyResource(
                    typeof(EShopOnAbpResource),
                    typeof(EShopOnAbpPublicWebModule).Assembly
                );
            });
        }

        public override void ConfigureServices(ServiceConfigurationContext context)
        {
            Microsoft.IdentityModel.Logging.IdentityModelEventSource.ShowPII = true;
            var hostingEnvironment = context.Services.GetHostingEnvironment();
            var configuration = context.Services.GetConfiguration();

            context.Services.AddAutoMapperObjectMapper<EShopOnAbpPublicWebModule>();
            Configure<AbpAutoMapperOptions>(options =>
            {
                options.AddMaps<EShopOnAbpPublicWebModule>(validate: true);
            });

            Configure<AbpBundlingOptions>(options =>
            {
                options.StyleBundles.Configure(
                   BasicThemeBundles.Styles.Global,
                   bundle =>
                   {
                       bundle.AddContributors(typeof(CartWidgetStyleContributor));
                   }
               );
            });

            Configure<AbpMultiTenancyOptions>(options =>
            {
                options.IsEnabled = true;
            });

            Configure<AbpDistributedCacheOptions>(options =>
            {
                options.KeyPrefix = "EShopOnAbp:";
            });

            Configure<AppUrlOptions>(options =>
            {
                options.Applications["MVC"].RootUrl = configuration["App:SelfUrl"];
            });

            ConfigurePayment(configuration);

            context.Services.AddAuthentication(options =>
                {
                    options.DefaultScheme = "Cookies";
                    options.DefaultChallengeScheme = "oidc";
                })
                .AddCookie("Cookies", options =>
                {
                    options.ExpireTimeSpan = TimeSpan.FromDays(365);
                })
                .AddAbpOpenIdConnect("oidc", options =>
                {
                    options.Authority = configuration["AuthServer:Authority"];
                    options.RequireHttpsMetadata = Convert.ToBoolean(configuration["AuthServer:RequireHttpsMetadata"]);
                    options.ResponseType = OpenIdConnectResponseType.CodeIdToken;

                    options.ClientId = configuration["AuthServer:ClientId"];
                    options.ClientSecret = configuration["AuthServer:ClientSecret"];

                    options.SaveTokens = true;
                    options.GetClaimsFromUserInfoEndpoint = true;

                    options.Scope.Add("role");
                    options.Scope.Add("email");
                    options.Scope.Add("phone");
                    options.Scope.Add("AuthServer");
                    options.Scope.Add("AdministrationService");
                    options.Scope.Add("BasketService");
                    options.Scope.Add("CatalogService");
                    options.Scope.Add("PaymentService");
                    options.Scope.Add("OrderingService");
                });

            var redis = ConnectionMultiplexer.Connect(configuration["Redis:Configuration"]);
            context.Services
                .AddDataProtection()
                .PersistKeysToStackExchangeRedis(redis, "EShopOnAbp-Protection-Keys")
                .SetApplicationName("eShopOnAbp-PublicWeb");

            Configure<AbpNavigationOptions>(options =>
            {
                options.MenuContributors.Add(new EShopOnAbpPublicWebMenuContributor(configuration));
            });

            Configure<AbpToolbarOptions>(options =>
            {
                options.Contributors.Add(new EShopOnAbpPublicWebToolbarContributor());
            });
            
            context.Services
                .AddReverseProxy()
                .LoadFromConfig(configuration.GetSection("ReverseProxy"))
                .AddTransforms(builderContext =>
                {
                    builderContext.AddRequestTransform(async (transformContext) =>
                    {
                        transformContext.ProxyRequest.Headers
                            .Authorization = new AuthenticationHeaderValue(
                                "Bearer",
                                await transformContext.HttpContext.GetTokenAsync("access_token")
                            );
                    });
                });
        }

        private void ConfigurePayment(IConfiguration configuration)
        {
            Configure<EShopOnAbpPublicWebPaymentOptions>(options =>
            {
                options.PaymentSuccessfulCallbackUrl = configuration["App:SelfUrl"].EnsureEndsWith('/') + "PaymentCompleted";
            });

            Configure<PaymentMethodUiOptions>(options =>
            {
                options.ConfigureIcon(PaymentMethodNames.PayPal, "fa-cc-paypal paypal");
            });
        }

        public override void OnApplicationInitialization(ApplicationInitializationContext context)
        {
            var app = context.GetApplicationBuilder();
            var env = context.GetEnvironment();
            
            app.Use((ctx, next) =>
            {
                ctx.Request.Scheme = "https";
                return next();
            });

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseAbpRequestLocalization();

            if (!env.IsDevelopment())
            {
                app.UseErrorPage();
            }

            app.UseCorrelationId();
            app.UseStaticFiles();
            app.UseRouting();
            // app.UseHttpMetrics();
            app.UseAuthentication();
            app.UseAbpSerilogEnrichers();
            app.UseAuthorization();
            app.UseConfiguredEndpoints(endpoints =>
            {
                endpoints.MapReverseProxy();
                // endpoints.MapMetrics();
            });
        }
    }
}

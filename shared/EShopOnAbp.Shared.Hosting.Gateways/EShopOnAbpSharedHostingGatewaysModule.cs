using EShopOnAbp.Shared.Hosting.AspNetCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ocelot.DependencyInjection;
using Ocelot.Provider.Kubernetes;
using Ocelot.Provider.Polly;
using Volo.Abp.Modularity;

namespace EShopOnAbp.Shared.Hosting.Gateways
{
    [DependsOn(
        typeof(EShopOnAbpSharedHostingAspNetCoreModule)
    )]
    public class EShopOnAbpSharedHostingGatewaysModule : AbpModule
    {
        public override void ConfigureServices(ServiceConfigurationContext context)
        {
            var configuration = context.Services.GetConfiguration();
            var env = context.Services.GetHostingEnvironment();

            if (env.IsStaging())
            {
                context.Services.AddOcelot(configuration)
                    .AddKubernetes()
                    .AddPolly();
            }
            else
            {
                context.Services.AddOcelot(configuration)
                    .AddPolly();
            }
        }
    }
}
using Microsoft.Extensions.DependencyInjection;
using Weikio.ApiFramework.Abstractions.DependencyInjection;
using Weikio.ApiFramework.SDK;

namespace Adafy.ApiFramework.Plugins.Procountor
{
    public static class ServiceExtensions
    {
        public static IApiFrameworkBuilder AddProcountorApi(this IApiFrameworkBuilder builder, string endpoint  = null, ProcountorOptions configuration = null)
        {
            builder.Services.AddProcountorApi(endpoint, configuration);

            return builder;
        }
        
        public static IServiceCollection AddProcountorApi(this IServiceCollection services, string endpoint = null, ProcountorOptions configuration =null)
        {
            services.RegisterPlugin(endpoint, configuration);

            return services;
        }
    }
}

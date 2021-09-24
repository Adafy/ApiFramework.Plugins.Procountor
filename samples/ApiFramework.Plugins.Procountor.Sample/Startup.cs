using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Weikio.ApiFramework;

namespace Adafy.ApiFramework.Plugins.Procountor.Sample
{
    public class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllers();

            services.AddApiFrameworkStarterKit()
                .AddProcountorApi("/procountor", new ProcountorOptions()
                {
                    ApiKey = "",
                    ClientId = "",
                    ClientSecret = "",
                    RedirectUri = "",
                    Url = "https://api.procountor.com/latest/api",
                    IsAutoPagingEnabled = true,
                    IsReadOnly = false
                });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            
            app.UseHttpsRedirection();
            app.UseRouting();
            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }
    }
}

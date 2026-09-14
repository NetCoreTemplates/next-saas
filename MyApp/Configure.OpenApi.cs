using Scalar.AspNetCore;

[assembly: HostingStartup(typeof(MyApp.ConfigureOpenApi))]

namespace MyApp;

public class ConfigureOpenApi : IHostingStartup
{
    public void Configure(IWebHostBuilder builder) => builder.ConfigureServices((context, services) =>
    {
        if (!context.HostingEnvironment.IsDevelopment()) return;
        services.AddOpenApi();
        services.AddServiceStackOpenApi();
        services.AddTransient<IStartupFilter, StartupFilter>();
    });

    public class StartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.UseRouting();
            app.UseEndpoints(endpoints => {
                endpoints.MapOpenApi();
                endpoints.MapScalarApiReference();
            });
            next(app);
        };
    }
}

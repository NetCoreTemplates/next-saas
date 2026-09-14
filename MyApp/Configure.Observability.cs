using System.Diagnostics;

[assembly: HostingStartup(typeof(MyApp.ConfigureObservability))]

namespace MyApp;

public class ConfigureObservability : IHostingStartup
{
    public void Configure(IWebHostBuilder builder) => builder.ConfigureServices(services =>
        services.AddTransient<IStartupFilter, CorrelationStartupFilter>());
}

public class CorrelationStartupFilter(ILogger<CorrelationStartupFilter> log) : IStartupFilter
{
    private const string Header = "X-Request-Id";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, continuation) =>
        {
            var supplied = context.Request.Headers[Header].FirstOrDefault();
            var requestId = !string.IsNullOrWhiteSpace(supplied) && supplied.Length <= 128
                ? supplied : Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
            context.TraceIdentifier = requestId;
            context.Request.Headers[Header] = requestId;
            context.Response.Headers[Header] = requestId;
            using (log.BeginScope(new Dictionary<string, object> { ["RequestId"] = requestId }))
                await continuation();
        });
        next(app);
    };
}

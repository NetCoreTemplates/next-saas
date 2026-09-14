using ServiceStack.Web;
using ServiceStack.Jobs;

[assembly: HostingStartup(typeof(MyApp.ConfigureRequestLogs))]

namespace MyApp;

public class ConfigureRequestLogs : IHostingStartup
{
    public void Configure(IWebHostBuilder builder) => builder.ConfigureServices((context, services) =>
    {
        services.AddPlugin(new RequestLogsFeature {
            RequestLogger = new SqliteRequestLogger(),
            // Request bodies can contain passwords, API keys, uploaded content, and
            // billing metadata. Keep body capture as a local-development aid only.
            EnableRequestBodyTracking = context.HostingEnvironment.IsDevelopment(),
            EnableErrorTracking = true,
            ExcludeRequestDtoTypes = [typeof(ServiceModel.StripeWebhook)],
        });
        services.AddHostedService<RequestLogsHostedService>();
    });
}

public class RequestLogsHostedService(ILogger<RequestLogsHostedService> log, IRequestLogger requestLogger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var logger = (SqliteRequestLogger)requestLogger;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
            await logger.TickAsync(log, stoppingToken);
    }
}

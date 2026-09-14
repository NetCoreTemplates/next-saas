using Microsoft.AspNetCore.Identity;
using ServiceStack.Jobs;
using MyApp.Data;
using MyApp.ServiceInterface;

[assembly: HostingStartup(typeof(MyApp.ConfigureBackgroundJobs))]

namespace MyApp;

public class ConfigureBackgroundJobs : IHostingStartup
{
    public void Configure(IWebHostBuilder builder) => builder
        .ConfigureServices((context,services) => {
            var smtpConfig = context.Configuration.GetSection(nameof(SmtpConfig))?.Get<SmtpConfig>();
            if (smtpConfig is not null)
            {
                services.AddSingleton(smtpConfig);
            }
            // Lazily register SendEmailCommand to allow SmtpConfig to only be required if used 
            services.AddTransient<SendEmailCommand>(c => new SendEmailCommand(
                c.GetRequiredService<ILogger<SendEmailCommand>>(),
                c.GetRequiredService<IBackgroundJobs>(),
                c.GetRequiredService<SmtpConfig>(),
                c.GetRequiredService<ServiceStack.Data.IDbConnectionFactory>()));
            services.AddTransient<ProcessStripeEventCommand>();
            services.AddTransient<DeleteStoredFileCommand>();
            services.AddTransient<ProcessWorkspaceLifecycleCommand>();
            services.AddTransient<ProcessNotificationDeliveryCommand>();
            services.AddTransient<ExpireUsageReservationsCommand>();
            services.AddTransient<BuildUsageRollupsCommand>();
            services.AddTransient<BuildSaasDailySnapshotCommand>();
            services.AddTransient<QueueQuotaNotificationsCommand>();
            services.AddTransient<ProcessDueWorkspaceLifecyclesCommand>();
            services.AddTransient<ExpireDataExportsCommand>();
            services.AddTransient<ApplyDataRetentionCommand>();
            services.AddTransient<ReconcileStripeSubscriptionsCommand>();
            
            services.AddPlugin(new CommandsFeature());
            services.AddPlugin(new BackgroundsJobFeature());
            services.AddHostedService<JobsHostedService>();
        }).ConfigureAppHost(afterAppHostInit: appHost => {
            var services = appHost.GetApplicationServices();

            // Log if EmailSender is enabled and SmtpConfig missing
            var log = services.GetRequiredService<ILogger<ConfigureBackgroundJobs>>();
            var emailSender = services.GetRequiredService<IEmailSender<ApplicationUser>>();
            if (emailSender is EmailSender)
            {
                var smtpConfig = services.GetService<SmtpConfig>();
                if (smtpConfig is null)
                {
                    log.LogWarning("SMTP is not configured, please configure SMTP to enable sending emails");
                }
                else
                {
                    log.LogWarning("SMTP is configured with <{FromEmail}> {FromName}", smtpConfig.FromEmail, smtpConfig.FromName);
                }
            }
            
            var jobs = services.GetRequiredService<IBackgroundJobs>();
            jobs.RecurringCommand<ExpireUsageReservationsCommand>(Schedule.Hourly);
            jobs.RecurringCommand<BuildUsageRollupsCommand>(Schedule.Hourly);
            jobs.RecurringCommand<BuildSaasDailySnapshotCommand>(Schedule.Hourly);
            jobs.RecurringCommand<QueueQuotaNotificationsCommand>(Schedule.Hourly);
            jobs.RecurringCommand<ProcessDueWorkspaceLifecyclesCommand>(Schedule.Hourly);
            jobs.RecurringCommand<ExpireDataExportsCommand>(Schedule.Hourly);
            jobs.RecurringCommand<ApplyDataRetentionCommand>(Schedule.Daily);
            jobs.RecurringCommand<ReconcileStripeSubscriptionsCommand>(Schedule.Hourly);
        });
}

public class JobsHostedService(IBackgroundJobs jobs) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await jobs.StartAsync(stoppingToken);
        
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            await jobs.TickAsync();
        }
    }
}

/// <summary>
/// Sends emails by executing SendEmailCommand in a background job where it's serially processed by 'smtp' worker
/// </summary>
public class EmailSender(IBackgroundJobs jobs) : IEmailSender<ApplicationUser>
{
    public Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        jobs.EnqueueCommand<SendEmailCommand>(new SendEmail {
            To = email,
            Subject = subject,
            BodyHtml = htmlMessage,
        });
        return Task.CompletedTask;
    }

    public Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink) =>
        SendEmailAsync(email, "Confirm your email", $"Please confirm your account by <a href='{confirmationLink}'>clicking here</a>.");

    public Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink) =>
        SendEmailAsync(email, "Reset your password", $"Please reset your password by <a href='{resetLink}'>clicking here</a>.");

    public Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode) =>
        SendEmailAsync(email, "Reset your password", $"Please reset your password using the following code: {resetCode}");
}

using Microsoft.Extensions.Configuration;
using MyApp.ServiceModel;

namespace MyApp.ServiceInterface;

public class AppConfig
{
    public string? BaseUrl { get; set; }
}

public class DeploymentConfig
{
    public bool EnforceStartupChecks { get; set; } = true;
    public bool RequireHttps { get; set; } = true;
    /// <summary>
    /// Requires a networked database server (any provider other than Sqlite), so the
    /// deployment can run more than one application instance and back up independently.
    /// </summary>
    public bool RequireNetworkDatabase { get; set; } = true;
    public bool RequireSmtp { get; set; } = true;
    public bool RequireStripe { get; set; } = true;
    public bool RequireStripeWebhook { get; set; } = true;
    public bool AllowTestStripeKeys { get; set; }
    public bool RequireRestrictedHosts { get; set; } = true;
    public bool RequireExplicitMigrations { get; set; } = true;
}

public class SecurityConfig
{
    public bool EnableSecurityHeaders { get; set; } = true;
    public bool RequireSameOriginForCookieApi { get; set; } = true;
    public int AuthenticationRequestsPerMinute { get; set; } = 20;
    public string ContentSecurityPolicy { get; set; } =
        "default-src 'self'; base-uri 'self'; object-src 'none'; frame-ancestors 'none'; " +
        "form-action 'self'; img-src 'self' data: blob:; font-src 'self' data:; " +
        "style-src 'self' 'unsafe-inline'; script-src 'self' 'unsafe-inline'; " +
        "connect-src 'self'; worker-src 'self' blob:; manifest-src 'self'";

    /// <summary>
    /// ServiceStack's built-in operator UIs are Vue applications that compile templates at runtime
    /// with new Function(), which CSP treats as eval. Only these paths receive the relaxed policy;
    /// every customer-facing route keeps <see cref="ContentSecurityPolicy"/> unchanged.
    /// </summary>
    public List<string> ToolingPaths { get; set; } = ["/admin-ui", "/ui", "/metadata"];

    /// <summary>
    /// Policy served on <see cref="ToolingPaths"/>. Left empty it is derived from
    /// <see cref="ContentSecurityPolicy"/> by adding 'unsafe-eval' to script-src, so the two
    /// cannot drift apart. Set it explicitly to override that derivation.
    /// </summary>
    public string ToolingContentSecurityPolicy { get; set; } = "";
}

public record ProductionReadinessResult(List<string> Errors, List<string> Warnings)
{
    public bool IsReady => Errors.Count == 0;
}

public static class ProductionReadiness
{
    public static ProductionReadinessResult Evaluate(IConfiguration values, DeploymentConfig deployment,
        AppConfig app, ProductConfig product, NotificationConfig notifications, StripeConfig stripe)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        if (!Uri.TryCreate(app.BaseUrl, UriKind.Absolute, out var baseUri))
            errors.Add("AppConfig.BaseUrl must be an absolute public URL.");
        else
        {
            if (deployment.RequireHttps && baseUri.Scheme != Uri.UriSchemeHttps)
                errors.Add("AppConfig.BaseUrl must use HTTPS in production.");
            if (baseUri.IsLoopback || baseUri.Host.EndsWith(".example.com", StringComparison.OrdinalIgnoreCase))
                errors.Add("AppConfig.BaseUrl must identify the deployed public host, not localhost or an example domain.");
        }
        if (deployment.RequireRestrictedHosts && values["AllowedHosts"] is "*" or null or "")
            errors.Add("AllowedHosts must be restricted to the deployed hostname in production.");
        if (product.SupportEmail.EndsWith("@example.com", StringComparison.OrdinalIgnoreCase) || !product.SupportEmail.Contains('@'))
            errors.Add("Product.SupportEmail must be a real monitored address.");
        if (deployment.RequireNetworkDatabase && values.GetValue("Database:Provider", "Sqlite").Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
            errors.Add("Database.Provider must be a networked database server in production, or Deployment.RequireNetworkDatabase must be explicitly disabled.");
        var connection = values.GetConnectionString("DefaultConnection");
        if (deployment.RequireNetworkDatabase && string.IsNullOrEmpty(connection))
            errors.Add("ConnectionStrings.DefaultConnection is required for a networked database server.");
        if (deployment.RequireExplicitMigrations && values.GetValue("Database:AutoMigrateEmpty", true))
            errors.Add("Database.AutoMigrateEmpty must be false in production; run the migrate app task during deployment.");
        if (deployment.RequireSmtp && notifications.Provider != EmailProvider.Smtp)
            errors.Add("Notifications.Provider must be Smtp in production so invitations and account recovery are deliverable.");
        if (deployment.RequireStripe)
        {
            if (string.IsNullOrEmpty(stripe.SecretKey) || string.IsNullOrEmpty(stripe.PublishableKey))
                errors.Add("Stripe publishable and secret keys are required by this deployment policy.");
            if (deployment.RequireStripeWebhook && string.IsNullOrEmpty(stripe.WebhookSecret))
                errors.Add("Stripe.WebhookSecret is required so recurring billing changes are verified.");
            var testMode = stripe.SecretKey.StartsWith("sk_test_", StringComparison.OrdinalIgnoreCase) || stripe.SecretKey.StartsWith("rk_test_", StringComparison.OrdinalIgnoreCase);
            if (testMode && !deployment.AllowTestStripeKeys)
                errors.Add("Stripe test keys are not allowed in production unless Deployment.AllowTestStripeKeys is explicitly enabled.");
        }
        else if (string.IsNullOrEmpty(stripe.SecretKey))
            warnings.Add("Stripe is disabled; only free or externally provisioned plans can be used.");
        return new(errors, warnings);
    }
}

public class SaasConfig
{
    public string DefaultPlan { get; set; } = "free";
    public string DefaultCurrency { get; set; } = "usd";
    public List<string> SupportedCurrencies { get; set; } = ["usd"];
    public bool EnablePersonalWorkspaces { get; set; } = true;
    public bool EnableTrials { get; set; } = true;
    public int DefaultTrialDays { get; set; } = 14;
    public bool TrialRequiresPaymentMethod { get; set; }
    public bool EnableAnnualBilling { get; set; } = true;
    public int PastDueGraceDays { get; set; } = 7;
    public string AfterGraceAccessMode { get; set; } = "FreeFallback";
    public List<int> QuotaWarningPercentages { get; set; } = [80, 90, 100];
    public bool EnableSupportAccess { get; set; }
    public int SupportAccessMaxMinutes { get; set; } = 60;
    public int WorkspaceDeletionDelayDays { get; set; } = 7;
    public int ExportExpiryDays { get; set; } = 7;
    public int InvitationExpiryDays { get; set; } = 7;
    public int AnalyticsRetentionDays { get; set; } = 365;
    public int AuditRetentionDays { get; set; } = 730;
    public int NotificationRetentionDays { get; set; } = 90;
    public int DeletedFileRetentionDays { get; set; } = 30;
    public int LifecycleHistoryRetentionDays { get; set; } = 365;
    public int RetentionBatchSize { get; set; } = 1000;
    public bool EnableLegalHolds { get; set; } = true;
    public int ApiKeyRequestsPerMinute { get; set; } = 120;
    public List<SaasFeatureConfig> Features { get; set; } = [
        new() { Key = "files.basic", DisplayName = "File storage", Description = "Upload, download, and delete files.", Category = "Storage", DefaultEnabled = true },
        new() { Key = "analytics.basic", DisplayName = "Usage analytics", Description = "View current and historical usage.", Category = "Analytics", DefaultEnabled = true },
        new() { Key = "analytics.advanced", DisplayName = "Advanced analytics", Description = "View usage breakdowns and projections.", Category = "Analytics" },
        new() { Key = "api.access", DisplayName = "API access", Description = "Create and use organization API keys.", Category = "Developer" },
        new() { Key = "audit.read", DisplayName = "Audit logs", Description = "View the organization audit trail.", Category = "Security" },
        new() { Key = "branding.custom", DisplayName = "Custom branding", Description = "Customize customer-facing branding.", Category = "Customization" },
        new() { Key = "support.community", DisplayName = "Community support", Description = "Access community support resources.", Category = "Support", DefaultEnabled = true },
    ];
    public List<SaasMeterConfig> Meters { get; set; } = [
        new() { Key = "documents.stored", DisplayName = "Documents stored", UnitName = "document", Kind = MeterKind.Gauge, Reset = MeterReset.Never, Aggregation = MeterAggregation.Current },
        new() { Key = "storage.bytes", DisplayName = "Storage", UnitName = "byte", Kind = MeterKind.Gauge, Reset = MeterReset.Never, Aggregation = MeterAggregation.Maximum },
        new() { Key = "documents.uploaded", DisplayName = "Documents uploaded", UnitName = "document", Kind = MeterKind.Counter },
        new() { Key = "api.requests", DisplayName = "API requests", UnitName = "request", Kind = MeterKind.Counter },
    ];
}

public class SaasFeatureConfig
{
    public string Key { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "General";
    public bool DefaultEnabled { get; set; }
}

public class SaasMeterConfig
{
    public string Key { get; set; } = "api.requests";
    public string DisplayName { get; set; } = "API requests";
    public string UnitName { get; set; } = "request";
    public string DefaultEnforcement { get; set; } = "HardLimit";
    public MeterKind Kind { get; set; } = MeterKind.Counter;
    public MeterReset Reset { get; set; } = MeterReset.BillingPeriod;
    public MeterAggregation Aggregation { get; set; } = MeterAggregation.Sum;
    public bool AllowCustomerBreakdown { get; set; } = true;
    public bool AllowUserBreakdown { get; set; } = true;
}

public class ProductConfig
{
    public string OrganizationName { get; set; } = "Acme";
    public string ProductName { get; set; } = "Acme";
    public string Description { get; set; } = "Secure document storage and analytics.";
    public string SupportEmail { get; set; } = "support@example.com";
    public string SalesEmail { get; set; } = "sales@example.com";
    public string PrivacyUrl { get; set; } = "/privacy";
    public string TermsUrl { get; set; } = "/terms";
}

public class FileStorageConfig
{
    public string RootPath { get; set; } = "App_Data/files";
    public long MaxFileBytes { get; set; } = 100 * 1024 * 1024;
    public List<string> AllowedExtensions { get; set; } = [".pdf", ".txt", ".md", ".csv", ".json", ".doc", ".docx", ".xls", ".xlsx", ".png", ".jpg", ".jpeg"];
}

public enum EmailProvider { Development, Smtp, Disabled }

public class NotificationConfig
{
    public string FromName { get; set; } = "Acme";
    public string FromEmail { get; set; } = "noreply@example.com";
    public EmailProvider Provider { get; set; } = EmailProvider.Development;
    public bool EnableInApp { get; set; } = true;
    public int MaxAttempts { get; set; } = 5;
}

public class StripeConfig
{
    public string PublishableKey { get; set; } = "";
    public string SecretKey { get; set; } = "";
    public string WebhookSecret { get; set; } = "";
    public string PortalConfigurationId { get; set; } = "";
    public bool EnableCatalogProvisioning { get; set; } = true;
    public bool AllowLiveCatalogProvisioning { get; set; }
}

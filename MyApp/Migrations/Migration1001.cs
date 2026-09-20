using MyApp.ServiceModel;
using ServiceStack.OrmLite;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyApp.Migrations;

public class Migration1001 : MigrationBase
{
    public override void Up()
    {
        Db.CreateTable<Workspace>();
        Db.CreateTable<WorkspaceMember>();
        Db.CreateTable<UserWorkspacePreference>();
        Db.CreateTable<SaasPlan>();
        Db.CreateTable<SaasPlanVersion>();
        Db.CreateTable<SaasPlanPrice>();
        Db.CreateTable<SaasPlanFeature>();
        Db.CreateTable<SaasPlanQuota>();
        Db.CreateTable<BillingSubscription>();
        Db.CreateTable<UsagePeriod>();
        Db.CreateTable<UsageAggregate>();
        Db.CreateTable<UsageEvent>();
        Db.CreateTable<UsageReservation>();
        Db.CreateTable<UsageDailyRollup>();
        Db.CreateTable<SaasDailySnapshot>();
        Db.CreateTable<CustomerEntitlementOverride>();
        Db.CreateTable<StripeEventInbox>();
        Db.CreateTable<SaasAuditEvent>();
        Db.CreateTable<StoredFile>();
        Db.CreateTable<NotificationPreference>();
        Db.CreateTable<NotificationDelivery>();
        Db.CreateTable<SupportNote>();
        Db.CreateTable<SupportAccessGrant>();
        Db.CreateTable<WorkspaceLifecycleRequest>();
        Db.CreateTable<DataExportArtifact>();
        Db.CreateTable<WorkspaceRetentionPolicy>();
        Db.CreateTable<DataRetentionRun>();

        foreach (var plan in LoadPlans())
            SeedPlan(plan.Code, plan.Name, plan.Description, plan.DisplayOrder, plan.ContactSales, plan.Audience,
                plan.Monthly, plan.Annual, plan.Documents, plan.StorageBytes, plan.ApiRequests, plan.Seats,
                plan.Features.Select(x => (x.Key, x.Name)).ToArray(), plan.TrialDays);

    }

    private void SeedPlan(string code, string name, string description, int order, bool contactSales, PlanAudience audience,
        long monthly, long annual, long? documents, long? storageBytes, long? apiRequests,
        long? seats, (string Key, string Name)[] features, int? trialDays)
    {
        var now = DateTime.UtcNow;
        var planId = $"plan.{code}";
        var versionId = $"plan.{code}.v1";
        Db.Insert(new SaasPlan {
            Id = planId, Code = code, Name = name, Description = description, DisplayOrder = order,
            IsContactSales = contactSales, Audience = audience, CreatedDate = now, ModifiedDate = now,
        });
        Db.Insert(new SaasPlanVersion {
            Id = versionId, PlanId = planId, Version = 1, Status = PlanVersionStatus.Published,
            Name = name, Description = description, DisplayOrder = order, IsPublic = true,
            IsContactSales = contactSales, IsArchived = false, Audience = audience,
            TrialDays = trialDays, EffectiveFrom = now, PublishedDate = now,
            PublishedBy = "migration", CreatedDate = now, ModifiedDate = now,
        });
        if (!contactSales)
        {
            Db.Insert(new SaasPlanPrice {
                Id = $"price.{code}.month", PlanVersionId = versionId, Interval = BillingInterval.Month,
                UnitAmount = monthly, CreatedDate = now, ModifiedDate = now,
            });
            Db.Insert(new SaasPlanPrice {
                Id = $"price.{code}.year", PlanVersionId = versionId, Interval = BillingInterval.Year,
                UnitAmount = annual, CreatedDate = now, ModifiedDate = now,
            });
        }
        for (var i = 0; i < features.Length; i++)
        {
            Db.Insert(new SaasPlanFeature {
                Id = $"feature.{code}.{i + 1}", PlanVersionId = versionId, Key = features[i].Key,
                Name = features[i].Name, DisplayOrder = i, CreatedDate = now, ModifiedDate = now,
            });
        }
        InsertQuota(code, versionId, "documents", "documents.stored", "Documents stored", documents, contactSales, now);
        InsertQuota(code, versionId, "storage", "storage.bytes", "Storage", storageBytes, contactSales, now);
        InsertQuota(code, versionId, "uploads", "documents.uploaded", "Documents uploaded", documents == null ? null : documents * 5, true, now);
        InsertQuota(code, versionId, "api", "api.requests", "API requests", apiRequests, contactSales, now);
        Db.Insert(new SaasPlanQuota {
            Id = $"quota.{code}.seats", PlanVersionId = versionId, MeterKey = "workspace.seats",
            DisplayName = "Team seats", IncludedUnits = seats,
            Enforcement = contactSales ? QuotaEnforcement.SoftLimit : QuotaEnforcement.HardLimit,
            CreatedDate = now, ModifiedDate = now,
        });
    }

    private void InsertQuota(string code, string versionId, string id, string meterKey, string displayName,
        long? allowance, bool contactSales, DateTime now) => Db.Insert(new SaasPlanQuota {
            Id = $"quota.{code}.{id}", PlanVersionId = versionId, MeterKey = meterKey,
            DisplayName = displayName, IncludedUnits = allowance,
            Enforcement = contactSales ? QuotaEnforcement.SoftLimit : QuotaEnforcement.HardLimit,
            CreatedDate = now, ModifiedDate = now,
        });

    private static List<PlanSeed> LoadPlans()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "plans.json");
        if (!File.Exists(path))
            throw new FileNotFoundException("The plan seed file was not copied to the application output.", path);
        return JsonSerializer.Deserialize<List<PlanSeed>>(File.ReadAllText(path), new JsonSerializerOptions {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() },
        }) ?? throw new InvalidOperationException("plans.json does not contain a valid plan catalog.");
    }

    private sealed class PlanSeed
    {
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public PlanAudience Audience { get; set; } = PlanAudience.Both;
        public string Description { get; set; } = "";
        public int DisplayOrder { get; set; }
        public bool ContactSales { get; set; }
        public int? TrialDays { get; set; }
        public long Monthly { get; set; }
        public long Annual { get; set; }
        public long? Documents { get; set; }
        public long? StorageBytes { get; set; }
        public long? ApiRequests { get; set; }
        public long? Seats { get; set; }
        public List<PlanFeatureSeed> Features { get; set; } = [];
    }

    private sealed class PlanFeatureSeed
    {
        public string Key { get; set; } = "";
        public string Name { get; set; } = "";
    }

    public override void Down()
    {
        Db.DropTable<DataRetentionRun>();
        Db.DropTable<WorkspaceRetentionPolicy>();
        Db.DropTable<DataExportArtifact>();
        Db.DropTable<WorkspaceLifecycleRequest>();
        Db.DropTable<SupportAccessGrant>();
        Db.DropTable<SupportNote>();
        Db.DropTable<NotificationDelivery>();
        Db.DropTable<NotificationPreference>();
        Db.DropTable<StoredFile>();
        Db.DropTable<SaasAuditEvent>();
        Db.DropTable<StripeEventInbox>();
        Db.DropTable<CustomerEntitlementOverride>();
        Db.DropTable<SaasDailySnapshot>();
        Db.DropTable<UsageDailyRollup>();
        Db.DropTable<UsageReservation>();
        Db.DropTable<UsageEvent>();
        Db.DropTable<UsageAggregate>();
        Db.DropTable<UsagePeriod>();
        Db.DropTable<BillingSubscription>();
        Db.DropTable<SaasPlanQuota>();
        Db.DropTable<SaasPlanFeature>();
        Db.DropTable<SaasPlanPrice>();
        Db.DropTable<SaasPlanVersion>();
        Db.DropTable<SaasPlan>();
        Db.DropTable<WorkspaceMember>();
        Db.DropTable<UserWorkspacePreference>();
        Db.DropTable<Workspace>();
    }
}

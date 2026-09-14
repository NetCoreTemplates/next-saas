using MyApp.ServiceInterface;
using MyApp.ServiceModel;
using NUnit.Framework;
using ServiceStack;
using ServiceStack.OrmLite;

namespace MyApp.Tests;

public class SaasManagerTests
{
    [Test]
    public void Usage_is_idempotent_and_hard_quota_is_enforced()
    {
        var factory = new OrmLiteConnectionFactory(":memory:", SqliteDialect.Provider);
        using var db = factory.Open();
        db.CreateTable<Workspace>();
        db.CreateTable<WorkspaceMember>();
        db.CreateTable<UserWorkspacePreference>();
        db.CreateTable<SaasPlan>();
        db.CreateTable<SaasPlanVersion>();
        db.CreateTable<SaasPlanPrice>();
        db.CreateTable<SaasPlanFeature>();
        db.CreateTable<SaasPlanQuota>();
        db.CreateTable<BillingSubscription>();
        db.CreateTable<UsagePeriod>();
        db.CreateTable<UsageAggregate>();
        db.CreateTable<UsageEvent>();
        db.CreateTable<CustomerEntitlementOverride>();
        db.CreateTable<SaasAuditEvent>();

        var now = DateTime.UtcNow;
        db.Insert(new SaasPlan { Id="plan.free", Code="free", Name="Free", CreatedDate=now, ModifiedDate=now });
        db.Insert(new SaasPlanVersion { Id="plan.free.v1", PlanId="plan.free", Status=PlanVersionStatus.Published, CreatedDate=now, ModifiedDate=now });
        db.Insert(new SaasPlanQuota { Id="quota.free.api", PlanVersionId="plan.free.v1", MeterKey="api.requests", DisplayName="API requests", IncludedUnits=1_000, Enforcement=QuotaEnforcement.HardLimit, CreatedDate=now, ModifiedDate=now });

        var manager = new SaasManager(new SaasConfig());
        var workspace = manager.EnsurePersonalWorkspace(db, "user-1", "Ada", "ada@example.com");
        var subscription = db.Single<BillingSubscription>(x => x.WorkspaceId == workspace.Id);

        var first = manager.RecordUsage(db, workspace, subscription, "user-1", new RecordUsage { MeterKey="api.requests", Units=900, IdempotencyKey="operation-1" });
        var replay = manager.RecordUsage(db, workspace, subscription, "user-1", new RecordUsage { MeterKey="api.requests", Units=900, IdempotencyKey="operation-1" });

        Assert.Multiple(() => {
            Assert.That(first.Accepted, Is.True);
            Assert.That(replay.Duplicate, Is.True);
            Assert.That(replay.Usage!.UsedUnits, Is.EqualTo(900));
            Assert.That(replay.Usage.DisplayName, Is.EqualTo("API requests"));
        });
        var error = Assert.Throws<HttpError>(() => manager.RecordUsage(db, workspace, subscription, "user-1", new RecordUsage { MeterKey="api.requests", Units=101, IdempotencyKey="operation-2" }));
        Assert.That(error!.ErrorCode, Is.EqualTo("QuotaExceeded"));
        Assert.That(db.Count<UsageEvent>(), Is.EqualTo(1));
        Assert.That(db.Count<SaasAuditEvent>(x => x.Action == "quota.rejected"), Is.EqualTo(1));

        db.Insert(new CustomerEntitlementOverride { WorkspaceId=workspace.Id, Key="api.requests", QuotaUnits=2_000, Reason="Contract test", CreatedDate=now, ModifiedDate=now });
        var overridden = manager.RecordUsage(db, workspace, subscription, "user-1", new RecordUsage { MeterKey="api.requests", Units=101, IdempotencyKey="operation-2" });
        Assert.Multiple(() => {
            Assert.That(overridden.Accepted, Is.True);
            Assert.That(overridden.Usage!.Allowance, Is.EqualTo(2_000));
            Assert.That(overridden.Usage.UsedUnits, Is.EqualTo(1_001));
        });

        var elapsedPeriod = db.Single<UsagePeriod>(x => x.WorkspaceId == workspace.Id && x.MeterKey == "api.requests");
        var meteredQuota = db.Single<SaasPlanQuota>(x => x.PlanVersionId == subscription.PlanVersionId && x.MeterKey == "api.requests");
        meteredQuota.RolloverEnabled = true;
        db.Update(meteredQuota);
        elapsedPeriod.PeriodStart = now.AddMonths(-2);
        elapsedPeriod.PeriodEnd = now.AddDays(-1);
        db.Update(elapsedPeriod);
        subscription.PeriodStart = elapsedPeriod.PeriodStart;
        subscription.PeriodEnd = elapsedPeriod.PeriodEnd;
        db.Update(subscription);
        var rolled = manager.GetUsage(db, workspace, subscription).Single(x => x.MeterKey == "api.requests");
        Assert.Multiple(() => {
            Assert.That(rolled.UsedUnits, Is.Zero);
            Assert.That(rolled.Allowance, Is.EqualTo(2_999));
            Assert.That(rolled.PeriodEnd, Is.GreaterThan(DateTime.UtcNow));
            Assert.That(db.Count<UsageEvent>(), Is.EqualTo(2));
        });
    }

    [Test]
    public void Plan_changes_are_drafted_and_existing_subscriptions_stay_on_the_published_version()
    {
        var factory = new OrmLiteConnectionFactory(":memory:", SqliteDialect.Provider);
        using var db = factory.Open();
        db.CreateTable<Workspace>();
        db.CreateTable<SaasPlan>();
        db.CreateTable<SaasPlanVersion>();
        db.CreateTable<SaasPlanPrice>();
        db.CreateTable<SaasPlanFeature>();
        db.CreateTable<SaasPlanQuota>();
        db.CreateTable<BillingSubscription>();
        db.CreateTable<SaasAuditEvent>();

        var now = DateTime.UtcNow;
        db.Insert(new Workspace { Id = "workspace-1", Name = "Acme", Slug = "acme", CreatedDate = now, ModifiedDate = now });
        db.Insert(new SaasPlan { Id = "plan.pro", Code = "pro", Name = "Pro", Description = "Original plan", CreatedDate = now, ModifiedDate = now });
        db.Insert(new SaasPlanVersion { Id = "plan.pro.v1", PlanId = "plan.pro", Version = 1, Status = PlanVersionStatus.Published, CreatedDate = now, ModifiedDate = now });
        db.Insert(new SaasPlanPrice { Id = "price.pro.month", PlanVersionId = "plan.pro.v1", Currency = "usd", Interval = BillingInterval.Month, UnitAmount = 4_900, CreatedDate = now, ModifiedDate = now });
        db.Insert(new SaasPlanFeature { Id = "feature.pro.analytics", PlanVersionId = "plan.pro.v1", Key = "analytics.advanced", Name = "Analytics", CreatedDate = now, ModifiedDate = now });
        db.Insert(new SaasPlanQuota { Id = "quota.pro.api", PlanVersionId = "plan.pro.v1", MeterKey = "api.requests", DisplayName = "API requests", IncludedUnits = 50_000, CreatedDate = now, ModifiedDate = now });
        db.Insert(new BillingSubscription { Id = "subscription-1", WorkspaceId = "workspace-1", PlanVersionId = "plan.pro.v1", Status = SubscriptionStatus.Active, PeriodStart = now, PeriodEnd = now.AddMonths(1), CreatedDate = now, ModifiedDate = now });

        var manager = new SaasManager(new SaasConfig());
        var draft = manager.SavePlanDraft(db, "admin-1", new SaveSaasPlanDraft {
            PlanId = "plan.pro", Name = "Pro Scale", Description = "Updated plan", DisplayOrder = 2, IsPublic = true,
            TrialDays = 21,
            Prices = [new SavePlanPrice { Currency = "USD", Interval = BillingInterval.Month, UnitAmount = 5_900, StripePriceId = "price_month_v2" }],
            Features = [new SavePlanFeature { Key = "analytics.advanced", Name = "Advanced analytics" }],
            Quotas = [new SavePlanQuota { MeterKey = "api.requests", DisplayName = "API requests", IncludedUnits = 75_000, Enforcement = QuotaEnforcement.HardLimit }],
        });

        Assert.Multiple(() => {
            Assert.That(draft.HasDraft, Is.True);
            Assert.That(draft.Version.Version, Is.EqualTo(2));
            Assert.That(draft.Version.Status, Is.EqualTo(PlanVersionStatus.Draft));
            Assert.That(draft.Version.TrialDays, Is.EqualTo(21));
            Assert.That(draft.Plan.Name, Is.EqualTo("Pro Scale"));
            Assert.That(draft.Prices.Single().UnitAmount, Is.EqualTo(5_900));
            Assert.That(db.SingleById<SaasPlan>("plan.pro")!.Name, Is.EqualTo("Pro"));
            Assert.That(db.SingleById<SaasPlanPrice>("price.pro.month")!.UnitAmount, Is.EqualTo(4_900));
            Assert.That(db.SingleById<BillingSubscription>("subscription-1")!.PlanVersionId, Is.EqualTo("plan.pro.v1"));
        });

        var published = manager.PublishPlanDraft(db, "admin-1", "plan.pro");
        Assert.Multiple(() => {
            Assert.That(published.HasDraft, Is.False);
            Assert.That(published.Version.Version, Is.EqualTo(2));
            Assert.That(published.Version.Status, Is.EqualTo(PlanVersionStatus.Published));
            Assert.That(published.Version.TrialDays, Is.EqualTo(21));
            Assert.That(db.SingleById<SaasPlan>("plan.pro")!.Name, Is.EqualTo("Pro Scale"));
            Assert.That(published.ActiveSubscriptions, Is.EqualTo(1));
            Assert.That(db.SingleById<SaasPlanVersion>("plan.pro.v1")!.Status, Is.EqualTo(PlanVersionStatus.Retired));
            Assert.That(db.SingleById<BillingSubscription>("subscription-1")!.PlanVersionId, Is.EqualTo("plan.pro.v1"));
            Assert.That(db.Count<SaasAuditEvent>(x => x.Category == "plan"), Is.EqualTo(2));
        });
    }

    [Test]
    public void Feature_entitlements_resolve_plan_and_active_customer_overrides()
    {
        var factory = new OrmLiteConnectionFactory(":memory:", SqliteDialect.Provider);
        using var db = factory.Open();
        db.CreateTable<Workspace>();
        db.CreateTable<SaasPlan>();
        db.CreateTable<SaasPlanVersion>();
        db.CreateTable<SaasPlanFeature>();
        db.CreateTable<BillingSubscription>();
        db.CreateTable<CustomerEntitlementOverride>();

        var now = DateTime.UtcNow;
        var workspace = new Workspace { Id="workspace-1", Name="Acme", Slug="acme", CreatedDate=now, ModifiedDate=now };
        var subscription = new BillingSubscription { Id="sub-1", WorkspaceId=workspace.Id, PlanVersionId="plan.pro.v1", Status=SubscriptionStatus.Active, PeriodStart=now, PeriodEnd=now.AddMonths(1), CreatedDate=now, ModifiedDate=now };
        db.Insert(workspace);
        db.Insert(new SaasPlan { Id="plan.pro", Code="pro", Name="Pro", CreatedDate=now, ModifiedDate=now });
        db.Insert(new SaasPlanVersion { Id="plan.pro.v1", PlanId="plan.pro", Status=PlanVersionStatus.Published, CreatedDate=now, ModifiedDate=now });
        db.Insert(new SaasPlanFeature { PlanVersionId="plan.pro.v1", Key="analytics.advanced", Name="Advanced analytics", Enabled=true, CreatedDate=now, ModifiedDate=now });
        db.Insert(subscription);
        db.Insert(new CustomerEntitlementOverride { WorkspaceId=workspace.Id, Key="analytics.advanced", Enabled=false, Reason="Contract", ValidUntil=now.AddDays(1), CreatedDate=now, ModifiedDate=now });
        db.Insert(new CustomerEntitlementOverride { WorkspaceId=workspace.Id, Key="audit.read", Enabled=true, Reason="Expired", ValidUntil=now.AddMinutes(-1), CreatedDate=now, ModifiedDate=now });

        var resolver = new EntitlementResolver(new SaasConfig());
        var effective = resolver.GetEffective(db, workspace, subscription);

        Assert.Multiple(() => {
            Assert.That(effective.Single(x => x.Key == "analytics.advanced").Enabled, Is.False);
            Assert.That(effective.Single(x => x.Key == "analytics.advanced").Source, Is.EqualTo("Override"));
            Assert.That(effective.Single(x => x.Key == "audit.read").Enabled, Is.False);
            Assert.That(effective.Single(x => x.Key == "audit.read").Source, Is.EqualTo("GlobalFallback"));
            Assert.That(effective.Single(x => x.Key == "files.basic").Enabled, Is.True);
        });
    }

    [Test]
    public void Gauge_reservations_settle_release_and_decrement_without_resetting()
    {
        var factory = new OrmLiteConnectionFactory(":memory:", SqliteDialect.Provider);
        using var db = factory.Open();
        db.CreateTable<Workspace>();
        db.CreateTable<WorkspaceMember>();
        db.CreateTable<UserWorkspacePreference>();
        db.CreateTable<SaasPlan>();
        db.CreateTable<SaasPlanVersion>();
        db.CreateTable<SaasPlanPrice>();
        db.CreateTable<SaasPlanFeature>();
        db.CreateTable<SaasPlanQuota>();
        db.CreateTable<BillingSubscription>();
        db.CreateTable<UsagePeriod>();
        db.CreateTable<UsageAggregate>();
        db.CreateTable<UsageEvent>();
        db.CreateTable<UsageReservation>();
        db.CreateTable<CustomerEntitlementOverride>();
        db.CreateTable<SaasAuditEvent>();

        var now = DateTime.UtcNow;
        db.Insert(new SaasPlan { Id="plan.free", Code="free", Name="Free", CreatedDate=now, ModifiedDate=now });
        db.Insert(new SaasPlanVersion { Id="plan.free.v1", PlanId="plan.free", Status=PlanVersionStatus.Published, CreatedDate=now, ModifiedDate=now });
        db.Insert(new SaasPlanQuota { PlanVersionId="plan.free.v1", MeterKey="documents.stored", DisplayName="Documents stored", IncludedUnits=2, Enforcement=QuotaEnforcement.HardLimit, CreatedDate=now, ModifiedDate=now });
        var manager = new SaasManager(new SaasConfig());
        var workspace = manager.EnsurePersonalWorkspace(db, "user-1", "Ada", "ada@example.com");
        var subscription = db.Single<BillingSubscription>(x => x.WorkspaceId == workspace.Id);

        var first = manager.ReserveUsage(db, workspace, subscription, "user-1", "documents.stored", 1, "upload-1");
        var replay = manager.ReserveUsage(db, workspace, subscription, "user-1", "documents.stored", 1, "upload-1");
        var settled = manager.SettleUsage(db, workspace, subscription, "user-1", first.Id, 1);
        var second = manager.ReserveUsage(db, workspace, subscription, "user-1", "documents.stored", 1, "upload-2");
        var released = manager.ReleaseUsage(db, workspace, subscription, "user-1", second.Id);
        var adjusted = manager.AdjustGauge(db, workspace, subscription, "user-1", "documents.stored", -1, "delete-1", "test");

        Assert.Multiple(() => {
            Assert.That(replay.Id, Is.EqualTo(first.Id));
            Assert.That(settled.UsedUnits, Is.EqualTo(1));
            Assert.That(settled.PeakUnits, Is.EqualTo(1));
            Assert.That(released.ReservedUnits, Is.Zero);
            Assert.That(adjusted.UsedUnits, Is.Zero);
            Assert.That(adjusted.PeakUnits, Is.EqualTo(1));
            Assert.That(adjusted.Reset, Is.EqualTo(MeterReset.Never));
            Assert.That(db.Count<UsageEvent>(), Is.EqualTo(2));
        });
    }

    [Test]
    public async Task Concurrent_reservations_cannot_both_spend_the_final_unit()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"next-saas-quota-{Guid.NewGuid():N}.db");
        var factory = new OrmLiteConnectionFactory($"Data Source={dbPath};Cache=Shared;Default Timeout=5", SqliteDialect.Provider);
        try
        {
            var now = DateTime.UtcNow;
            using (var db = factory.Open())
            {
                db.CreateTable<Workspace>();
                db.CreateTable<WorkspaceMember>();
                db.CreateTable<UserWorkspacePreference>();
                db.CreateTable<SaasPlan>();
                db.CreateTable<SaasPlanVersion>();
                db.CreateTable<SaasPlanPrice>();
                db.CreateTable<SaasPlanFeature>();
                db.CreateTable<SaasPlanQuota>();
                db.CreateTable<BillingSubscription>();
                db.CreateTable<UsagePeriod>();
                db.CreateTable<UsageAggregate>();
                db.CreateTable<UsageEvent>();
                db.CreateTable<UsageReservation>();
                db.CreateTable<CustomerEntitlementOverride>();
                db.CreateTable<SaasAuditEvent>();
                db.Insert(new SaasPlan { Id="plan.free", Code="free", Name="Free", CreatedDate=now, ModifiedDate=now });
                db.Insert(new SaasPlanVersion { Id="plan.free.v1", PlanId="plan.free", Status=PlanVersionStatus.Published, CreatedDate=now, ModifiedDate=now });
                db.Insert(new SaasPlanQuota { Id="quota.free.documents", PlanVersionId="plan.free.v1", MeterKey="documents.stored", DisplayName="Documents stored", IncludedUnits=1, Enforcement=QuotaEnforcement.HardLimit, CreatedDate=now, ModifiedDate=now });
                var manager = new SaasManager(new SaasConfig());
                var workspace = manager.EnsurePersonalWorkspace(db, "user-1", "Ada", "ada@example.com");
                var subscription = db.Single<BillingSubscription>(x => x.WorkspaceId == workspace.Id);
                manager.GetUsage(db, workspace, subscription); // materialize the shared period before racing
            }

            using var start = new Barrier(2);
            async Task<string> Reserve(string operationId) => await Task.Run(() => {
                using var db = factory.Open();
                var workspace = db.Select<Workspace>().Single();
                var subscription = db.Select<BillingSubscription>().Single();
                start.SignalAndWait(TimeSpan.FromSeconds(5));
                try
                {
                    new SaasManager(new SaasConfig()).ReserveUsage(db, workspace, subscription, "user-1", "documents.stored", 1, operationId);
                    return "accepted";
                }
                catch (HttpError error)
                {
                    return error.ErrorCode ?? "http-error";
                }
            });

            var outcomes = await Task.WhenAll(Reserve("concurrent-1"), Reserve("concurrent-2"));
            Assert.Multiple(() => {
                Assert.That(outcomes.Count(x => x == "accepted"), Is.EqualTo(1));
                Assert.That(outcomes.Count(x => x == "QuotaExceeded"), Is.EqualTo(1));
            });
            using var verify = factory.Open();
            var aggregate = verify.Select<UsageAggregate>().Single();
            Assert.Multiple(() => {
                Assert.That(aggregate.ReservedUnits, Is.EqualTo(1));
                Assert.That(verify.Count<UsageReservation>(), Is.EqualTo(1));
            });
        }
        finally
        {
            File.Delete(dbPath);
            File.Delete(dbPath + "-shm");
            File.Delete(dbPath + "-wal");
        }
    }

    [Test]
    public async Task Concurrent_first_loads_create_exactly_one_personal_workspace()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"next-saas-workspace-{Guid.NewGuid():N}.db");
        var factory = new OrmLiteConnectionFactory($"Data Source={dbPath};Cache=Shared;Default Timeout=5", SqliteDialect.Provider);
        try
        {
            var now = DateTime.UtcNow;
            using (var db = factory.Open())
            {
                db.CreateTable<Workspace>();
                db.CreateTable<WorkspaceMember>();
                db.CreateTable<UserWorkspacePreference>();
                db.CreateTable<SaasPlan>();
                db.CreateTable<SaasPlanVersion>();
                db.CreateTable<BillingSubscription>();
                db.CreateTable<SaasAuditEvent>();
                db.Insert(new SaasPlan { Id="plan.free", Code="free", Name="Free", CreatedDate=now, ModifiedDate=now });
                db.Insert(new SaasPlanVersion { Id="plan.free.v1", PlanId="plan.free", Status=PlanVersionStatus.Published, CreatedDate=now, ModifiedDate=now });
            }

            const int callers = 8;
            using var start = new Barrier(callers);
            async Task<Workspace> OpenWorkspace() => await Task.Run(() => {
                using var db = factory.Open();
                start.SignalAndWait(TimeSpan.FromSeconds(5));
                return new SaasManager(new SaasConfig()).EnsurePersonalWorkspace(db, "user-1", "Ada", "ada@example.com");
            });

            var workspaces = await Task.WhenAll(Enumerable.Range(0, callers).Select(_ => OpenWorkspace()));
            using var verify = factory.Open();
            Assert.Multiple(() => {
                Assert.That(workspaces.Select(x => x.Id).Distinct().Count(), Is.EqualTo(1));
                Assert.That(verify.Count<Workspace>(), Is.EqualTo(1));
                Assert.That(verify.Count<WorkspaceMember>(), Is.EqualTo(1));
                Assert.That(verify.Count<UserWorkspacePreference>(), Is.EqualTo(1));
                Assert.That(verify.Count<BillingSubscription>(), Is.EqualTo(1));
                Assert.That(verify.Count<SaasAuditEvent>(x => x.Action == "created"), Is.EqualTo(1));
            });
        }
        finally
        {
            File.Delete(dbPath);
            File.Delete(dbPath + "-shm");
            File.Delete(dbPath + "-wal");
        }
    }

    [Test]
    public void Invitations_are_not_implicitly_accepted_and_the_persisted_workspace_is_selected()
    {
        var factory = new OrmLiteConnectionFactory(":memory:", SqliteDialect.Provider);
        using var db = factory.Open();
        db.CreateTable<Workspace>();
        db.CreateTable<WorkspaceMember>();
        db.CreateTable<UserWorkspacePreference>();
        db.CreateTable<SaasPlan>();
        db.CreateTable<SaasPlanVersion>();
        db.CreateTable<BillingSubscription>();
        db.CreateTable<SaasAuditEvent>();
        var now = DateTime.UtcNow;
        var first = new Workspace { Id="workspace-first", Name="First", Slug="first", CreatedDate=now, ModifiedDate=now };
        var preferred = new Workspace { Id="workspace-preferred", Name="Preferred", Slug="preferred", CreatedDate=now, ModifiedDate=now };
        db.Insert(first);
        db.Insert(preferred);
        db.Insert(new SaasPlan { Id="plan.free", Code="free", Name="Free", CreatedDate=now, ModifiedDate=now });
        db.Insert(new SaasPlanVersion { Id="plan.free.v1", PlanId="plan.free", Status=PlanVersionStatus.Published, CreatedDate=now, ModifiedDate=now });
        db.Insert(new WorkspaceMember {
            Id="invitation", WorkspaceId=first.Id, UserId="invite:ada@example.com", InvitedEmail="ada@example.com",
            Role=WorkspaceMemberRole.Member, Status=WorkspaceMemberStatus.Invited, CreatedDate=now, ModifiedDate=now,
        });

        var manager = new SaasManager(new SaasConfig());
        var personal = manager.EnsurePersonalWorkspace(db, "user-1", "Ada", "ADA@example.com");
        db.Insert(new WorkspaceMember {
            WorkspaceId=preferred.Id, UserId="user-1", Role=WorkspaceMemberRole.Admin,
            Status=WorkspaceMemberStatus.Active, JoinedDate=now, CreatedDate=now, ModifiedDate=now,
        });
        db.Save(new UserWorkspacePreference {
            UserId="user-1", ActiveWorkspaceId=preferred.Id, CreatedDate=now, ModifiedDate=now,
        });
        var selected = manager.EnsurePersonalWorkspace(db, "user-1", "Ada", "ada@example.com");

        Assert.Multiple(() => {
            Assert.That(personal.Id, Is.Not.EqualTo(first.Id));
            Assert.That(db.SingleById<WorkspaceMember>("invitation")!.Status, Is.EqualTo(WorkspaceMemberStatus.Invited));
            Assert.That(db.SingleById<WorkspaceMember>("invitation")!.UserId, Is.EqualTo("invite:ada@example.com"));
            Assert.That(selected.Id, Is.EqualTo(preferred.Id));
            Assert.That(db.Count<Workspace>(), Is.EqualTo(3));
            Assert.That(db.Count<SaasAuditEvent>(x => x.Action == "invitation.accepted"), Is.Zero);
        });
    }
}

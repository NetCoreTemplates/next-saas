using System.Data;
using System.Text;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using MyApp.Data;
using MyApp.ServiceInterface;
using MyApp.ServiceModel;
using ServiceStack;
using ServiceStack.Auth;
using ServiceStack.Data;
using ServiceStack.OrmLite;

namespace MyApp;

/// <summary>
/// Creates deterministic, Development-only example records for product tours, screenshots and demos: about fifty
/// customers with a year of subscriptions, invoices, members and usage, see ExampleDataStories. This is
/// deliberately an app task instead of a migration: reference plans remain production-safe, while every local
/// database provider receives the same example data through OrmLite.
/// </summary>
public static class ExampleDataSeeder
{
    private const string SeedUserId = "example-data-seed";
    private const string MainWorkspaceId = "demo.workspace.northstar";
    // The user ApplyWebhookAsync() records Stripe events as
    private const string StripeUserId = "stripe";

    public static async Task SeedAsync(IServiceProvider services)
    {
        var environment = services.GetRequiredService<IHostEnvironment>();
        if (!environment.IsDevelopment())
            throw new InvalidOperationException("Example data can only be seeded in Development.");

        using var scope = services.GetRequiredService<IServiceScopeFactory>().CreateScope();
        var scoped = scope.ServiceProvider;
        var users = scoped.GetRequiredService<UserManager<ApplicationUser>>();
        var manager = scoped.GetRequiredService<ISaasManager>();
        var files = scoped.GetRequiredService<IFileStore>();
        var dbFactory = scoped.GetRequiredService<IDbConnectionFactory>();
        var config = scoped.GetRequiredService<SaasConfig>();

        var managerUser = await RequiredUserAsync(users, "manager@email.com");
        var employeeUser = await RequiredUserAsync(users, "employee@email.com");
        var testUser = await RequiredUserAsync(users, "test@email.com");
        var adminUser = await RequiredUserAsync(users, "admin@email.com");
        var now = DateTime.UtcNow;
        var today = now.Date;

        // Example data is written with its own audit dates, so it uses the connection without its audit rules.
        // SaasManager relies on the rules and on its connection being confined to the organization, so it's given
        // a connection opened for each organization with OpenForWorkspace().
        using var seedDb = dbFactory.OpenAcrossWorkspaces(SeedUserId);
        var db = seedDb.WithoutFilters();
        var catalog = PlanCatalog.Load(db, config.DefaultCurrency);
        var stories = ExampleDataStories.Create(
            new(managerUser.Id, managerUser.Email!), new(employeeUser.Id, employeeUser.Email!), new(testUser.Id, testUser.Email!));

        RemovePreviousSeed(db, stories);
        foreach (var story in stories)
            SeedWorkspace(db, story, catalog, config, today, now);

        SeedMainTeam(db, managerUser, employeeUser, testUser, now);
        SavePreference(db, managerUser.Id, MainWorkspaceId, now);
        SavePreference(db, employeeUser.Id, MainWorkspaceId, now);
        SavePreference(db, testUser.Id, MainWorkspaceId, now);
        SeedApiKey(db, managerUser, now);

        var storedBytes = await SeedFilesAsync(db, files, managerUser.Id, now);
        foreach (var story in stories)
        {
            SeedUsage(db, dbFactory, manager, story, catalog, config, storedBytes, today, now);
            SeedHistory(db, story, catalog, config, adminUser.Id, today);
        }
        SeedNotifications(db, managerUser, now);
        SeedAuditAndOperations(db, managerUser.Id, adminUser.Id, now);

        SeedPlatformSnapshots(db, stories, catalog, today);
        // Today's snapshot is recorded by the job that records every day's
        await scoped.GetRequiredService<BuildSaasDailySnapshotCommand>().ExecuteAsync(new NoArgs());

        var revenue = SaasRevenue.Calculate(db, config.DefaultCurrency);
        Console.WriteLine("Example data is ready.");
        Console.WriteLine($"  {stories.Count} organizations with {ExampleDataStories.HistoryDays} days of history, " +
                          $"estimated MRR {revenue.Mrr:N0} {config.DefaultCurrency.ToUpperInvariant()} from {revenue.Paying} paying customers");
        Console.WriteLine("  Customer: manager@email.com / p@55wOrd (Northstar Labs)");
        Console.WriteLine($"  Operator: admin@email.com / p@55wOrd ({stories.Count} example organizations)");
    }

    /// <summary>
    /// Seeding again replaces the example organizations' history, which is relative to the day it's run
    /// </summary>
    private static void RemovePreviousSeed(IDbConnection db, List<OrganizationStory> stories)
    {
        var ids = stories.Select(x => x.Id).ToList();
        using var tx = db.OpenTransaction();
        db.Delete<UsageEvent>(x => Sql.In(x.WorkspaceId, ids));
        db.Delete<UsageDailyRollup>(x => Sql.In(x.WorkspaceId, ids));
        db.Delete<UsageReservation>(x => Sql.In(x.WorkspaceId, ids));
        db.Delete<UsageAggregate>(x => Sql.In(x.WorkspaceId, ids));
        db.Delete<UsagePeriod>(x => Sql.In(x.WorkspaceId, ids));
        db.Delete<SaasAuditEvent>(x => Sql.In(x.WorkspaceId, ids) && x.Id.StartsWith("demo.seed."));
        db.Delete<SupportNote>(x => Sql.In(x.WorkspaceId, ids) && x.Id.StartsWith("demo.seed."));
        db.Delete<WorkspaceMember>(x => Sql.In(x.WorkspaceId, ids) && x.Id.StartsWith("demo.member."));
        tx.Commit();
    }

    private static void SeedWorkspace(IDbConnection db, OrganizationStory story, PlanCatalog catalog, SaasConfig config, DateTime today, DateTime now)
    {
        var created = today.AddDays(-story.CreatedDaysAgo).AddHours(9);
        var stripeId = story.Slug.Replace("-", "_", StringComparison.Ordinal);
        db.Save(new Workspace {
            Id = story.Id, Name = story.Name, Slug = story.Slug, Kind = story.Kind,
            Status = WorkspaceStatus.Active, BillingEmail = story.BillingEmail, StripeCustomerId = $"cus_demo_{stripeId}",
            CreatedDate = created, ModifiedDate = now.AddDays(-1), CreatedBy = SeedUserId, ModifiedBy = SeedUserId,
        });

        // Northstar's team is the screenshot team, see SeedMainTeam()
        if (story.Id != MainWorkspaceId)
        {
            for (var i = 0; i < story.Members.Count; i++)
            {
                var member = story.Members[i];
                var joined = today.AddDays(-member.JoinedDaysAgo).AddHours(10);
                db.Save(new WorkspaceMember {
                    Id = MemberId(story, i), WorkspaceId = story.Id, UserId = member.UserId, InvitedEmail = member.Email,
                    Role = member.Role, Status = WorkspaceMemberStatus.Active, JoinedDate = joined,
                    CreatedDate = joined, ModifiedDate = joined, CreatedBy = SeedUserId, ModifiedBy = SeedUserId,
                });
            }
        }

        var current = story.Current;
        var (periodStart, periodEnd) = story.BillingPeriodOn(0, today);
        // The same access the lifecycle policy gives its billing state, so evaluating it doesn't change it
        var accessMode = current.Status switch {
            SubscriptionStatus.PastDue => WorkspaceAccessMode.Grace,
            SubscriptionStatus.Canceled => Enum.TryParse<WorkspaceAccessMode>(config.AfterGraceAccessMode, true, out var mode) ? mode : WorkspaceAccessMode.FreeFallback,
            _ => WorkspaceAccessMode.Full,
        };
        db.Save(new BillingSubscription {
            Id = SubscriptionId(story), WorkspaceId = story.Id, PlanVersionId = catalog.VersionId(current.PlanCode),
            Status = current.Status, Interval = BillingInterval.Month, PeriodStart = periodStart, PeriodEnd = periodEnd,
            TrialEnd = current.Status == SubscriptionStatus.Trialing ? periodEnd : null,
            CancelAt = current.Status == SubscriptionStatus.Canceled ? today.AddDays(-current.DaysAgo) : null,
            // A grace period of 10 days from when the payment failed
            GraceEnd = current.Status == SubscriptionStatus.PastDue ? today.AddDays(10 - current.DaysAgo) : null,
            StripeSubscriptionId = story.Plans.Any(x => x.Status != SubscriptionStatus.Free) ? $"sub_demo_{stripeId}" : null,
            StripeStatus = StripeStatus(current.Status),
            AccessMode = accessMode,
            AccessReason = accessMode switch {
                WorkspaceAccessMode.Grace => "Payment is past due; access continues during the configured grace period.",
                WorkspaceAccessMode.FreeFallback => "Paid access is unavailable; Free plan entitlements apply.",
                WorkspaceAccessMode.ReadOnly => "The organization is read-only.",
                _ => null,
            },
            CreatedDate = created, ModifiedDate = today.AddDays(-current.DaysAgo).AddHours(12),
            CreatedBy = SeedUserId, ModifiedBy = SeedUserId,
        });
    }

    /// <summary>
    /// Every day's usage by each member, with the billing periods it was counted in and the daily rollups the usage
    /// rollup job would build from it, then what the organization stores now
    /// </summary>
    private static void SeedUsage(IDbConnection db, IDbConnectionFactory dbFactory, ISaasManager manager, OrganizationStory story,
        PlanCatalog catalog, SaasConfig config, long storedBytes, DateTime today, DateTime now)
    {
        var random = new Random(ExampleDataStories.StableSeed($"{story.Slug}:members"));
        var periods = new Dictionary<(string MeterKey, DateTime Start), UsagePeriod>();
        var aggregates = new Dictionary<string, UsageAggregate>();
        var events = new List<UsageEvent>();
        var warnings = new List<SaasAuditEvent>();

        UsagePeriod PeriodOf(string meterKey, int daysAgo)
        {
            var (start, end) = story.BillingPeriodOn(daysAgo, today);
            if (periods.TryGetValue((meterKey, start), out var period)) return period;
            // Its allowance is the one of the plan it ended on
            var lastDay = Math.Max(Math.Max(0, (today - end).Days + 1), (story.CanceledDaysAgo ?? -1) + 1);
            var quota = catalog.Quota(story.PlanOn(lastDay)!.PlanCode, meterKey);
            period = new UsagePeriod {
                Id = $"demo.period.{story.Slug}.{meterKey}.{start:yyyyMMdd}", WorkspaceId = story.Id, MeterKey = meterKey,
                PeriodStart = start, PeriodEnd = end, Allowance = quota?.IncludedUnits,
                Enforcement = quota?.Enforcement ?? QuotaEnforcement.HardLimit,
                Source = "plan", Kind = MeterKind.Counter, Reset = MeterReset.BillingPeriod,
                CreatedDate = start, ModifiedDate = start, CreatedBy = SeedUserId, ModifiedBy = SeedUserId,
            };
            periods[(meterKey, start)] = period;
            aggregates[period.Id] = new UsageAggregate {
                Id = $"demo.aggregate.{story.Slug}.{meterKey}.{start:yyyyMMdd}", WorkspaceId = story.Id, UsagePeriodId = period.Id,
                CreatedDate = start, ModifiedDate = start, CreatedBy = SeedUserId, ModifiedBy = SeedUserId,
            };
            return period;
        }

        void Record(string meterKey, int daysAgo, long[] unitsByMember, string channel)
        {
            var date = today.AddDays(-daysAgo);
            var period = PeriodOf(meterKey, daysAgo);
            var aggregate = aggregates[period.Id];
            for (var i = 0; i < unitsByMember.Length; i++)
            {
                if (unitsByMember[i] <= 0) continue;
                var recorded = date.AddHours(8).AddMinutes(random.Next(600));
                var used = aggregate.UsedUnits;
                aggregate.UsedUnits += unitsByMember[i];
                aggregate.PeakUnits = aggregate.UsedUnits;
                if (aggregate.LastEventDate == null || recorded > aggregate.LastEventDate)
                    aggregate.LastEventDate = aggregate.ModifiedDate = recorded;
                events.Add(new UsageEvent {
                    Id = $"demo.event.{story.Slug}.{meterKey}.{date:yyyyMMdd}.{i}", WorkspaceId = story.Id, UsagePeriodId = period.Id,
                    MeterKey = meterKey, Units = unitsByMember[i], IdempotencyKey = $"example:{meterKey}:{date:yyyyMMdd}:{i}",
                    Source = SeedUserId, EventType = "consume", MetadataJson = $$"""{"channel":"{{channel}}"}""",
                    RecordedDate = recorded, RecordedBy = story.Members[i].UserId,
                });
                // A meter's warning when it crosses 80% of its allowance, like SaasManager.RecordUsage() writes
                if (period.Allowance > 0 && used * 100 < period.Allowance * 80 && aggregate.UsedUnits * 100 >= period.Allowance * 80)
                {
                    warnings.Add(new SaasAuditEvent {
                        Id = $"demo.seed.warning.{story.Slug}.{meterKey}.{period.PeriodStart:yyyyMMdd}", WorkspaceId = story.Id,
                        Category = "usage", Action = "quota.warning", UserId = "quota-policy", SubjectId = period.Id,
                        DetailJson = new { meterKey, threshold = 80, used = aggregate.UsedUnits, allowance = period.Allowance }.ToJson(),
                        Outcome = "Succeeded", UserAgent = "Acme example data", CreatedDate = recorded,
                    });
                }
            }
        }

        var days = ExampleUsage.Simulate(story, today, catalog.Allowance);
        foreach (var day in days)
        {
            // The members who had joined by then share its usage, the most active the most
            var weights = story.Members.Select(x => x.JoinedDaysAgo >= day.DaysAgo ? x.Weight : 0).ToArray();
            var totalWeight = weights.Sum();
            var requests = weights.Select(x => (long)(day.ApiRequests * x / totalWeight)).ToArray();
            requests[0] += day.ApiRequests - requests.Sum();
            Record("api.requests", day.DaysAgo, requests, "api");

            var documents = new long[weights.Length];
            for (var i = 0; i < day.DocumentsUploaded; i++)
            {
                var pick = random.NextDouble() * totalWeight;
                var member = 0;
                while (pick > weights[member] && member < weights.Length - 1) pick -= weights[member++];
                documents[member]++;
            }
            Record("documents.uploaded", day.DaysAgo, documents, "web");
        }

        // The rollups BuildUsageRollupsCommand builds from the events, so it finds nothing to change
        var userBreakdown = config.Meters.Where(x => !x.AllowUserBreakdown).Select(x => x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        UsageDailyRollup Rollup(DateTime date, string meterKey, string dimensionType, string dimensionValue, IEnumerable<UsageEvent> rows) => new() {
            WorkspaceId = story.Id, Date = date, MeterKey = meterKey, DimensionType = dimensionType, DimensionValue = dimensionValue,
            Units = rows.Sum(x => x.Units), EventCount = rows.LongCount(),
            CreatedDate = date.AddHours(23), ModifiedDate = date.AddHours(23), CreatedBy = SeedUserId, ModifiedBy = SeedUserId,
        };
        var rollups = events.GroupBy(x => (x.RecordedDate.Date, x.MeterKey))
            .Select(x => Rollup(x.Key.Date, x.Key.MeterKey, "workspace", "all", x))
            .Concat(events.Where(x => !userBreakdown.Contains(x.MeterKey))
                .GroupBy(x => (x.RecordedDate.Date, x.MeterKey, x.RecordedBy))
                .Select(x => Rollup(x.Key.Date, x.Key.MeterKey, "user", x.Key.RecordedBy, x)))
            .ToList();

        using (var tx = db.OpenTransaction())
        {
            db.InsertAll(periods.Values);
            db.InsertAll(aggregates.Values);
            db.InsertAll(events);
            db.InsertAll(rollups);
            db.InsertAll(warnings);
            tx.Commit();
        }

        // The current period, and the meters that aren't counted, are resolved like they are for its requests
        using var workspaceDb = dbFactory.OpenForWorkspace(story.Id, SeedUserId);
        var workspace = db.SingleById<Workspace>(story.Id)!;
        var subscription = db.SingleById<BillingSubscription>(SubscriptionId(story))!;
        var usage = manager.GetUsage(workspaceDb, workspace, subscription);

        // What it stores now: most of what it uploaded, within the plan it was last paying for
        long storedDocuments = 12, stored = storedBytes;
        if (story.Id != MainWorkspaceId)
        {
            var plan = story.Plans.LastOrDefault(x => x.Status != SubscriptionStatus.Canceled)?.PlanCode ?? story.Current.PlanCode;
            storedDocuments = Math.Min(catalog.Allowance(plan, "documents.stored") ?? long.MaxValue,
                (long)(days.Sum(x => x.DocumentsUploaded) * (0.6 + random.NextDouble() * 0.3)) + random.Next(5, 40));
            stored = Math.Min(catalog.Allowance(plan, "storage.bytes") ?? long.MaxValue,
                storedDocuments * random.Next(150_000, 2_400_000));
        }
        SetAggregate(db, story.Id, usage, "documents.stored", storedDocuments, now);
        SetAggregate(db, story.Id, usage, "storage.bytes", stored, now);
    }

    private static void SetAggregate(IDbConnection db, string workspaceId, IEnumerable<UsageSummary> usage,
        string meterKey, long units, DateTime now)
    {
        var summary = usage.SingleOrDefault(x => x.MeterKey == meterKey);
        if (summary == null) return;
        var period = db.Single<UsagePeriod>(x => x.WorkspaceId == workspaceId && x.MeterKey == meterKey && x.PeriodStart == summary.PeriodStart)
            ?? throw new InvalidOperationException($"Usage period '{workspaceId}:{meterKey}' was not created.");
        var aggregate = db.Single<UsageAggregate>(x => x.UsagePeriodId == period.Id)!;
        aggregate.UsedUnits = units;
        aggregate.PeakUnits = Math.Max(aggregate.PeakUnits, units);
        aggregate.LastEventDate = now.AddHours(-2);
        aggregate.ModifiedDate = now;
        aggregate.ModifiedBy = SeedUserId;
        db.Update(aggregate);
    }

    /// <summary>
    /// The organization's audit log: when it was created, who joined it, the Stripe events for its subscription and
    /// invoices, and the changes they made to what it's billed, as ApplyWebhookAsync() records them. Northstar's
    /// other lifecycle events are the hand-written ones in SeedAuditAndOperations().
    /// </summary>
    private static void SeedHistory(IDbConnection db, OrganizationStory story, PlanCatalog catalog, SaasConfig config, string adminId, DateTime today)
    {
        var events = new List<SaasAuditEvent>();
        var subscriptionId = SubscriptionId(story);
        void Add(int daysAgo, int hour, string category, string action, string userId, string? subjectId, object? detail = null, string outcome = "Succeeded")
        {
            if (daysAgo < 0 || daysAgo > story.CreatedDaysAgo) return;
            events.Add(new SaasAuditEvent {
                Category = category, Action = action, UserId = userId, SubjectId = subjectId,
                DetailJson = detail?.ToJson(), Outcome = outcome, UserAgent = "Acme example data",
                CreatedDate = today.AddDays(-daysAgo).AddHours(hour),
            });
        }
        // Stripe events are recorded with their event's id, like ApplyWebhookAsync() records them
        void Stripe(int daysAgo, int hour, string eventType) =>
            Add(daysAgo, hour, "billing", eventType, StripeUserId, $"evt_demo_{story.Slug.Replace('-', '_')}_{events.Count + 1}");
        SubscriptionState State(PlanSegment segment) => new(catalog.VersionId(segment.PlanCode), segment.Status, BillingInterval.Month, null);

        if (!story.HasHandWrittenHistory)
        {
            Add(story.CreatedDaysAgo, 9, "workspace", "created", story.OwnerId, story.Id);
            for (var i = 1; i < story.Members.Count; i++)
            {
                var member = story.Members[i];
                Add(Math.Min(member.JoinedDaysAgo + 1, story.CreatedDaysAgo), 11, "membership", "member.invited", story.OwnerId,
                    MemberId(story, i), new { email = member.Email, role = member.Role.ToString() });
                Add(member.JoinedDaysAgo, 14, "membership", "invitation.accepted", member.UserId, MemberId(story, i));
            }
        }

        // An organization starts on the Free plan, then checks out its subscription
        var state = State(new(story.CreatedDaysAgo, "free", SubscriptionStatus.Free));
        foreach (var segment in story.Plans)
        {
            string[] stripeEvents = segment.Status switch {
                SubscriptionStatus.Free when state.Status == SubscriptionStatus.Free => [],
                // A trial that ended without converting
                SubscriptionStatus.Free or SubscriptionStatus.Canceled => ["customer.subscription.deleted"],
                SubscriptionStatus.PastDue => ["invoice.payment_failed"],
                _ when state.Status == SubscriptionStatus.Free => ["checkout.session.completed", "customer.subscription.created"],
                _ => ["customer.subscription.updated"],
            };
            if (!story.HasHandWrittenHistory)
                foreach (var eventType in stripeEvents)
                    Stripe(segment.DaysAgo, 10, eventType);
            var next = State(segment);
            if (segment.DaysAgo <= story.CreatedDaysAgo && SaasRevenue.ChangeEvent(catalog.Prices, config.DefaultCurrency, subscriptionId,
                    state, next, StripeUserId, today.AddDays(-segment.DaysAgo).AddHours(10)) is { } change)
                events.Add(change);
            // Stripe retries the payment that failed
            if (segment.Status == SubscriptionStatus.PastDue && !story.HasHandWrittenHistory)
                Stripe(segment.DaysAgo - 3, 6, "invoice.payment_failed");
            state = next;
        }

        // An invoice is paid at the start of each billing period
        for (var daysAgo = Math.Min(story.CreatedDaysAgo, ExampleDataStories.HistoryDays - 1); daysAgo >= 0; daysAgo--)
        {
            var segment = story.PlanOn(daysAgo)!;
            if (segment.Status != SubscriptionStatus.Active || catalog.MonthlyAmount(segment.PlanCode) <= 0) continue;
            if (story.BillingPeriodOn(daysAgo, today).Start != today.AddDays(-daysAgo)) continue;
            Stripe(daysAgo, 5, "invoice.paid");
        }

        for (var i = 0; i < events.Count; i++)
            events[i].Id = $"demo.seed.audit.{story.Slug}.{i + 1:0000}";
        db.InsertAll(events.Select(x => { x.WorkspaceId = story.Id; return x; }));

        if (story.SupportNote != null)
        {
            var written = today.AddDays(-Math.Min(story.CreatedDaysAgo, 4)).AddHours(15);
            db.Save(new SupportNote {
                Id = $"demo.seed.note.{story.Slug}", WorkspaceId = story.Id, Body = story.SupportNote,
                CreatedDate = written, ModifiedDate = written, CreatedBy = adminId, ModifiedBy = adminId,
            });
        }
    }

    /// <summary>
    /// The platform's daily metrics for the history, as BuildSaasDailySnapshotCommand would have recorded them
    /// </summary>
    private static void SeedPlatformSnapshots(IDbConnection db, List<OrganizationStory> stories, PlanCatalog catalog, DateTime today)
    {
        var from = today.AddDays(-(ExampleDataStories.HistoryDays - 1));
        var storyIds = stories.Select(x => x.Id).ToList();
        // Organizations that aren't examples, e.g. the ones of the development users, count from when they were created
        var others = db.Column<DateTime>(db.From<Workspace>()
            .Where(x => !Sql.In(x.Id, storyIds) && x.Status == WorkspaceStatus.Active).Select(x => x.CreatedDate));
        var files = db.Select<StoredFile>(x => x.Status == StoredFileStatus.Available);

        var rows = new List<SaasDailySnapshot>();
        for (var daysAgo = ExampleDataStories.HistoryDays - 1; daysAgo >= 1; daysAgo--)
        {
            var date = today.AddDays(-daysAgo);
            var revenue = new RevenueSummary();
            var statuses = new Dictionary<SubscriptionStatus, long>();
            var organizations = others.Count(x => x.Date <= date);
            foreach (var story in stories)
            {
                if (story.PlanOn(daysAgo) is not { } segment) continue;
                organizations++;
                statuses[segment.Status] = statuses.GetValueOrDefault(segment.Status) + 1;
                revenue.Add(segment.PlanCode, catalog.Name(segment.PlanCode), segment.Status, catalog.MonthlyAmount(segment.PlanCode));
            }
            var stored = files.Where(x => x.CreatedDate.Date <= date).ToList();
            List<(string Key, string Dimension, decimal Value)> metrics = [
                ("workspaces.active", "all", organizations),
                ("subscriptions.active", "all", statuses.GetValueOrDefault(SubscriptionStatus.Active)),
                ("subscriptions.trialing", "all", statuses.GetValueOrDefault(SubscriptionStatus.Trialing)),
                ("subscriptions.past_due", "all", statuses.GetValueOrDefault(SubscriptionStatus.PastDue)),
                ("subscriptions.canceled", "all", statuses.GetValueOrDefault(SubscriptionStatus.Canceled)),
                ("storage.files", "all", stored.Count),
                ("storage.bytes", "all", stored.Sum(x => x.ByteLength)),
                ("operations.stripe_failed", "all", 0),
            ];
            metrics.AddRange(SaasRevenue.SnapshotMetrics(revenue));
            rows.AddRange(metrics.Select(x => new SaasDailySnapshot {
                Id = $"demo.snapshot.{date:yyyyMMdd}.{x.Key}.{x.Dimension}", Date = date,
                MetricKey = x.Key, Dimension = x.Dimension, Value = x.Value,
                CreatedDate = date.AddHours(23), ModifiedDate = date.AddHours(23), CreatedBy = SeedUserId, ModifiedBy = SeedUserId,
            }));
        }

        using var tx = db.OpenTransaction();
        db.Delete<SaasDailySnapshot>(x => x.Date >= from && x.Date < today);
        db.InsertAll(rows);
        tx.Commit();
    }

    private static string SubscriptionId(OrganizationStory story) => $"demo.subscription.{story.Slug}";

    private static string MemberId(OrganizationStory story, int index) =>
        index == 0 ? $"demo.member.{story.Slug}.owner" : $"demo.member.{story.Slug}.{index}";

    private static string? StripeStatus(SubscriptionStatus status) => status switch {
        SubscriptionStatus.Trialing => "trialing",
        SubscriptionStatus.Active => "active",
        SubscriptionStatus.PastDue => "past_due",
        SubscriptionStatus.Paused => "paused",
        SubscriptionStatus.Canceled => "canceled",
        _ => null,
    };

    /// <summary>
    /// The published plans the example customers subscribe to
    /// </summary>
    private sealed class PlanCatalog
    {
        private readonly Dictionary<string, (SaasPlan Plan, string VersionId)> plans;
        private readonly Dictionary<(string PlanCode, string MeterKey), SaasPlanQuota> quotas;
        public SaasPriceCatalog Prices { get; }

        private PlanCatalog(Dictionary<string, (SaasPlan, string)> plans, Dictionary<(string, string), SaasPlanQuota> quotas, SaasPriceCatalog prices) =>
            (this.plans, this.quotas, Prices) = (plans, quotas, prices);

        public static PlanCatalog Load(IDbConnection db, string currency)
        {
            var plans = new Dictionary<string, (SaasPlan, string)>(StringComparer.OrdinalIgnoreCase);
            foreach (var plan in db.Select<SaasPlan>(x => !x.IsArchived))
            {
                var versionId = db.Select<SaasPlanVersion>(x => x.PlanId == plan.Id && x.Status == PlanVersionStatus.Published)
                    .OrderByDescending(x => x.Version).FirstOrDefault()?.Id;
                if (versionId != null) plans[plan.Code] = (plan, versionId);
            }
            var versionPlans = plans.ToDictionary(x => x.Value.Item2, x => x.Key);
            var quotas = db.Select<SaasPlanQuota>(x => Sql.In(x.PlanVersionId, versionPlans.Keys))
                .ToDictionary(x => (versionPlans[x.PlanVersionId], x.MeterKey));
            return new(plans, quotas, SaasPriceCatalog.Load(db, currency));
        }

        public string VersionId(string planCode) => plans.TryGetValue(planCode, out var plan) ? plan.VersionId
            : throw new InvalidOperationException($"Published plan '{planCode}' was not found.");
        public string Name(string planCode) => plans[planCode].Plan.Name;
        public SaasPlanQuota? Quota(string planCode, string meterKey) => quotas.GetValueOrDefault((planCode, meterKey));
        public long? Allowance(string planCode, string meterKey) => Quota(planCode, meterKey)?.IncludedUnits;
        public decimal MonthlyAmount(string planCode) => Prices.MonthlyAmount(VersionId(planCode), BillingInterval.Month);
    }

    private static async Task<ApplicationUser> RequiredUserAsync(UserManager<ApplicationUser> users, string email) =>
        await users.FindByEmailAsync(email) ?? throw new InvalidOperationException(
            $"Development user '{email}' was not found. Run the migrate task before seeding example data.");

    private static void SeedMainTeam(System.Data.IDbConnection db, ApplicationUser managerUser,
        ApplicationUser employeeUser, ApplicationUser testUser, DateTime now)
    {
        var joined = now.AddDays(-60);
        SaveMember(db, "demo.member.northstar.owner", managerUser.Id, managerUser.Email!, WorkspaceMemberRole.Owner, joined);
        SaveMember(db, "demo.member.northstar.admin", employeeUser.Id, employeeUser.Email!, WorkspaceMemberRole.Admin, joined.AddDays(7));
        SaveMember(db, "demo.member.northstar.member", testUser.Id, testUser.Email!, WorkspaceMemberRole.Member, joined.AddDays(18));
        db.Save(new WorkspaceMember {
            Id = "demo.member.northstar.invited", WorkspaceId = MainWorkspaceId,
            UserId = "invite:demo.northstar.billing", InvitedEmail = "jordan@northstar.example",
            Role = WorkspaceMemberRole.Billing, Status = WorkspaceMemberStatus.Invited,
            InvitedDate = now.AddDays(-2), InvitationSentDate = now.AddDays(-2), InvitationExpiresAt = now.AddDays(5),
            InvitationTokenHash = "demo-screenshot-invitation-not-a-real-token",
            CreatedDate = now.AddDays(-2), ModifiedDate = now.AddDays(-2), CreatedBy = managerUser.Id, ModifiedBy = managerUser.Id,
        });
        void SaveMember(System.Data.IDbConnection connection, string id, string userId, string email,
            WorkspaceMemberRole role, DateTime joinedDate) => connection.Save(new WorkspaceMember {
                Id = id, WorkspaceId = MainWorkspaceId, UserId = userId, InvitedEmail = email,
                Role = role, Status = WorkspaceMemberStatus.Active, JoinedDate = joinedDate,
                CreatedDate = joinedDate, ModifiedDate = joinedDate, CreatedBy = SeedUserId, ModifiedBy = SeedUserId,
            });
    }

    private static void SavePreference(System.Data.IDbConnection db, string userId, string workspaceId, DateTime now) =>
        db.Save(new UserWorkspacePreference {
            UserId = userId, ActiveWorkspaceId = workspaceId,
            CreatedDate = now, ModifiedDate = now, CreatedBy = SeedUserId, ModifiedBy = SeedUserId,
        });

    private static void SeedApiKey(System.Data.IDbConnection db, ApplicationUser user, DateTime now)
    {
        if (!db.TableExists<ApiKeysFeature.ApiKey>()) return;
        var existing = db.Single<ApiKeysFeature.ApiKey>(x =>
            x.UserId == user.Id && x.RefIdStr == MainWorkspaceId && x.Name == "Production ingestion");
        if (existing != null)
        {
            existing.LastUsedDate = now.AddMinutes(-18);
            db.Update(existing);
            return;
        }
        var key = $"ak_{Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant()}";
        db.Insert(new ApiKeysFeature.ApiKey {
            Key = key, VisibleKey = $"ak_***{key[^4..]}", Name = "Production ingestion",
            UserId = user.Id, UserName = user.UserName, RefIdStr = MainWorkspaceId,
            Scopes = ["usage:read", "usage:write", "workspace:read"], Environment = "live",
            Notes = "Development-only credential created by the screenshot seed.",
            CreatedDate = now.AddDays(-18), LastUsedDate = now.AddMinutes(-18),
        });
    }

    private static async Task<long> SeedFilesAsync(System.Data.IDbConnection db, IFileStore files, string userId, DateTime now)
    {
        (string Name, string Type)[] examples = [
            ("Q3-board-pack.pdf", "application/pdf"), ("customer-research-notes.md", "text/markdown"),
            ("FY26-operating-plan.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"),
            ("security-review.pdf", "application/pdf"), ("product-metrics.csv", "text/csv"),
            ("partner-agreement.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document"),
            ("brand-guidelines.pdf", "application/pdf"), ("launch-checklist.md", "text/markdown"),
            ("renewal-forecast.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"),
            ("api-export.json", "application/json"), ("team-offsite-notes.txt", "text/plain"),
            ("architecture-overview.pdf", "application/pdf"),
        ];
        long total = 0;
        for (var index = 0; index < examples.Length; index++)
        {
            var (name, type) = examples[index];
            var id = $"demo.file.{index + 1:00}";
            var workspaceFiles = files.ForWorkspace(MainWorkspaceId);
            var objectKey = workspaceFiles.NewFileKey(id);
            var heading = Encoding.UTF8.GetBytes($"Example screenshot document: {name}\nGenerated for the Next SaaS template.\n");
            var content = new byte[90_000 + index * 11_000];
            heading.CopyTo(content, 0);
            Array.Fill(content, (byte)' ', heading.Length, content.Length - heading.Length);
            await using var input = new MemoryStream(content);
            var stored = await workspaceFiles.WriteAsync(objectKey, input, 1024 * 1024);
            total += stored.ByteLength;
            db.Save(new StoredFile {
                Id = id, WorkspaceId = MainWorkspaceId, IdempotencyKey = $"screenshot-file-{index + 1:00}",
                Name = name, ObjectKey = objectKey, ContentType = type, ByteLength = stored.ByteLength,
                Sha256 = stored.Sha256, Status = StoredFileStatus.Available, UploadedBy = userId,
                CreatedDate = now.AddDays(-(index + 1) * 2).AddHours(9 + index), ModifiedDate = now.AddDays(-1),
                CreatedBy = SeedUserId, ModifiedBy = SeedUserId,
            });
        }
        return total;
    }

    private static void SeedNotifications(System.Data.IDbConnection db, ApplicationUser user, DateTime now)
    {
        var notifications = new[] {
            ("usage.warning", "API usage reached 50%", "Northstar Labs has used 50% of its monthly API request allowance.", false, 1),
            ("member.invited", "Invitation sent", "Jordan Lee was invited as a Billing member.", true, 2),
            ("file.available", "Security review is ready", "security-review.pdf is available in Documents.", true, 4),
            ("billing.renewal", "Subscription renews soon", "Your Business subscription renews in 8 days.", true, 7),
        };
        for (var i = 0; i < notifications.Length; i++)
        {
            var item = notifications[i];
            db.Save(new NotificationDelivery {
                Id = $"demo.notification.{i + 1}", WorkspaceId = MainWorkspaceId, UserId = user.Id,
                Recipient = user.Email!, TemplateKey = item.Item1, Channel = NotificationChannel.InApp,
                Subject = item.Item2, Body = item.Item3, DeduplicationKey = $"screenshot-notification-{i + 1}",
                Status = NotificationDeliveryStatus.Delivered, Attempts = 1,
                DeliveredDate = now.AddDays(-item.Item5), ReadDate = item.Item4 ? now.AddDays(-item.Item5).AddHours(2) : null,
                CreatedDate = now.AddDays(-item.Item5), ModifiedDate = now.AddDays(-item.Item5), CreatedBy = SeedUserId, ModifiedBy = SeedUserId,
            });
        }
    }

    private static void SeedAuditAndOperations(System.Data.IDbConnection db, string managerId, string adminId, DateTime now)
    {
        (string Category, string Action, string Subject, int Days)[] audit = [
            ("workspace", "created", MainWorkspaceId, 74), ("billing", "subscription.activated", "demo.subscription.northstar-labs", 61),
            ("membership", "member.invited", "demo.member.northstar.admin", 54), ("membership", "member.joined", "demo.member.northstar.admin", 53),
            ("file", "uploaded", "demo.file.01", 22), ("api-key", "created", "Production ingestion", 18),
            ("workspace", "profile.updated", MainWorkspaceId, 12), ("usage", "quota.warning", "api.requests", 1),
        ];
        for (var i = 0; i < audit.Length; i++)
        {
            var item = audit[i];
            db.Save(new SaasAuditEvent {
                Id = $"demo.audit.{i + 1:00}", WorkspaceId = MainWorkspaceId, Category = item.Category,
                Action = item.Action, UserId = i == 1 ? "stripe-webhook" : managerId, SubjectId = item.Subject,
                DetailJson = i == 7 ? "{\"meterKey\":\"api.requests\",\"threshold\":50}" : null,
                Outcome = "Succeeded", IpAddress = "203.0.113.42", UserAgent = "Acme example data",
                CreatedDate = now.AddDays(-item.Days).AddHours(10),
            });
        }

        db.Save(new SupportNote {
            Id = "demo.support-note.1", WorkspaceId = MainWorkspaceId,
            Body = "Customer is preparing for a quarterly security review. Confirm data-retention settings before renewal.",
            CreatedDate = now.AddDays(-5), ModifiedDate = now.AddDays(-5), CreatedBy = adminId, ModifiedBy = adminId,
        });
        db.Save(new WorkspaceRetentionPolicy {
            WorkspaceId = MainWorkspaceId, AnalyticsRetentionDays = 730, AuditRetentionDays = 1095,
            NotificationRetentionDays = 180, DeletedFileRetentionDays = 45, LifecycleHistoryRetentionDays = 730,
            Reason = "Extended retention for annual security reviews.",
            CreatedDate = now.AddDays(-20), ModifiedDate = now.AddDays(-5), CreatedBy = adminId, ModifiedBy = adminId,
        });
        db.Save(new StripeEventInbox {
            Id = "demo.stripe-event.failed", StripeEventId = "evt_demo_payment_failed",
            EventType = "invoice.payment_failed", PayloadJson = "{\"id\":\"evt_demo_payment_failed\",\"livemode\":false}",
            Status = StripeInboxStatus.Failed, Attempts = 3, LastError = "Example failure: customer payment method was declined.",
            ReceivedDate = now.AddHours(-9),
        });
        db.Save(new NotificationDelivery {
            Id = "demo.notification.failed", WorkspaceId = "demo.workspace.redwood", UserId = "demo.user.redwood",
            Recipient = "eli@redwood.example", TemplateKey = "billing.payment-failed", Channel = NotificationChannel.Email,
            Subject = "Action needed: payment failed", Body = "Example notification body.",
            DeduplicationKey = "screenshot-failed-notification", Status = NotificationDeliveryStatus.Failed,
            Attempts = 5, LastError = "Example SMTP rejection.", CreatedDate = now.AddHours(-8), ModifiedDate = now.AddHours(-2),
            CreatedBy = SeedUserId, ModifiedBy = SeedUserId,
        });
        db.Save(new WorkspaceLifecycleRequest {
            Id = "demo.lifecycle.export", WorkspaceId = "demo.workspace.atlas", Type = LifecycleRequestType.Export,
            Status = LifecycleRequestStatus.Failed, RequestedBy = "demo.user.atlas",
            LastError = "Example export interrupted before the archive was finalized.",
            CreatedDate = now.AddDays(-1), ModifiedDate = now.AddHours(-3), CreatedBy = SeedUserId, ModifiedBy = SeedUserId,
        });
    }
}

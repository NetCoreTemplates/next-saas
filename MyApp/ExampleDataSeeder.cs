using System.Text;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using MyApp.Data;
using MyApp.ServiceInterface;
using MyApp.ServiceModel;
using ServiceStack.Auth;
using ServiceStack.Data;
using ServiceStack.OrmLite;

namespace MyApp;

/// <summary>
/// Creates deterministic, Development-only example records for product tours and screenshots. This is deliberately
/// an app task instead of a migration: reference plans remain production-safe, while every local
/// database provider receives the same example data through OrmLite.
/// </summary>
public static class ExampleDataSeeder
{
    private const string SeedActor = "example-data-seed";
    private const string MainWorkspaceId = "demo.workspace.northstar";

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

        var managerUser = await RequiredUserAsync(users, "manager@email.com");
        var employeeUser = await RequiredUserAsync(users, "employee@email.com");
        var testUser = await RequiredUserAsync(users, "test@email.com");
        var adminUser = await RequiredUserAsync(users, "admin@email.com");
        var now = DateTime.UtcNow;

        using var db = dbFactory.Open();
        var workspaces = new[] {
            new DemoWorkspace(MainWorkspaceId, "Northstar Labs", "northstar-labs", "finance@northstar.example", "business", SubscriptionStatus.Active, 74, managerUser.Id, managerUser.Email!),
            new DemoWorkspace("demo.workspace.harbor", "Harbor & Pine", "harbor-and-pine", "accounts@harborpine.example", "pro", SubscriptionStatus.Trialing, 26, "demo.user.harbor", "maya@harborpine.example"),
            new DemoWorkspace("demo.workspace.meridian", "Meridian Health", "meridian-health", "billing@meridian.example", "enterprise", SubscriptionStatus.Active, 118, "demo.user.meridian", "samira@meridian.example"),
            new DemoWorkspace("demo.workspace.redwood", "Redwood Studio", "redwood-studio", "hello@redwood.example", "pro", SubscriptionStatus.PastDue, 43, "demo.user.redwood", "eli@redwood.example"),
            new DemoWorkspace("demo.workspace.solo", "Avery Chen", "avery-chen", "avery@example.com", "personal", SubscriptionStatus.Active, 19, "demo.user.avery", "avery@example.com", WorkspaceKind.Individual),
            new DemoWorkspace("demo.workspace.atlas", "Atlas Legal", "atlas-legal", "operations@atlaslegal.example", "business", SubscriptionStatus.Active, 91, "demo.user.atlas", "nora@atlaslegal.example"),
        };

        foreach (var demo in workspaces)
            SeedWorkspace(db, manager, demo, now);

        SeedMainTeam(db, managerUser, employeeUser, testUser, now);
        SavePreference(db, managerUser.Id, MainWorkspaceId, now);
        SavePreference(db, employeeUser.Id, MainWorkspaceId, now);
        SavePreference(db, testUser.Id, MainWorkspaceId, now);
        SeedApiKey(db, managerUser, now);

        var storedBytes = await SeedFilesAsync(db, files, managerUser.Id, now);
        SeedUsage(db, manager, workspaces, storedBytes, now);
        SeedNotifications(db, managerUser, now);
        SeedAuditAndOperations(db, managerUser.Id, adminUser.Id, now);

        Console.WriteLine("Example data is ready.");
        Console.WriteLine("  Customer: manager@email.com / p@55wOrd (Northstar Labs)");
        Console.WriteLine("  Operator: admin@email.com / p@55wOrd (six example organizations)");
    }

    private static async Task<ApplicationUser> RequiredUserAsync(UserManager<ApplicationUser> users, string email) =>
        await users.FindByEmailAsync(email) ?? throw new InvalidOperationException(
            $"Development user '{email}' was not found. Run the migrate task before seeding example data.");

    private static void SeedWorkspace(System.Data.IDbConnection db, ISaasManager manager, DemoWorkspace demo, DateTime now)
    {
        var created = now.AddDays(-demo.AgeDays);
        var workspace = new Workspace {
            Id = demo.Id, Name = demo.Name, Slug = demo.Slug, Kind = demo.Kind,
            Status = WorkspaceStatus.Active, BillingEmail = demo.BillingEmail,
            StripeCustomerId = $"cus_demo_{demo.Slug.Replace("-", "_", StringComparison.Ordinal)}",
            CreatedDate = created, ModifiedDate = now.AddDays(-1), CreatedBy = SeedActor, ModifiedBy = SeedActor,
        };
        db.Save(workspace);

        db.Save(new WorkspaceMember {
            Id = demo.Id == MainWorkspaceId ? "demo.member.northstar.owner" : $"demo.member.{demo.Slug}.owner",
            WorkspaceId = demo.Id, UserId = demo.OwnerId,
            InvitedEmail = demo.OwnerEmail, Role = WorkspaceMemberRole.Owner, Status = WorkspaceMemberStatus.Active,
            JoinedDate = created, CreatedDate = created, ModifiedDate = created,
            CreatedBy = SeedActor, ModifiedBy = SeedActor,
        });

        var plan = db.Single<SaasPlan>(x => x.Code == demo.PlanCode && !x.IsArchived);
        var versionId = plan == null ? null : db.Select<SaasPlanVersion>(x =>
                x.PlanId == plan.Id && x.Status == PlanVersionStatus.Published)
            .OrderByDescending(x => x.Version).FirstOrDefault()?.Id;
        versionId = versionId
            ?? throw new InvalidOperationException($"Published plan '{demo.PlanCode}' was not found.");
        var periodStart = now.Date.AddDays(-11);
        var accessMode = demo.Status == SubscriptionStatus.PastDue ? WorkspaceAccessMode.Grace : WorkspaceAccessMode.Full;
        db.Save(new BillingSubscription {
            Id = $"demo.subscription.{demo.Slug}", WorkspaceId = demo.Id, PlanVersionId = versionId,
            Status = demo.Status, Interval = BillingInterval.Month,
            PeriodStart = periodStart, PeriodEnd = periodStart.AddMonths(1),
            TrialEnd = demo.Status == SubscriptionStatus.Trialing ? now.AddDays(8) : null,
            GraceEnd = demo.Status == SubscriptionStatus.PastDue ? now.AddDays(4) : null,
            StripeSubscriptionId = demo.PlanCode == "free" ? null : $"sub_demo_{demo.Slug.Replace("-", "_", StringComparison.Ordinal)}",
            StripeStatus = demo.Status switch {
                SubscriptionStatus.Trialing => "trialing",
                SubscriptionStatus.PastDue => "past_due",
                _ => "active",
            },
            AccessMode = accessMode,
            AccessReason = accessMode == WorkspaceAccessMode.Grace ? "Payment is past due; access continues during the grace period." : null,
            CreatedDate = created, ModifiedDate = now.AddHours(-6), CreatedBy = SeedActor, ModifiedBy = SeedActor,
        });

        manager.GetUsage(db, workspace, db.SingleById<BillingSubscription>($"demo.subscription.{demo.Slug}")!);
    }

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
                CreatedDate = joinedDate, ModifiedDate = joinedDate, CreatedBy = SeedActor, ModifiedBy = SeedActor,
            });
    }

    private static void SavePreference(System.Data.IDbConnection db, string userId, string workspaceId, DateTime now) =>
        db.Save(new UserWorkspacePreference {
            UserId = userId, ActiveWorkspaceId = workspaceId,
            CreatedDate = now, ModifiedDate = now, CreatedBy = SeedActor, ModifiedBy = SeedActor,
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
            var objectKey = $"workspaces/{MainWorkspaceId}/files/{id}";
            var heading = Encoding.UTF8.GetBytes($"Example screenshot document: {name}\nGenerated for the Next SaaS template.\n");
            var content = new byte[90_000 + index * 11_000];
            heading.CopyTo(content, 0);
            Array.Fill(content, (byte)' ', heading.Length, content.Length - heading.Length);
            await using var input = new MemoryStream(content);
            var stored = await files.WriteAsync(objectKey, input, 1024 * 1024);
            total += stored.ByteLength;
            db.Save(new StoredFile {
                Id = id, WorkspaceId = MainWorkspaceId, IdempotencyKey = $"screenshot-file-{index + 1:00}",
                Name = name, ObjectKey = objectKey, ContentType = type, ByteLength = stored.ByteLength,
                Sha256 = stored.Sha256, Status = StoredFileStatus.Available, UploadedBy = userId,
                CreatedDate = now.AddDays(-(index + 1) * 2).AddHours(9 + index), ModifiedDate = now.AddDays(-1),
                CreatedBy = SeedActor, ModifiedBy = SeedActor,
            });
        }
        return total;
    }

    private static void SeedUsage(System.Data.IDbConnection db, ISaasManager manager,
        IReadOnlyCollection<DemoWorkspace> demos, long storedBytes, DateTime now)
    {
        foreach (var demo in demos)
        {
            db.Delete<UsageDailyRollup>(x => x.WorkspaceId == demo.Id);
            db.Delete<UsageEvent>(x => x.WorkspaceId == demo.Id && x.Source == SeedActor);
            var workspace = db.SingleById<Workspace>(demo.Id)!;
            var subscription = db.SingleById<BillingSubscription>($"demo.subscription.{demo.Slug}")!;
            var usage = manager.GetUsage(db, workspace, subscription);
            var apiAllowance = usage.Single(x => x.MeterKey == "api.requests").Allowance ?? 100_000;
            var apiUnits = demo.Slug switch {
                "redwood-studio" => apiAllowance * 87 / 100,
                "harbor-and-pine" => apiAllowance * 61 / 100,
                "northstar-labs" => apiAllowance * 54 / 100,
                _ => apiAllowance * 20 / 100,
            };
            SetAggregate(db, demo.Id, usage, "api.requests", apiUnits, now);
            SetAggregate(db, demo.Id, usage, "documents.uploaded", demo.Id == MainWorkspaceId ? 138 : Math.Max(12, demo.AgeDays), now);
            if (demo.Id == MainWorkspaceId)
            {
                SetAggregate(db, demo.Id, usage, "documents.stored", 12, now);
                SetAggregate(db, demo.Id, usage, "storage.bytes", storedBytes, now);
                SeedDailySeries(db, usage, "api.requests", apiUnits, now);
                SeedDailySeries(db, usage, "documents.uploaded", 138, now);
            }
        }
    }

    private static void SetAggregate(System.Data.IDbConnection db, string workspaceId, IEnumerable<UsageSummary> usage,
        string meterKey, long units, DateTime now)
    {
        var summary = usage.Single(x => x.MeterKey == meterKey);
        var period = db.Single<UsagePeriod>(x => x.WorkspaceId == workspaceId && x.MeterKey == meterKey && x.PeriodStart == summary.PeriodStart)
            ?? throw new InvalidOperationException($"Usage period '{workspaceId}:{meterKey}' was not created.");
        var aggregate = db.Single<UsageAggregate>(x => x.UsagePeriodId == period.Id)!;
        aggregate.UsedUnits = units;
        aggregate.PeakUnits = Math.Max(aggregate.PeakUnits, units);
        aggregate.LastEventDate = now.AddHours(-2);
        aggregate.ModifiedDate = now;
        aggregate.ModifiedBy = SeedActor;
        db.Update(aggregate);
    }

    private static void SeedDailySeries(System.Data.IDbConnection db, IEnumerable<UsageSummary> usage,
        string meterKey, long total, DateTime now)
    {
        var summary = usage.Single(x => x.MeterKey == meterKey);
        var period = db.Single<UsagePeriod>(x => x.WorkspaceId == MainWorkspaceId && x.MeterKey == meterKey && x.PeriodStart == summary.PeriodStart)!;
        long allocated = 0;
        for (var day = 27; day >= 0; day--)
        {
            var date = now.Date.AddDays(-day);
            var remainingDays = day + 1;
            var units = day == 0 ? total - allocated : Math.Max(1, (total - allocated) / remainingDays + ((day % 5) - 2) * Math.Max(1, total / 500));
            units = Math.Min(units, total - allocated);
            allocated += units;
            var suffix = date.ToString("yyyyMMdd");
            db.Save(new UsageDailyRollup {
                Id = $"demo.rollup.{meterKey}.{suffix}", WorkspaceId = MainWorkspaceId, Date = date,
                MeterKey = meterKey, Units = units, EventCount = Math.Max(1, units / Math.Max(1, total / 90)),
                DimensionType = "workspace", DimensionValue = "all",
                CreatedDate = date.AddHours(23), ModifiedDate = date.AddHours(23), CreatedBy = SeedActor, ModifiedBy = SeedActor,
            });
            db.Save(new UsageEvent {
                Id = $"demo.event.{meterKey}.{suffix}", WorkspaceId = MainWorkspaceId, UsagePeriodId = period.Id,
                MeterKey = meterKey, Units = units, IdempotencyKey = $"screenshot:{meterKey}:{suffix}",
                Source = SeedActor, EventType = "consume", MetadataJson = "{\"channel\":\"web\"}",
                RecordedDate = date.AddHours(10 + day % 8), RecordedBy = SeedActor,
            });
        }
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
                CreatedDate = now.AddDays(-item.Item5), ModifiedDate = now.AddDays(-item.Item5), CreatedBy = SeedActor, ModifiedBy = SeedActor,
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
                Action = item.Action, ActorId = i == 1 ? "stripe-webhook" : managerId, SubjectId = item.Subject,
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
            CreatedBy = SeedActor, ModifiedBy = SeedActor,
        });
        db.Save(new WorkspaceLifecycleRequest {
            Id = "demo.lifecycle.export", WorkspaceId = "demo.workspace.atlas", Type = LifecycleRequestType.Export,
            Status = LifecycleRequestStatus.Failed, RequestedBy = "demo.user.atlas",
            LastError = "Example export interrupted before the archive was finalized.",
            CreatedDate = now.AddDays(-1), ModifiedDate = now.AddHours(-3), CreatedBy = SeedActor, ModifiedBy = SeedActor,
        });
    }

    private sealed record DemoWorkspace(string Id, string Name, string Slug, string BillingEmail,
        string PlanCode, SubscriptionStatus Status, int AgeDays, string OwnerId, string OwnerEmail,
        WorkspaceKind Kind = WorkspaceKind.Business);
}

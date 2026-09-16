using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http;
using MyApp.ServiceInterface;
using MyApp.ServiceModel;
using NUnit.Framework;
using ServiceStack;
using ServiceStack.Auth;
using ServiceStack.OrmLite;

namespace MyApp.Tests;

public class SaasSecurityTests
{
    [Test]
    public void Production_readiness_rejects_unsafe_template_defaults()
    {
        var values = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["AllowedHosts"] = "*", ["Database:Provider"] = "Sqlite", ["Database:AutoMigrateEmpty"] = "true",
        }).Build();
        var result = ProductionReadiness.Evaluate(values, new DeploymentConfig(),
            new AppConfig { BaseUrl = "http://localhost:5001" }, new ProductConfig(),
            new NotificationConfig(), new StripeConfig());

        Assert.Multiple(() => {
            Assert.That(result.IsReady, Is.False);
            Assert.That(result.Errors, Has.Some.Contains("HTTPS"));
            Assert.That(result.Errors, Has.Some.Contains("AllowedHosts"));
            Assert.That(result.Errors, Has.Some.Contains("networked database server"));
            Assert.That(result.Errors, Has.Some.Contains("Smtp"));
            Assert.That(result.Errors, Has.Some.Contains("Stripe"));
        });
    }

    [Test]
    public void Production_readiness_accepts_an_explicit_deployment_configuration()
    {
        var values = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["AllowedHosts"] = "saas.acme.test", ["Database:Provider"] = "PostgreSql",
            ["Database:AutoMigrateEmpty"] = "false",
            ["ConnectionStrings:DefaultConnection"] = "Host=db;Database=acme;Username=acme;Password=secret",
        }).Build();
        var result = ProductionReadiness.Evaluate(values, new DeploymentConfig(),
            new AppConfig { BaseUrl = "https://saas.acme.test" }, new ProductConfig { SupportEmail = "support@acme.test" },
            new NotificationConfig { Provider = EmailProvider.Smtp },
            new StripeConfig { PublishableKey = "pk_live_example", SecretKey = "sk_live_example", WebhookSecret = "whsec_example" });

        Assert.That(result.IsReady, Is.True, string.Join(Environment.NewLine, result.Errors));
    }

    [Test]
    public void Retention_policy_uses_customer_overrides_and_legal_holds()
    {
        var now = new DateTime(2026, 9, 14, 0, 0, 0, DateTimeKind.Utc);
        var inherited = new WorkspaceRetentionPolicy();
        var extended = new WorkspaceRetentionPolicy { AuditRetentionDays = 730 };
        var held = new WorkspaceRetentionPolicy { AuditRetentionDays = 1, LegalHold = true };

        Assert.Multiple(() => {
            Assert.That(DataRetentionPolicy.EffectiveDays(inherited, x => x.AuditRetentionDays, 365), Is.EqualTo(365));
            Assert.That(DataRetentionPolicy.EffectiveDays(extended, x => x.AuditRetentionDays, 365), Is.EqualTo(730));
            Assert.That(DataRetentionPolicy.IsExpired(inherited, now, now.AddDays(-366), x => x.AuditRetentionDays, 365), Is.True);
            Assert.That(DataRetentionPolicy.IsExpired(extended, now, now.AddDays(-366), x => x.AuditRetentionDays, 365), Is.False);
            Assert.That(DataRetentionPolicy.IsExpired(held, now, now.AddYears(-10), x => x.AuditRetentionDays, 365), Is.False);
        });
    }

    [Test]
    public void Invitation_tokens_are_random_and_stored_as_one_way_hashes()
    {
        var first = WorkspaceInvitationTokens.Create();
        var second = WorkspaceInvitationTokens.Create();

        Assert.Multiple(() => {
            Assert.That(first, Has.Length.EqualTo(48));
            Assert.That(second, Is.Not.EqualTo(first));
            Assert.That(WorkspaceInvitationTokens.Hash(first), Is.Not.EqualTo(first));
            Assert.That(WorkspaceInvitationTokens.Hash(first), Is.EqualTo(WorkspaceInvitationTokens.Hash(first)));
        });
    }

    [Test]
    public void Invitations_require_pending_state_matching_email_and_unexpired_token()
    {
        var now = DateTime.UtcNow;
        var invitation = new WorkspaceMember {
            Status = WorkspaceMemberStatus.Invited,
            InvitedEmail = "invited@example.com",
            InvitationExpiresAt = now.AddDays(1),
        };

        Assert.DoesNotThrow(() => WorkspaceInvitationTokens.AssertCanAccept(invitation, "INVITED@example.com", now));
        Assert.That(Assert.Throws<HttpError>(() => WorkspaceInvitationTokens.AssertCanAccept(invitation, "other@example.com", now))!.ErrorCode,
            Is.EqualTo("InvitationEmailMismatch"));

        invitation.InvitationExpiresAt = now;
        Assert.That(Assert.Throws<HttpError>(() => WorkspaceInvitationTokens.AssertCanAccept(invitation, "invited@example.com", now))!.ErrorCode,
            Is.EqualTo("InvitationExpired"));

        invitation.InvitationExpiresAt = now.AddDays(1);
        invitation.InvitationRevokedDate = now;
        Assert.That(Assert.Throws<HttpError>(() => WorkspaceInvitationTokens.AssertCanAccept(invitation, "invited@example.com", now))!.ErrorCode,
            Is.EqualTo("InvitationNotPending"));
    }

    [TestCase("/team/invite?token=abc", "/team/invite?token=abc")]
    [TestCase("/dashboard", "/dashboard")]
    [TestCase("https://evil.example/phish", null)]
    [TestCase("//evil.example/phish", null)]
    [TestCase("", null)]
    public void Registration_only_preserves_local_return_urls(string returnUrl, string? expected)
    {
        Assert.That(MyApp.ServiceInterface.RegisterService.SafeReturnUrl(returnUrl), Is.EqualTo(expected));
    }

    [Test]
    public void Api_rate_limits_are_isolated_by_organization_and_credential()
    {
        var limiter = new MyApp.SaasApiRateLimiter(new SaasConfig { ApiKeyRequestsPerMinute = 2 });
        var now = DateTime.UtcNow;

        Assert.Multiple(() => {
            Assert.That(limiter.TryAcquire("org-a", "ak-one", now, out _), Is.True);
            Assert.That(limiter.TryAcquire("org-a", "ak-one", now, out _), Is.True);
            Assert.That(limiter.TryAcquire("org-a", "ak-one", now, out var retryAfter), Is.False);
            Assert.That(retryAfter, Is.GreaterThan(0));
            Assert.That(limiter.TryAcquire("org-b", "ak-one", now, out _), Is.True);
            Assert.That(limiter.TryAcquire("org-a", "ak-two", now, out _), Is.True);
            Assert.That(limiter.TryAcquire("org-a", "ak-one", now.AddMinutes(1), out _), Is.True);
        });
    }

    [TestCase(typeof(RecordUsage), "api.access")]
    [TestCase(typeof(UploadStoredFile), "files.basic")]
    [TestCase(typeof(GetUsageAnalytics), "analytics.basic")]
    [TestCase(typeof(QueryWorkspaceAuditEvents), "audit.read")]
    public void Product_apis_declare_server_side_feature_requirements(Type requestType, string featureKey)
    {
        var requirement = requestType.GetCustomAttributes(typeof(RequiresFeatureAttribute), true)
            .Cast<RequiresFeatureAttribute>().Single();
        Assert.That(requirement.FeatureKey, Is.EqualTo(featureKey));
    }

    [Test]
    public void Creating_an_organization_provisions_an_isolated_free_tenant_and_selects_it()
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
        db.Insert(new SaasPlan { Id = "plan.free", Code = "free", Name = "Free", CreatedDate = now, ModifiedDate = now });
        db.Insert(new SaasPlanVersion { Id = "plan.free.v1", PlanId = "plan.free", Version = 1, Status = PlanVersionStatus.Published, CreatedDate = now, ModifiedDate = now });

        var workspace = new SaasManager(new SaasConfig()).CreateOrganization(db, "user-1", "Example, Inc.", "billing@example.com");

        var member = db.Single<WorkspaceMember>(x => x.WorkspaceId == workspace.Id);
        var subscription = db.Single<BillingSubscription>(x => x.WorkspaceId == workspace.Id);
        Assert.Multiple(() => {
            Assert.That(workspace.Name, Is.EqualTo("Example, Inc."));
            Assert.That(workspace.Slug, Is.EqualTo("example-inc"));
            Assert.That(member.UserId, Is.EqualTo("user-1"));
            Assert.That(member.Role, Is.EqualTo(WorkspaceMemberRole.Owner));
            Assert.That(subscription.Status, Is.EqualTo(SubscriptionStatus.Free));
            Assert.That(subscription.PlanVersionId, Is.EqualTo("plan.free.v1"));
            Assert.That(db.SingleById<UserWorkspacePreference>("user-1")!.ActiveWorkspaceId, Is.EqualTo(workspace.Id));
        });
    }

    [Test]
    public void Api_key_is_bound_to_its_organization_not_the_users_active_preference()
    {
        var factory = new OrmLiteConnectionFactory(":memory:", SqliteDialect.Provider);
        using var db = factory.Open();
        db.CreateTable<Workspace>();
        db.CreateTable<WorkspaceMember>();
        db.CreateTable<UserWorkspacePreference>();
        db.CreateTable<ApiKeysFeature.ApiKey>();

        var now = DateTime.UtcNow;
        db.Insert(new Workspace { Id = "organization-a", Name = "A", Slug = "a", CreatedDate = now, ModifiedDate = now });
        db.Insert(new Workspace { Id = "organization-b", Name = "B", Slug = "b", CreatedDate = now, ModifiedDate = now });
        db.Insert(new WorkspaceMember { Id = "member-a", WorkspaceId = "organization-a", UserId = "user-1", Role = WorkspaceMemberRole.Member, Status = WorkspaceMemberStatus.Active, CreatedDate = now, ModifiedDate = now });
        db.Insert(new WorkspaceMember { Id = "member-b", WorkspaceId = "organization-b", UserId = "user-1", Role = WorkspaceMemberRole.Owner, Status = WorkspaceMemberStatus.Active, CreatedDate = now, ModifiedDate = now });
        db.Insert(new UserWorkspacePreference { UserId = "user-1", ActiveWorkspaceId = "organization-b", CreatedDate = now, ModifiedDate = now });
        db.Insert(new ApiKeysFeature.ApiKey { Key = "ak-organization-a", UserId = "user-1", RefIdStr = "organization-a", CreatedDate = now });

        var resolver = new WorkspaceContextResolver(new SaasManager(new SaasConfig()));
        var context = resolver.ResolveApiKey(db, new AuthUserSession { UserAuthId = "user-1" }, "ak-organization-a");

        Assert.Multiple(() => {
            Assert.That(context.Workspace.Id, Is.EqualTo("organization-a"));
            Assert.That(context.Member.Id, Is.EqualTo("member-a"));
            Assert.That(db.SingleById<UserWorkspacePreference>("user-1")!.ActiveWorkspaceId, Is.EqualTo("organization-b"));
        });
    }

    [Test]
    public void Api_key_requires_an_active_membership_and_an_explicit_organization()
    {
        var factory = new OrmLiteConnectionFactory(":memory:", SqliteDialect.Provider);
        using var db = factory.Open();
        db.CreateTable<Workspace>();
        db.CreateTable<WorkspaceMember>();
        db.CreateTable<ApiKeysFeature.ApiKey>();
        var now = DateTime.UtcNow;
        db.Insert(new Workspace { Id = "organization-a", Name = "A", Slug = "a", CreatedDate = now, ModifiedDate = now });
        db.Insert(new ApiKeysFeature.ApiKey { Key = "ak-legacy", UserId = "user-1", CreatedDate = now });
        db.Insert(new ApiKeysFeature.ApiKey { Key = "ak-orphaned", UserId = "user-1", RefIdStr = "organization-a", CreatedDate = now });

        var resolver = new WorkspaceContextResolver(new SaasManager(new SaasConfig()));
        var session = new AuthUserSession { UserAuthId = "user-1" };

        Assert.Multiple(() => {
            Assert.That(Assert.Throws<HttpError>(() => resolver.ResolveApiKey(db, session, "ak-legacy"))!.ErrorCode,
                Is.EqualTo("OrganizationKeyRequired"));
            Assert.That(Assert.Throws<HttpError>(() => resolver.ResolveApiKey(db, session, "ak-orphaned"))!.ErrorCode,
                Is.EqualTo("WorkspaceAccessDenied"));
        });
    }

    [TestCase(WorkspaceMemberRole.Owner, true)]
    [TestCase(WorkspaceMemberRole.Admin, true)]
    [TestCase(WorkspaceMemberRole.Billing, true)]
    [TestCase(WorkspaceMemberRole.Member, false)]
    public void Billing_policy_has_an_explicit_role_matrix(WorkspaceMemberRole role, bool allowed)
    {
        var context = Context(role);
        if (allowed)
            Assert.DoesNotThrow(() => WorkspaceAuthorization.RequireBilling(context));
        else
            Assert.That(Assert.Throws<HttpError>(() => WorkspaceAuthorization.RequireBilling(context))!.ErrorCode,
                Is.EqualTo("BillingRoleRequired"));
    }

    [TestCase(WorkspaceMemberRole.Owner, true)]
    [TestCase(WorkspaceMemberRole.Admin, true)]
    [TestCase(WorkspaceMemberRole.Billing, false)]
    [TestCase(WorkspaceMemberRole.Member, false)]
    public void Organization_admin_policy_has_an_explicit_role_matrix(WorkspaceMemberRole role, bool allowed)
    {
        var context = Context(role);
        if (allowed)
            Assert.DoesNotThrow(() => WorkspaceAuthorization.RequireAdmin(context));
        else
            Assert.That(Assert.Throws<HttpError>(() => WorkspaceAuthorization.RequireAdmin(context))!.ErrorCode,
                Is.EqualTo("WorkspaceAdminRequired"));
    }

    [TestCase("Admin", PlatformCapability.ViewCustomers, true)]
    [TestCase("Admin", PlatformCapability.ManageBilling, true)]
    [TestCase("Admin", PlatformCapability.ManageSupport, true)]
    [TestCase("Admin", PlatformCapability.ManagePlatform, true)]
    [TestCase("Admin", PlatformCapability.ApproveSupportAccess, true)]
    [TestCase("BillingAdmin", PlatformCapability.ViewCustomers, true)]
    [TestCase("BillingAdmin", PlatformCapability.ManageBilling, true)]
    [TestCase("BillingAdmin", PlatformCapability.ManageSupport, false)]
    [TestCase("BillingAdmin", PlatformCapability.ManagePlatform, false)]
    [TestCase("Support", PlatformCapability.ViewCustomers, true)]
    [TestCase("Support", PlatformCapability.ManageSupport, true)]
    [TestCase("Support", PlatformCapability.ManageBilling, false)]
    [TestCase("Support", PlatformCapability.ApproveSupportAccess, false)]
    [TestCase("", PlatformCapability.ViewCustomers, false)]
    public void Platform_policy_has_an_explicit_role_matrix(string role, PlatformCapability capability, bool allowed)
    {
        var session = new AuthUserSession { UserAuthId = "operator-1", Roles = role.Length == 0 ? [] : [role] };
        if (allowed)
            Assert.DoesNotThrow(() => PlatformAuthorization.Require(session, capability));
        else
            Assert.That(Assert.Throws<HttpError>(() => PlatformAuthorization.Require(session, capability))!.ErrorCode,
                Is.EqualTo("PlatformCapabilityRequired"));
    }

    [Test]
    public void Support_access_lifecycle_is_explicit_and_auditable()
    {
        var now = DateTime.UtcNow;
        var grant = new SupportAccessGrant {
            WorkspaceId = "organization-1", OperatorId = "support-1", StartsAt = now,
            ExpiresAt = now.AddMinutes(30), Reason = "Investigate customer issue",
        };

        Assert.Multiple(() => {
            Assert.That(grant.AccessStartedAt, Is.Null);
            Assert.That(grant.AccessEndedAt, Is.Null);
            Assert.That(SaasAudit.IsRegistered("support", "access.started"), Is.True);
            Assert.That(SaasAudit.IsRegistered("support", "access.ended"), Is.True);
            Assert.That(SaasAudit.IsRegistered("support", "access.denied"), Is.True);
        });
    }

    [Test]
    public void Support_access_is_bound_to_operator_tenant_lifecycle_and_expiry()
    {
        var now = DateTime.UtcNow;
        var grant = new SupportAccessGrant {
            WorkspaceId = "organization-1", OperatorId = "support-1", StartsAt = now.AddMinutes(-1),
            ExpiresAt = now.AddMinutes(30), AccessStartedAt = now, Reason = "Investigate customer issue",
        };

        Assert.Multiple(() => {
            Assert.That(SupportAccessPolicy.IsUsable(grant, "organization-1", "support-1", now), Is.True);
            Assert.That(SupportAccessPolicy.IsUsable(grant, "organization-2", "support-1", now), Is.False);
            Assert.That(SupportAccessPolicy.IsUsable(grant, "organization-1", "support-2", now), Is.False);
            Assert.That(SupportAccessPolicy.IsUsable(grant, "organization-1", "support-1", grant.ExpiresAt), Is.False);
        });

        grant.RevokedAt = now;
        Assert.That(SupportAccessPolicy.IsUsable(grant, "organization-1", "support-1", now), Is.False);
        grant.RevokedAt = null;
        grant.AccessEndedAt = now;
        Assert.That(SupportAccessPolicy.IsUsable(grant, "organization-1", "support-1", now), Is.False);
    }

    [Test]
    public void Personal_account_deletion_is_blocked_while_user_owns_an_organization()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"next-saas-account-owner-{Guid.NewGuid():N}.db");
        try
        {
            var factory = new OrmLiteConnectionFactory($"Data Source={dbPath}", SqliteDialect.Provider);
            using (var db = factory.Open())
            {
                db.CreateTable<Workspace>();
                db.CreateTable<WorkspaceMember>();
                var now = DateTime.UtcNow;
                db.Insert(new Workspace { Id = "organization-1", Name = "Owned Co", Slug = "owned-co", CreatedDate = now, ModifiedDate = now });
                db.Insert(new WorkspaceMember { Id = "member-1", WorkspaceId = "organization-1", UserId = "user-1", Role = WorkspaceMemberRole.Owner, Status = WorkspaceMemberStatus.Active, CreatedDate = now, ModifiedDate = now });
            }

            var manager = new AccountDeletionManager(factory);
            Assert.That(manager.GetOwnedOrganizationNames("user-1"), Is.EqualTo(new[] { "Owned Co" }));
            Assert.That(Assert.Throws<InvalidOperationException>(() => manager.RemoveSaasAccess("user-1"))!.Message,
                Does.Contain("Owned Co"));
        }
        finally
        {
            File.Delete(dbPath);
        }
    }

    [Test]
    public void Personal_account_deletion_removes_memberships_credentials_and_personal_notifications()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"next-saas-account-cleanup-{Guid.NewGuid():N}.db");
        try
        {
            var factory = new OrmLiteConnectionFactory($"Data Source={dbPath}", SqliteDialect.Provider);
            using (var db = factory.Open())
            {
                db.CreateTable<Workspace>();
                db.CreateTable<WorkspaceMember>();
                db.CreateTable<UserWorkspacePreference>();
                db.CreateTable<ApiKeysFeature.ApiKey>();
                db.CreateTable<NotificationPreference>();
                db.CreateTable<NotificationDelivery>();
                db.CreateTable<SupportAccessGrant>();
                db.CreateTable<SaasAuditEvent>();
                var now = DateTime.UtcNow;
                db.Insert(new Workspace { Id = "organization-1", Name = "Member Co", Slug = "member-co", CreatedDate = now, ModifiedDate = now });
                db.Insert(new WorkspaceMember { Id = "member-1", WorkspaceId = "organization-1", UserId = "user-1", Role = WorkspaceMemberRole.Member, Status = WorkspaceMemberStatus.Active, CreatedDate = now, ModifiedDate = now });
                db.Insert(new UserWorkspacePreference { UserId = "user-1", ActiveWorkspaceId = "organization-1", CreatedDate = now, ModifiedDate = now });
                db.Insert(new ApiKeysFeature.ApiKey { Key = "ak-delete-me", UserId = "user-1", RefIdStr = "organization-1", CreatedDate = now });
                db.Insert(new NotificationPreference { UserId = "user-1", WorkspaceId = "organization-1", TemplateKey = "*", CreatedDate = now, ModifiedDate = now });
                db.Insert(new NotificationDelivery { UserId = "user-1", WorkspaceId = "organization-1", DeduplicationKey = "delete-me", CreatedDate = now, ModifiedDate = now });
                db.Insert(new SupportAccessGrant { Id = "grant-1", WorkspaceId = "organization-1", OperatorId = "user-1", StartsAt = now, ExpiresAt = now.AddHours(1), CreatedDate = now, ModifiedDate = now });
            }

            new AccountDeletionManager(factory).RemoveSaasAccess("user-1");

            using var verify = factory.Open();
            var grant = verify.SingleById<SupportAccessGrant>("grant-1");
            Assert.Multiple(() => {
                Assert.That(verify.Count<WorkspaceMember>(x => x.UserId == "user-1"), Is.Zero);
                Assert.That(verify.Count<ApiKeysFeature.ApiKey>(x => x.UserId == "user-1"), Is.Zero);
                Assert.That(verify.Count<NotificationPreference>(x => x.UserId == "user-1"), Is.Zero);
                Assert.That(verify.Count<NotificationDelivery>(x => x.UserId == "user-1"), Is.Zero);
                Assert.That(verify.SingleById<UserWorkspacePreference>("user-1"), Is.Null);
                Assert.That(grant.RevokedAt, Is.Not.Null);
                Assert.That(grant.AccessEndedAt, Is.Not.Null);
                Assert.That(verify.Count<SaasAuditEvent>(), Is.EqualTo(3));
            });
        }
        finally
        {
            File.Delete(dbPath);
        }
    }

    [Test]
    public void Cookie_authenticated_saas_mutations_require_same_origin()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/saas/workspace";
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("saas.acme.test");
        context.Request.Headers.Cookie = ".AspNetCore.Identity.Application=session";

        Assert.That(MyApp.WebSecurityPolicy.RequiresSameOriginValidation(context.Request), Is.True);
        Assert.That(MyApp.WebSecurityPolicy.IsSameOrigin(context.Request, "https://saas.acme.test"), Is.False);

        context.Request.Headers.Origin = "https://evil.example";
        Assert.That(MyApp.WebSecurityPolicy.IsSameOrigin(context.Request, "https://saas.acme.test"), Is.False);

        context.Request.Headers.Origin = "https://saas.acme.test";
        Assert.That(MyApp.WebSecurityPolicy.IsSameOrigin(context.Request, "https://saas.acme.test"), Is.True);

        context.Request.Headers.Origin = "";
        context.Request.Headers["X-Api-Key"] = "ak-service";
        Assert.That(MyApp.WebSecurityPolicy.RequiresSameOriginValidation(context.Request), Is.False);
    }

    [Test]
    public void Authentication_rate_limit_targets_sensitive_post_routes()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/Identity/Account/Login";
        Assert.That(MyApp.WebSecurityPolicy.IsSensitiveAuthenticationRequest(context.Request), Is.True);

        context.Request.Method = "GET";
        Assert.That(MyApp.WebSecurityPolicy.IsSensitiveAuthenticationRequest(context.Request), Is.False);

        context.Request.Method = "POST";
        context.Request.Path = "/saas/files";
        Assert.That(MyApp.WebSecurityPolicy.IsSensitiveAuthenticationRequest(context.Request), Is.False);
    }

    [Test]
    public void Operator_tooling_paths_receive_eval_only_where_the_built_in_UIs_need_it()
    {
        var config = new SecurityConfig();

        foreach (var path in new[] { "/admin-ui", "/admin-ui/database", "/ui", "/ui/MyRequest", "/metadata" })
            Assert.That(MyApp.WebSecurityPolicy.IsToolingPath(path, config.ToolingPaths), Is.True, path);

        // Customer-facing routes, and near-misses that must not match by prefix alone.
        foreach (var path in new[] { "/", "/pricing", "/admin", "/admin/plans", "/admin-uix", "/uixyz", "/saas/files" })
            Assert.That(MyApp.WebSecurityPolicy.IsToolingPath(path, config.ToolingPaths), Is.False, path);
    }

    [Test]
    public void Tooling_policy_adds_eval_to_script_src_and_leaves_other_directives_alone()
    {
        var relaxed = MyApp.WebSecurityPolicy.WithUnsafeEval(new SecurityConfig().ContentSecurityPolicy);

        Assert.Multiple(() => {
            Assert.That(relaxed, Does.Contain("script-src 'self' 'unsafe-inline' 'unsafe-eval'"));
            // Everything that is not script-src must survive unchanged.
            Assert.That(relaxed, Does.Contain("default-src 'self'"));
            Assert.That(relaxed, Does.Contain("object-src 'none'"));
            Assert.That(relaxed, Does.Contain("frame-ancestors 'none'"));
            Assert.That(relaxed, Does.Contain("style-src 'self' 'unsafe-inline'"));
            Assert.That(relaxed, Does.Not.Contain("style-src 'self' 'unsafe-inline' 'unsafe-eval'"));
            Assert.That(relaxed, Does.Contain("connect-src 'self'"));
            // The customer-facing policy itself must never gain eval.
            Assert.That(new SecurityConfig().ContentSecurityPolicy, Does.Not.Contain("unsafe-eval"));
        });
    }

    [Test]
    public void Tooling_policy_derivation_is_idempotent_and_handles_a_missing_script_src()
    {
        var once = MyApp.WebSecurityPolicy.WithUnsafeEval("default-src 'self'; script-src 'self'");
        Assert.That(once, Is.EqualTo("default-src 'self'; script-src 'self' 'unsafe-eval'"));
        Assert.That(MyApp.WebSecurityPolicy.WithUnsafeEval(once), Is.EqualTo(once));

        // Without a script-src, default-src would otherwise still block eval.
        Assert.That(MyApp.WebSecurityPolicy.WithUnsafeEval("default-src 'self'"),
            Is.EqualTo("default-src 'self'; script-src 'self' 'unsafe-eval'"));
    }

    private static WorkspaceContext Context(WorkspaceMemberRole role) => new(
        new Workspace { Id = "organization-1", Name = "Acme", Slug = "acme" },
        new WorkspaceMember { Id = "member-1", WorkspaceId = "organization-1", UserId = "user-1", Role = role, Status = WorkspaceMemberStatus.Active },
        "user-1");
}

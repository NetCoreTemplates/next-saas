using MyApp.ServiceInterface;
using MyApp.ServiceModel;
using NUnit.Framework;
using ServiceStack;
using ServiceStack.Host;
using ServiceStack.OrmLite;
using ServiceStack.Testing;

namespace MyApp.Tests;

/// <summary>
/// Whether an API is available in the organization's current state is decided once, when its organization is
/// resolved, from the access the API says it needs.
/// </summary>
public class WorkspaceAccessTests
{
    private static Workspace Organization(WorkspaceStatus status) => new() { Id = "organization-a", Name = "A", Slug = "a", Status = status };
    private static BillingSubscription Subscription(WorkspaceAccessMode mode) => new() { WorkspaceId = "organization-a", AccessMode = mode };

    [TestCase(WorkspaceStatus.Active, WorkspaceAccessMode.Full, WorkspaceAccess.Write, null)]
    [TestCase(WorkspaceStatus.Active, WorkspaceAccessMode.Grace, WorkspaceAccess.Write, null)]
    [TestCase(WorkspaceStatus.Active, WorkspaceAccessMode.FreeFallback, WorkspaceAccess.Write, null)]
    [TestCase(WorkspaceStatus.Active, WorkspaceAccessMode.ReadOnly, WorkspaceAccess.Read, null)]
    [TestCase(WorkspaceStatus.Active, WorkspaceAccessMode.ReadOnly, WorkspaceAccess.Write, "WorkspaceReadOnly")]
    [TestCase(WorkspaceStatus.PendingDeletion, WorkspaceAccessMode.ReadOnly, WorkspaceAccess.Read, null)]
    [TestCase(WorkspaceStatus.PendingDeletion, WorkspaceAccessMode.Full, WorkspaceAccess.Write, "WorkspaceReadOnly")]
    [TestCase(WorkspaceStatus.Suspended, WorkspaceAccessMode.Full, WorkspaceAccess.Read, "WorkspaceSuspended")]
    [TestCase(WorkspaceStatus.Active, WorkspaceAccessMode.Suspended, WorkspaceAccess.Read, "WorkspaceSuspended")]
    [TestCase(WorkspaceStatus.Suspended, WorkspaceAccessMode.Suspended, WorkspaceAccess.Write, "WorkspaceSuspended")]
    [TestCase(WorkspaceStatus.Suspended, WorkspaceAccessMode.Suspended, WorkspaceAccess.Account, null)]
    [TestCase(WorkspaceStatus.PendingDeletion, WorkspaceAccessMode.ReadOnly, WorkspaceAccess.Account, null)]
    public void Access_depends_on_the_state_of_the_organization(WorkspaceStatus status, WorkspaceAccessMode mode,
        WorkspaceAccess access, string? expectedError)
    {
        void Require() => WorkspaceAccessPolicy.Require(access, Organization(status), Subscription(mode));

        if (expectedError == null)
            Assert.DoesNotThrow(Require);
        else
            Assert.That(Assert.Throws<HttpError>(Require)!.ErrorCode, Is.EqualTo(expectedError));
    }

    [Test]
    public void A_suspended_organization_can_only_be_administered()
    {
        using var db = OpenWith(WorkspaceStatus.Suspended);
        var manager = new SaasManager(new SaasConfig());

        Assert.That(Assert.Throws<HttpError>(() => manager.AssertMembership(db, "organization-a", WorkspaceAccess.Write))!.ErrorCode,
            Is.EqualTo("WorkspaceSuspended"));
        Assert.That(Assert.Throws<HttpError>(() => manager.AssertMembership(db, "organization-a", WorkspaceAccess.Read))!.ErrorCode,
            Is.EqualTo("WorkspaceSuspended"));
        // The customer can still see their organization, and why it's unavailable
        Assert.That(manager.AssertMembership(db, "organization-a", WorkspaceAccess.Account).Workspace.Id, Is.EqualTo("organization-a"));
    }

    [Test]
    public void A_request_needs_to_say_which_organization_it_is_for_and_be_a_member_of_it()
    {
        using var db = OpenWith(WorkspaceStatus.Active);
        db.Insert(new Workspace { Id = "organization-b", Name = "B", Slug = "b" });
        var manager = new SaasManager(new SaasConfig());

        Assert.That(Assert.Throws<HttpError>(() => manager.AssertMembership(db, "", WorkspaceAccess.Read))!.ErrorCode,
            Is.EqualTo("WorkspaceIdRequired"));
        // user-1 isn't a member of organization-b, or of an organization that doesn't exist
        Assert.That(Assert.Throws<HttpError>(() => manager.AssertMembership(db, "organization-b", WorkspaceAccess.Read))!.ErrorCode,
            Is.EqualTo("WorkspaceAccessDenied"));
        Assert.That(Assert.Throws<HttpError>(() => manager.AssertMembership(db, "organization-x", WorkspaceAccess.Read))!.ErrorCode,
            Is.EqualTo("WorkspaceAccessDenied"));

        var (workspace, member) = manager.AssertMembership(db, "organization-a", WorkspaceAccess.Read);
        Assert.That(workspace.Id, Is.EqualTo("organization-a"));
        Assert.That(member.UserId, Is.EqualTo("user-1"));

        // Finding the membership doesn't confine the connection, which is done with what was found
        Assert.That(db.GetWorkspaceId(), Is.Null);
        db.GetWorkspaceScope().Confine(workspace, member);
        Assert.That(db.GetWorkspaceId(), Is.EqualTo("organization-a"));
        Assert.That(db.GetWorkspaceScope().IsAdmin, Is.True);

        // A scope can't move to another organization
        Assert.Throws<InvalidOperationException>(() => db.GetWorkspaceScope().Confine("organization-b"));
    }

    [Test]
    public void An_organization_pending_deletion_can_be_read_but_not_changed()
    {
        using var db = OpenWith(WorkspaceStatus.PendingDeletion);
        var manager = new SaasManager(new SaasConfig());

        Assert.DoesNotThrow(() => manager.AssertMembership(db, "organization-a", WorkspaceAccess.Read));
        Assert.That(Assert.Throws<HttpError>(() => manager.AssertMembership(db, "organization-a", WorkspaceAccess.Write))!.ErrorCode,
            Is.EqualTo("WorkspaceReadOnly"));
    }

    [TestCase("GET", WorkspaceAccess.Read)]
    [TestCase("HEAD", WorkspaceAccess.Read)]
    [TestCase("POST", WorkspaceAccess.Write)]
    [TestCase("PATCH", WorkspaceAccess.Write)]
    [TestCase("DELETE", WorkspaceAccess.Write)]
    public void Access_is_inferred_from_the_http_method_when_an_api_does_not_say(string verb, WorkspaceAccess expected) =>
        Assert.That(WorkspaceAccessPolicy.For(typeof(QueryStoredFiles), verb), Is.EqualTo(expected));

    [Test]
    public void Access_is_what_the_api_says_it_needs() =>
        Assert.That(WorkspaceAccessPolicy.For(typeof(LeaveWorkspace), "POST"), Is.EqualTo(WorkspaceAccess.Account));

    [Test]
    public void Opening_the_connection_of_a_workspace_api_checks_the_user_is_a_member_of_its_organization()
    {
        using var appHost = new BasicAppHost {
            ConfigureAppHost = host => host.Register<ISaasManager>(new SaasManager(new SaasConfig())),
        }.Init();

        // What the AppHost's DbConnectionRequestFilters apply to every connection opened for a request
        System.Data.IDbConnection OpenFor(object requestDto, string? apiKeyWorkspaceId = null)
        {
            var request = new BasicRequest(requestDto) {
                Items = { [Keywords.Session] = new AuthUserSession { UserAuthId = "user-1", IsAuthenticated = true } },
            };
            if (apiKeyWorkspaceId != null)
                request.Items[Keywords.ApiKey] = new ApiKeysFeature.ApiKey { UserId = "user-1", RefIdStr = apiKeyWorkspaceId };
            return Seed(WorkspaceStatus.Active).ForRequest(request);
        }

        using (var db = OpenFor(new QueryStoredFiles { WorkspaceId = "organization-a" }))
        {
            Assert.That(db.GetWorkspaceId(), Is.EqualTo("organization-a"));
            Assert.That(db.GetWorkspaceScope().Member.UserId, Is.EqualTo("user-1"));
        }

        Assert.That(Assert.Throws<HttpError>(() => OpenFor(new QueryStoredFiles { WorkspaceId = "organization-b" }))!.ErrorCode,
            Is.EqualTo("WorkspaceAccessDenied"));
        Assert.That(Assert.Throws<HttpError>(() => OpenFor(new QueryStoredFiles()))!.ErrorCode,
            Is.EqualTo("WorkspaceIdRequired"));

        // A request sent with an API key is for the key's organization, and can't say another
        using (var db = OpenFor(new QueryStoredFiles(), apiKeyWorkspaceId: "organization-a"))
            Assert.That(db.GetWorkspaceId(), Is.EqualTo("organization-a"));
        var mismatch = Assert.Throws<HttpError>(() =>
            OpenFor(new QueryStoredFiles { WorkspaceId = "organization-a" }, apiKeyWorkspaceId: "organization-b"));
        Assert.That(mismatch!.Message, Is.EqualTo("This API key is for another organization."));

        // A request that isn't for an organization can't use tables owned by one
        using (var db = OpenFor(new GetMyWorkspaces()))
        {
            Assert.That(db.GetWorkspaceId(), Is.Null);
            Assert.Throws<InvalidOperationException>(() => _ = db.GetWorkspaceScope().Workspace);
        }
    }

    // What ForRequest() applies to the connections opened for a signed-in user's request
    private static System.Data.IDbConnection OpenWith(WorkspaceStatus status) =>
        Seed(status).SetUserId("user-1").ForWorkspace(new WorkspaceScope());

    private static System.Data.IDbConnection Seed(WorkspaceStatus status)
    {
        var db = new OrmLiteConnectionFactory(":memory:", SqliteDialect.Provider).Open();
        db.CreateTable<Workspace>();
        db.CreateTable<WorkspaceMember>();
        db.CreateTable<UserWorkspacePreference>();
        db.CreateTable<SaasPlan>();
        db.CreateTable<SaasPlanVersion>();
        db.CreateTable<BillingSubscription>();
        db.CreateTable<SaasAuditEvent>();
        db.CreateTable<PlatformAuditEvent>();
        db.Insert(new SaasPlan { Id = "plan.free", Code = "free", Name = "Free" });
        db.Insert(new SaasPlanVersion { Id = "plan.free.v1", PlanId = "plan.free", Status = PlanVersionStatus.Published });
        db.Insert(Organization(status));
        db.Insert(new WorkspaceMember { WorkspaceId = "organization-a", UserId = "user-1", Role = WorkspaceMemberRole.Owner, Status = WorkspaceMemberStatus.Active });
        db.Insert(new UserWorkspacePreference { UserId = "user-1", ActiveWorkspaceId = "organization-a" });
        db.Insert(new BillingSubscription { WorkspaceId = "organization-a", PlanVersionId = "plan.free.v1", Status = SubscriptionStatus.Free, PeriodStart = DateTime.UtcNow.AddDays(-1), PeriodEnd = DateTime.UtcNow.AddDays(20) });
        return db;
    }
}

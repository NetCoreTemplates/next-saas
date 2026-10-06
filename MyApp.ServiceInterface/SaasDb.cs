using System.Data;
using MyApp.ServiceModel;
using ServiceStack;
using ServiceStack.Data;
using ServiceStack.OrmLite;
using ServiceStack.Web;

namespace MyApp.ServiceInterface;

/// <summary>
/// The organization that database connections are confined to. A request has one that's shared by every
/// connection it opens. It starts without an organization, so tables owned by one can't be used.
/// A request for an organization (IRequireWorkspace) also has the organization and the user's membership of it,
/// once the user has been checked to be a member.
/// </summary>
public sealed class WorkspaceScope
{
    private Workspace? workspace;
    private WorkspaceMember? member;

    /// <summary>
    /// The organization connections are confined to, if any
    /// </summary>
    public string? WorkspaceId { get; private set; }

    /// <summary>
    /// Whether the request's user has been checked to be a member of the organization
    /// </summary>
    public bool HasMember => member != null;

    public Workspace Workspace => workspace ?? throw NotForAnOrganization();
    public WorkspaceMember Member => member ?? throw NotForAnOrganization();
    public string UserId => Member.UserId;
    public bool IsAdmin => Member.Role is WorkspaceMemberRole.Owner or WorkspaceMemberRole.Admin;
    public bool CanManageBilling => Member.Role is WorkspaceMemberRole.Owner or WorkspaceMemberRole.Admin or WorkspaceMemberRole.Billing;

    public string AssertWorkspaceId() => WorkspaceId ?? throw new InvalidOperationException(
        "This database connection isn't confined to an organization. APIs that use tables owned by an " +
        "organization need a Request DTO that implements IRequireWorkspace, or use AcrossWorkspaces() for code " +
        "that works on more than one.");

    /// <summary>
    /// A scope can't move to another organization, as queries that already ran were confined to the first.
    /// </summary>
    public void Confine(string workspaceId)
    {
        if (string.IsNullOrEmpty(workspaceId))
            throw new ArgumentException("An organization is required.", nameof(workspaceId));
        if (WorkspaceId != null && WorkspaceId != workspaceId)
            throw new InvalidOperationException("This database connection is already confined to another organization.");
        WorkspaceId = workspaceId;
        // Every request and job that works for an organization goes through here, so its trace says which
        System.Diagnostics.Activity.Current?.SetTag(SaasTelemetry.WorkspaceTag, workspaceId);
    }

    /// <summary>
    /// Confine to an organization its user has been checked to be a member of
    /// </summary>
    public void Confine(Workspace workspace, WorkspaceMember member)
    {
        if (member.WorkspaceId != workspace.Id)
            throw new ArgumentException("The membership is of another organization.", nameof(member));
        Confine(workspace.Id);
        this.workspace = workspace;
        this.member = member;
    }

    private static InvalidOperationException NotForAnOrganization() => new(
        "This request isn't for an organization. Its Request DTO needs to implement IRequireWorkspace.");
}

/// <summary>
/// Filters and rules used by database connections, so that tenant isolation and audit columns are enforced
/// by the connection instead of being repeated by every query and write.
/// See https://react-templates.net/docs/next-saas/security/tenant-isolation
/// </summary>
public static class SaasDb
{
    /// <summary>
    /// The user id recorded by connections that don't say otherwise, e.g. startup tasks and background jobs.
    /// </summary>
    public const string SystemUserId = "system";

    // What's kept with a connection, see db.SetItem()
    private const string UserIdItem = "Saas.UserId";
    private const string ScopeItem = "Saas.WorkspaceScope";
    private const string AcrossWorkspacesItem = "Saas.AcrossWorkspaces";

    // The user id a connection writes its audit columns with. The rules read it for each row, so it can change
    // with WithUserId().
    private sealed class AuditUser
    {
        public string Id = SystemUserId;
    }

    /// <summary>
    /// Confines tables owned by an organization to the organization of the connection's scope, which they read for
    /// each statement, so the scope can be confined after the connection is opened
    /// </summary>
    public static readonly FilterSet<WorkspaceScope> WorkspaceFilters = FilterSet.Create<WorkspaceScope>(f => {
        f.Ensure<IHasWorkspaceId>(x => x.WorkspaceId, s => s.AssertWorkspaceId());
        f.Filter<Workspace>((x, s) => x.Id == s.AssertWorkspaceId());
        // API keys are bound to an organization by their RefIdStr. A request is authenticated with its API key
        // before its organization is resolved, so until then keys aren't filtered. The API Keys feature opens
        // the request's connection for its own APIs, so they're confined once the organization is resolved.
        f.Filter<ApiKeysFeature.ApiKey>((x, s) => s.WorkspaceId == null || x.RefIdStr == s.WorkspaceId);
    });

    // Sets the audit columns of every row a connection writes, with the user id it writes as
    private static readonly FilterSet<AuditUser> AuditRules = FilterSet.Create<AuditUser>(f => {
        // The created columns have [IgnoreOnUpdate], so they're only written when the row is inserted
        f.OnInsert<SaasAuditBase>(x => x.CreatedDate, _ => DateTime.UtcNow);
        f.OnInsert<SaasAuditBase>(x => x.CreatedBy, user => user.Id);
        f.OnWrite<SaasAuditBase>(x => x.ModifiedDate, _ => DateTime.UtcNow);
        f.OnWrite<SaasAuditBase>(x => x.ModifiedBy, user => user.Id);
    });

    /// <summary>
    /// Configure a connection opened for a request, which the AppHost's DbConnectionRequestFilters apply to all of them.
    /// It knows the request's user, and the organization the request is for.
    /// </summary>
    public static IDbConnection ForRequest(this IDbConnection db, IRequest request)
    {
        // Audit columns record who made the request: the signed-in user, or the user of its API key
        db.SetUserId(request.GetUserId());

        // Every connection a request opens shares one scope, which starts without an organization.
        // Setting the organization on the scope confines all of them.
        if (!request.Items.TryGetValue(nameof(WorkspaceScope), out var existing) || existing is not WorkspaceScope scope)
            request.Items[nameof(WorkspaceScope)] = scope = new WorkspaceScope();
        db.ForWorkspace(scope);

        // An API key is for the organization it was created for
        var apiKeyWorkspaceId = request.GetApiKey()?.RefIdStr;

        // An API for an organization says which one in its Request DTO. The first connection opened for it
        // checks the user is a member of that organization, and that it's in a state the API can be used in.
        if (request.Dto is IRequireWorkspace requireWorkspace && !scope.HasMember)
        {
            if (!apiKeyWorkspaceId.IsNullOrEmpty())
            {
                // A request sent with an API key doesn't need to say which organization, but can't say another
                if (requireWorkspace.WorkspaceId.IsNullOrEmpty())
                    requireWorkspace.WorkspaceId = apiKeyWorkspaceId!;
                else if (requireWorkspace.WorkspaceId != apiKeyWorkspaceId)
                    throw new HttpError(403, "WorkspaceAccessDenied", "This API key is for another organization.");
            }

            // Fails unless the user is a member of the organization, and it's in a state the API can be used in
            var (workspace, member) = request.TryResolve<ISaasManager>().AssertMembership(db, requireWorkspace.WorkspaceId,
                WorkspaceAccessPolicy.For(requireWorkspace.GetType(), request.Verb));

            // Confines every connection of the request, and is what its Services use: Db.GetWorkspaceScope()
            scope.Confine(workspace, member);
        }

        // Any other request sent with an API key can only use the key's organization
        if (!apiKeyWorkspaceId.IsNullOrEmpty())
            scope.Confine(apiKeyWorkspaceId!);

        return db;
    }

    /// <summary>
    /// The user this connection writes as, if it's been given one
    /// </summary>
    public static string? GetUserId(this IDbConnection db) =>
        GetAuditUser(db).Id is var userId && userId != SystemUserId ? userId : null;

    /// <summary>
    /// The scope this connection is confined by. For the connection of an <see cref="IRequireWorkspace"/> API
    /// it has the organization and the user's membership of it.
    /// </summary>
    public static WorkspaceScope GetWorkspaceScope(this IDbConnection db) => FindWorkspaceScope(db)
        ?? throw new InvalidOperationException("This database connection doesn't have a workspace scope, see ForWorkspace().");

    private static WorkspaceScope? FindWorkspaceScope(IDbConnection db) => db.IsWithoutFilters()
        ? null
        : db.GetItem<WorkspaceScope>(ScopeItem);

    /// <summary>
    /// The organization a request has been confined to, if any
    /// </summary>
    public static string? GetWorkspaceId(this IRequest request) =>
        request.Items.TryGetValue(nameof(WorkspaceScope), out var existing) ? (existing as WorkspaceScope)?.WorkspaceId : null;

    /// <summary>
    /// Confine this connection to the organization of a scope, which can be set later. It's applied to every
    /// connection opened for a request (see ForRequest) so tables owned by an organization:
    ///  - can't be used until the request's organization is resolved
    ///  - then only return, update and delete the rows of that organization
    ///  - are inserted with that organization, and can't be written with another
    /// API keys are confined to it too, once it's resolved.
    /// </summary>
    public static IDbConnection ForWorkspace(this IDbConnection db, WorkspaceScope scope)
    {
        if (db.IsWithoutFilters())
            throw new InvalidOperationException("A connection returned by AcrossWorkspaces() can't be confined to an organization.");
        if (db.GetItem<WorkspaceScope>(ScopeItem) is { } existing)
        {
            if (!ReferenceEquals(existing, scope))
                throw new InvalidOperationException("This database connection already has a workspace scope.");
            return db;
        }
        return db.SetItem(ScopeItem, scope).UseFilters(WorkspaceFilters.For(scope));
    }

    /// <summary>
    /// Open a connection confined to one organization, for code that isn't run for a request: background jobs and
    /// commands. It records who is writing, e.g. the name of the job:
    /// <code>using var db = dbFactory.OpenForWorkspace(request.WorkspaceId, "my-job");</code>
    /// Connections outside a request are opened with this or <see cref="OpenAcrossWorkspaces"/>, so each says
    /// whether it's confined. ArchitectureGuardTests limits dbFactory.Open() to an allow-list.
    /// </summary>
    public static IDbConnection OpenForWorkspace(this IDbConnectionFactory dbFactory, string workspaceId, string userId) =>
        dbFactory.Open().SetUserId(userId).ForWorkspace(workspaceId);

    /// <summary>
    /// Open a connection that isn't confined to an organization, for code that isn't run for a request and works
    /// on more than one: sweeps by a condition, webhooks and startup tasks. It records who is writing.
    /// Uses of it are limited to an allow-list by ArchitectureGuardTests, so each new use is reviewed.
    /// </summary>
    public static IDbConnection OpenAcrossWorkspaces(this IDbConnectionFactory dbFactory, string userId) =>
        dbFactory.Open().SetUserId(userId);

    /// <summary>
    /// Confine this connection to an organization. For a connection opened for a request this also confines the
    /// request's other connections.
    /// </summary>
    public static IDbConnection ForWorkspace(this IDbConnection db, string workspaceId)
    {
        var scope = FindWorkspaceScope(db);
        if (scope == null)
            db.ForWorkspace(scope = new WorkspaceScope());
        scope.Confine(workspaceId);
        return db;
    }

    /// <summary>
    /// The organization this connection is confined to, if any
    /// </summary>
    public static string? GetWorkspaceId(this IDbConnection db) => FindWorkspaceScope(db)?.WorkspaceId;

    /// <summary>
    /// Fails unless this connection is confined to the organization. Code that's given a connection and relies on it
    /// being confined, instead of writing the organization in its queries, checks it first, so it can't work on
    /// every organization's rows when it's given a connection that isn't.
    /// </summary>
    public static IDbConnection AssertConfinedTo(this IDbConnection db, string workspaceId)
    {
        var confinedTo = db.GetWorkspaceId();
        if (confinedTo != workspaceId)
            throw new InvalidOperationException(confinedTo == null
                ? "This database connection isn't confined to an organization, see ForWorkspace()."
                : "This database connection is confined to another organization.");
        return db;
    }

    /// <summary>
    /// The subscription of the organization this connection is confined to. Each organization has one.
    /// </summary>
    public static BillingSubscription GetSubscription(this IDbConnection db)
    {
        if (db.GetWorkspaceId() == null)
            throw new InvalidOperationException("This database connection isn't confined to an organization, see ForWorkspace().");
        return db.Single(db.From<BillingSubscription>())
            ?? throw new HttpError(404, "SubscriptionNotFound", "The organization subscription was not found.");
    }

    /// <summary>
    /// The same connection and transaction without being confined to an organization, for code that needs to
    /// work on more than one: finding which organizations a user belongs to, platform administration, and
    /// checks that must be unique across organizations. It still records who is writing.
    /// Uses of it are limited to an allow-list by ArchitectureGuardTests, so each new use is reviewed.
    /// </summary>
    public static IDbConnection AcrossWorkspaces(this IDbConnection db)
    {
        if (db.IsWithoutFilters())
            return db;

        return db.GetOrAddItem(AcrossWorkspacesItem, () => {
            // Without the connection's filters and rules, then given the audit rules again
            return db.WithoutFilters().UseFilters(AuditRules.For(GetAuditUser(db)));
        });
    }

    /// <summary>
    /// Set the audit columns of every row this connection writes. It's applied to every connection when it
    /// opens (see Configure.Db.cs), which then writes as <see cref="SystemUserId"/> until given a user id.
    /// </summary>
    public static IDbConnection WithAuditRules(this IDbConnection db)
    {
        return db.UseFilters(AuditRules.For(GetAuditUser(db)));
    }

    /// <summary>
    /// Record who is writing on this connection: the signed-in user of a request (see ForRequest)
    /// or the name of a background job, e.g. dbFactory.OpenAcrossWorkspaces("retention-job")
    /// </summary>
    public static IDbConnection SetUserId(this IDbConnection db, string? userId)
    {
        var user = GetAuditUser(db);
        user.Id = userId.IsNullOrEmpty() ? SystemUserId : userId!;
        return db.UseFilters(AuditRules.For(user));
    }

    /// <summary>
    /// Record a different user id for the writes in a using block, for code that's given who it's writing for, or
    /// for a policy that's applied by the system during a user's request:
    /// <code>using (db.WithUserId("lifecycle-policy")) db.Update(subscription);</code>
    /// </summary>
    public static IDisposable WithUserId(this IDbConnection db, string userId)
    {
        var user = GetAuditUser(db);
        var scope = new UserIdScope(user, user.Id);
        user.Id = userId;
        return scope;
    }

    // The user id is kept with the connection, and shared with its AcrossWorkspaces() connection
    private static AuditUser GetAuditUser(IDbConnection db) => db.GetOrAddItem(UserIdItem, () => new AuditUser());

    private sealed class UserIdScope(AuditUser user, string previous) : IDisposable
    {
        public void Dispose() => user.Id = previous;
    }
}

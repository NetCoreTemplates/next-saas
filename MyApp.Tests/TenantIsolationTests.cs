using System.Data;
using MyApp.ServiceInterface;
using MyApp.ServiceModel;
using NUnit.Framework;
using ServiceStack;
using ServiceStack.OrmLite;

namespace MyApp.Tests;

/// <summary>
/// Tables owned by an organization are confined to it by the database connection, see SaasDb.ForWorkspace().
/// These run against every table implementing IHasWorkspaceId, so a new tenant-owned table is covered
/// by implementing the interface.
/// </summary>
public class TenantIsolationTests
{
    private const string OrganizationA = "organization-a";
    private const string OrganizationB = "organization-b";

    private static readonly Type[] TenantTables = typeof(IHasWorkspaceId).Assembly.GetTypes()
        .Where(x => x is { IsClass: true, IsAbstract: false } && typeof(IHasWorkspaceId).IsAssignableFrom(x))
        .OrderBy(x => x.Name)
        .ToArray();

    private TestDatabase database = null!;
    private OrmLiteConnectionFactory factory = null!;

    [SetUp]
    public void SetUp()
    {
        database = TestDatabase.Create();
        factory = database.Factory;

        // These tests are about which rows a connection can use, so rows are seeded without their related rows
        database.CreateTables([
            typeof(Workspace), typeof(UserWorkspacePreference), typeof(ApiKeysFeature.ApiKey),
            ..TenantTables,
        ]);
        using var db = factory.Open();

        db.Insert(new Workspace { Id = OrganizationA, Name = "A", Slug = "a" });
        db.Insert(new Workspace { Id = OrganizationB, Name = "B", Slug = "b" });
        foreach (var table in TenantTables)
        {
            InsertRow(table, db, OrganizationA);
            InsertRow(table, db, OrganizationB);
        }
    }

    [TearDown]
    public void TearDown() => database.Dispose();

    [Test]
    public void Every_table_owned_by_an_organization_is_covered()
    {
        Assert.That(TenantTables.Select(x => x.Name), Is.SupersetOf(new[] {
            nameof(WorkspaceMember), nameof(BillingSubscription), nameof(UsagePeriod), nameof(UsageAggregate),
            nameof(StoredFile), nameof(NotificationPreference), nameof(SupportAccessGrant),
            nameof(SaasAuditEvent), nameof(NotificationDelivery),
        }));
    }

    [TestCaseSource(nameof(TenantTables))]
    public void A_confined_connection_only_reads_its_organizations_rows(Type table)
    {
        using var db = factory.Open().ForWorkspace(OrganizationA);

        Assert.That(SelectWorkspaceIds(table, db), Is.EqualTo(new[] { OrganizationA }));
    }

    [TestCaseSource(nameof(TenantTables))]
    public void A_confined_connection_cannot_delete_another_organizations_rows(Type table)
    {
        using var db = factory.Open().ForWorkspace(OrganizationA);

        DeleteAllRows(table, db);

        Assert.That(SelectWorkspaceIds(table, db), Is.Empty);
        Assert.That(SelectWorkspaceIds(table, db.AcrossWorkspaces()), Is.EqualTo(new[] { OrganizationB }));
    }

    [TestCaseSource(nameof(TenantTables))]
    public void A_confined_connection_cannot_update_another_organizations_rows(Type table)
    {
        using var db = factory.Open().ForWorkspace(OrganizationA);

        // Writing a row for another organization is rejected instead of silently changing no rows
        Assert.Throws<InvalidOperationException>(() => UpdateRowOf(table, db, OrganizationB));
    }

    [TestCaseSource(nameof(TenantTables))]
    public void Inserts_get_the_connections_organization_and_reject_another(Type table)
    {
        using var db = factory.Open().ForWorkspace(OrganizationA);
        DeleteAllRows(table, db);

        // Rows that don't say which organization they're for get the connection's
        InsertRow(table, db, "");
        Assert.That(SelectWorkspaceIds(table, db), Is.EqualTo(new[] { OrganizationA }));

        Assert.Throws<InvalidOperationException>(() => InsertRow(table, db, OrganizationB));
    }

    [TestCaseSource(nameof(TenantTables))]
    public void A_request_that_has_not_resolved_its_organization_cannot_use_tenant_tables(Type table)
    {
        // What the AppHost's DbConnectionRequestFilters apply to every connection opened for a request
        using var db = factory.Open().ForWorkspace(new WorkspaceScope());

        var error = Assert.Throws<InvalidOperationException>(() => SelectWorkspaceIds(table, db));
        Assert.That(error!.Message, Does.Contain("isn't confined to an organization"));
        Assert.Throws<InvalidOperationException>(() => InsertRow(table, db, OrganizationA));
        Assert.Throws<InvalidOperationException>(() => DeleteAllRows(table, db));
    }

    [TestCaseSource(nameof(TenantTables))]
    public void AcrossWorkspaces_uses_every_organizations_rows(Type table)
    {
        using var db = factory.Open().ForWorkspace(OrganizationA);

        Assert.That(SelectWorkspaceIds(table, db.AcrossWorkspaces()),
            Is.EquivalentTo(new[] { OrganizationA, OrganizationB }));
    }

    [Test]
    public void A_confined_connection_only_sees_its_own_organization()
    {
        using var db = factory.Open().ForWorkspace(OrganizationA);

        Assert.Multiple(() =>
        {
            Assert.That(db.Select<Workspace>().Select(x => x.Id), Is.EqualTo(new[] { OrganizationA }));
            Assert.That(db.SingleById<Workspace>(OrganizationB), Is.Null);
            Assert.That(db.AcrossWorkspaces().Count<Workspace>(), Is.EqualTo(2));
        });
    }

    [Test]
    public void By_id_lookups_do_not_return_another_organizations_row()
    {
        using var db = factory.Open().ForWorkspace(OrganizationA);
        var other = db.AcrossWorkspaces().Single<StoredFile>(x => x.WorkspaceId == OrganizationB);

        Assert.Multiple(() =>
        {
            Assert.That(db.SingleById<StoredFile>(other.Id), Is.Null);
            Assert.That(db.DeleteById<StoredFile>(other.Id), Is.Zero);
            Assert.That(db.UpdateOnly(() => new StoredFile { Name = "renamed" }, x => x.Id == other.Id), Is.Zero);
            Assert.That(db.AcrossWorkspaces().SingleById<StoredFile>(other.Id).Name, Is.Not.EqualTo("renamed"));
        });
    }

    [Test]
    public void Inserted_rows_have_the_organization_they_were_written_with()
    {
        using var db = factory.Open().ForWorkspace(OrganizationA);

        var note = new SupportNote { Body = "inserted" };
        db.Insert(note);

        // The row can be returned or used without reading it back
        Assert.That(note.WorkspaceId, Is.EqualTo(OrganizationA));
        Assert.That(db.SingleById<SupportNote>(note.Id).WorkspaceId, Is.EqualTo(OrganizationA));
    }

    [Test]
    public void Resolving_the_organization_confines_every_connection_of_the_request()
    {
        var scope = new WorkspaceScope();
        using var db = factory.Open().ForWorkspace(scope);
        using var autoQueryDb = factory.Open().ForWorkspace(scope);
        Assert.Throws<InvalidOperationException>(() => autoQueryDb.Select<StoredFile>());

        db.ForWorkspace(OrganizationA);

        Assert.That(autoQueryDb.Select<StoredFile>().Select(x => x.WorkspaceId), Is.EqualTo(new[] { OrganizationA }));
        Assert.That(autoQueryDb.GetWorkspaceId(), Is.EqualTo(OrganizationA));
    }

    [Test]
    public void Api_keys_are_confined_to_their_organization_once_it_is_resolved()
    {
        using (var setup = factory.Open())
        {
            setup.Insert(new ApiKeysFeature.ApiKey { Key = "ak-a", UserId = "user-1", RefIdStr = OrganizationA, CreatedDate = DateTime.UtcNow });
            setup.Insert(new ApiKeysFeature.ApiKey { Key = "ak-b", UserId = "user-1", RefIdStr = OrganizationB, CreatedDate = DateTime.UtcNow });
        }
        using var db = factory.Open().ForWorkspace(new WorkspaceScope());

        // A request is authenticated with its API key before its organization is known
        Assert.That(db.Single<ApiKeysFeature.ApiKey>(x => x.Key == "ak-b"), Is.Not.Null);

        db.ForWorkspace(OrganizationA);

        Assert.That(db.Select<ApiKeysFeature.ApiKey>(x => x.UserId == "user-1").Select(x => x.Key), Is.EqualTo(new[] { "ak-a" }));
        Assert.That(db.Single<ApiKeysFeature.ApiKey>(x => x.Key == "ak-b"), Is.Null);
    }

    [Test]
    public void Confining_a_connection_tags_the_current_trace_with_its_organization()
    {
        using var listener = new System.Diagnostics.ActivityListener {
            ShouldListenTo = _ => true,
            Sample = (ref System.Diagnostics.ActivityCreationOptions<System.Diagnostics.ActivityContext> _) =>
                System.Diagnostics.ActivitySamplingResult.AllData,
        };
        System.Diagnostics.ActivitySource.AddActivityListener(listener);
        using var activity = SaasTelemetry.Activities.StartActivity("test");

        using var db = factory.Open().ForWorkspace(OrganizationA);

        Assert.That(activity!.GetTagItem(SaasTelemetry.WorkspaceTag), Is.EqualTo(OrganizationA));
    }

    [Test]
    public void A_confined_connection_cannot_move_to_another_organization()
    {
        using var db = factory.Open().ForWorkspace(OrganizationA);

        Assert.DoesNotThrow(() => db.ForWorkspace(OrganizationA));
        Assert.Throws<InvalidOperationException>(() => db.ForWorkspace(OrganizationB));
        Assert.Throws<InvalidOperationException>(() => db.AcrossWorkspaces().ForWorkspace(OrganizationB));
    }

    [Test]
    public void AcrossWorkspaces_still_records_who_is_writing()
    {
        using var db = factory.Open().SetUserId("operator-1").ForWorkspace(OrganizationA);

        db.AcrossWorkspaces().Insert(new SupportNote { WorkspaceId = OrganizationB, Body = "note" });

        var note = db.AcrossWorkspaces().Single<SupportNote>(x => x.Body == "note");
        Assert.Multiple(() =>
        {
            Assert.That(note.WorkspaceId, Is.EqualTo(OrganizationB));
            Assert.That(note.CreatedBy, Is.EqualTo("operator-1"));
            Assert.That(note.CreatedDate, Is.Not.EqualTo(default(DateTime)));
        });
    }

    [Test]
    public void Confining_to_the_organization_a_request_is_for_confines_the_connection()
    {
        using var db = factory.Open();
        db.Insert(new WorkspaceMember {
            WorkspaceId = OrganizationB, UserId = "user-1", Role = WorkspaceMemberRole.Member, Status = WorkspaceMemberStatus.Active,
        });

        // What ForRequest() applies to the connections opened for a signed-in user's request
        using var requestDb = factory.Open().SetUserId("user-1").ForWorkspace(new WorkspaceScope());
        var manager = new SaasManager(new SaasConfig());
        var (workspace, member) = manager.AssertMembership(requestDb, OrganizationB, WorkspaceAccess.Account);
        requestDb.GetWorkspaceScope().Confine(workspace, member);

        Assert.Multiple(() =>
        {
            Assert.That(requestDb.GetWorkspaceScope().Workspace.Id, Is.EqualTo(OrganizationB));
            Assert.That(requestDb.GetWorkspaceId(), Is.EqualTo(OrganizationB));
            Assert.That(requestDb.Select<StoredFile>().Select(x => x.WorkspaceId), Is.EqualTo(new[] { OrganizationB }));
        });
    }

    private static string[] SelectWorkspaceIds(Type table, IDbConnection db) =>
        db.CreateTypedApi(table).Select().Cast<IHasWorkspaceId>().Select(x => x.WorkspaceId).ToArray();

    private static int DeleteAllRows(Type table, IDbConnection db) => db.DeleteAll(table);

    private static int UpdateRowOf(Type table, IDbConnection db, string workspaceId) =>
        db.CreateTypedApi(table).Update(db.AcrossWorkspaces().CreateTypedApi(table).Select()
            .Cast<IHasWorkspaceId>().First(x => x.WorkspaceId == workspaceId));

    private static long InsertRow(Type table, IDbConnection db, string workspaceId)
    {
        var row = (IHasWorkspaceId)table.CreateInstance();
        // Give columns in unique constraints a different value in each row
        foreach (var property in table.GetProperties().Where(x => x.PropertyType == typeof(string) && x.CanWrite))
        {
            if (property.Name != nameof(IHasWorkspaceId.WorkspaceId) && (string?)property.GetValue(row) == "")
                property.SetValue(row, Guid.NewGuid().ToString("N"));
        }
        row.WorkspaceId = workspaceId;
        return db.CreateTypedApi(table).Insert(row);
    }
}

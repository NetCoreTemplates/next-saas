using System.IO.Compression;
using System.Text.Json;
using MyApp.ServiceInterface;
using MyApp.ServiceModel;
using NUnit.Framework;
using ServiceStack;
using ServiceStack.OrmLite;

namespace MyApp.Tests;

/// <summary>
/// An organization's export and deletion cover every table it owns without listing them, and its background
/// work and stored files can't reach another organization. See WorkspaceData.
/// </summary>
public class WorkspaceDataTests
{
    private const string OrganizationA = "organization-a";
    private const string OrganizationB = "organization-b";

    private string root = "";
    private TestDatabase database = null!;
    private OrmLiteConnectionFactory factory = null!;
    private IFileStore files = null!;

    [SetUp]
    public void SetUp()
    {
        root = Path.Combine(Path.GetTempPath(), $"next-saas-lifecycle-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        database = TestDatabase.Create();
        factory = database.Factory;
        files = new LocalFileStore(new FileStorageConfig { RootPath = Path.Combine(root, "files") }, root);

        // Rows are seeded without their related rows, as these tests are about which organization's rows are used
        database.CreateTables([
            typeof(Workspace), typeof(UserWorkspacePreference), typeof(ApiKeysFeature.ApiKey), ..WorkspaceData.Tables,
        ]);
        using var db = factory.Open();
        foreach (var organization in new[] { OrganizationA, OrganizationB })
        {
            db.Insert(new Workspace { Id = organization, Name = organization, Slug = organization });
            db.Insert(new ApiKeysFeature.ApiKey { Key = $"ak-{organization}", UserId = "user-1", RefIdStr = organization, CreatedDate = DateTime.UtcNow });
            foreach (var table in WorkspaceData.Tables)
                InsertRow(db, table, organization);
        }
    }

    [TearDown]
    public void TearDown()
    {
        database.Dispose();
        Directory.Delete(root, recursive: true);
    }

    [Test]
    public void Rows_are_deleted_before_the_rows_they_reference()
    {
        var order = WorkspaceData.Tables.ToList();

        Assert.Multiple(() =>
        {
            Assert.That(order.IndexOf(typeof(UsageAggregate)), Is.LessThan(order.IndexOf(typeof(UsagePeriod))));
            Assert.That(order.IndexOf(typeof(UsageEvent)), Is.LessThan(order.IndexOf(typeof(UsagePeriod))));
            Assert.That(order.IndexOf(typeof(DataExportArtifact)), Is.LessThan(order.IndexOf(typeof(WorkspaceLifecycleRequest))));
        });
    }

    [Test]
    public void Exclusions_from_export_and_deletion_say_why()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WorkspaceData.NotExported.Keys, Is.SubsetOf(WorkspaceData.Tables));
            Assert.That(WorkspaceData.KeptAfterDeletion.Keys, Is.SubsetOf(WorkspaceData.Tables));
            Assert.That(WorkspaceData.NotExported.Values.Concat(WorkspaceData.KeptAfterDeletion.Values), Has.All.Not.Empty);
        });
    }

    [Test]
    public void Export_and_deletion_need_a_connection_confined_to_the_organization()
    {
        using var db = factory.Open();

        Assert.Throws<InvalidOperationException>(() => WorkspaceData.ExportJson(db));
        Assert.Throws<InvalidOperationException>(() => WorkspaceData.DeleteAll(db));
    }

    [Test]
    public async Task Deleting_an_organization_deletes_every_table_it_owns_and_nothing_else()
    {
        using (var db = factory.Open())
        {
            var file = db.Single<StoredFile>(x => x.WorkspaceId == OrganizationA);
            file.ObjectKey = files.ForWorkspace(OrganizationA).NewFileKey();
            db.Update(file);
            await files.ForWorkspace(OrganizationA).WriteAsync(file.ObjectKey, new MemoryStream([1, 2, 3]), 10);
            Assert.That(files.Exists(file.ObjectKey), Is.True);
            foreach (var other in db.Select<StoredFile>(x => x.WorkspaceId == OrganizationB))
            {
                other.ObjectKey = files.ForWorkspace(OrganizationB).NewFileKey();
                db.Update(other);
                await files.ForWorkspace(OrganizationB).WriteAsync(other.ObjectKey, new MemoryStream([4, 5, 6]), 10);
            }
            foreach (var artifact in db.Select<DataExportArtifact>())
            {
                artifact.ObjectKey = files.ForWorkspace(artifact.WorkspaceId).ExportKey(artifact.Id);
                db.Update(artifact);
            }
        }
        var operation = Schedule(OrganizationA, LifecycleRequestType.Delete);

        await RunAsync(new ProcessWorkspaceLifecycle { WorkspaceId = OrganizationA, RequestId = operation.Id });

        using var verify = factory.Open();
        Assert.Multiple(() =>
        {
            foreach (var table in WorkspaceData.Tables)
            {
                var remaining = WorkspaceIds(verify, table);
                var expected = WorkspaceData.KeptAfterDeletion.ContainsKey(table)
                    ? Is.SupersetOf(new[] { OrganizationA, OrganizationB })
                    : (NUnit.Framework.Constraints.IResolveConstraint)Is.EqualTo(new[] { OrganizationB });
                Assert.That(remaining.Distinct(), expected, table.Name);
            }
            Assert.That(verify.Select<ApiKeysFeature.ApiKey>().Where(x => x.CancelledDate == null).Select(x => x.RefIdStr), Is.EqualTo(new[] { OrganizationB }));
            Assert.That(verify.SingleById<Workspace>(OrganizationA).Status, Is.EqualTo(WorkspaceStatus.Deleted));
            Assert.That(verify.SingleById<Workspace>(OrganizationB).Status, Is.EqualTo(WorkspaceStatus.Active));
            Assert.That(verify.SingleById<WorkspaceLifecycleRequest>(operation.Id).Status, Is.EqualTo(LifecycleRequestStatus.Completed));
            var otherFile = verify.Single<StoredFile>(x => x.WorkspaceId == OrganizationB);
            Assert.That(files.Exists(otherFile.ObjectKey), Is.True);
            Assert.That(Directory.Exists(Path.Combine(root, "files", "workspaces", OrganizationA, "files")) &&
                        Directory.EnumerateFiles(Path.Combine(root, "files", "workspaces", OrganizationA, "files")).Any(), Is.False);
        });
    }

    [Test]
    public async Task Exporting_an_organization_includes_every_table_it_owns_and_nothing_else()
    {
        using (var db = factory.Open())
        {
            foreach (var file in db.Select<StoredFile>())
            {
                file.Status = StoredFileStatus.Available;
                file.Name = "notes.txt";
                file.ObjectKey = files.ForWorkspace(file.WorkspaceId).NewFileKey();
                db.Update(file);
                await files.ForWorkspace(file.WorkspaceId).WriteAsync(file.ObjectKey, new MemoryStream([1, 2, 3]), 10);
            }
            var member = db.Single<WorkspaceMember>(x => x.WorkspaceId == OrganizationA);
            member.InvitationTokenHash = "secret-hash";
            db.Update(member);
        }
        var operation = Schedule(OrganizationA, LifecycleRequestType.Export);

        await RunAsync(new ProcessWorkspaceLifecycle { WorkspaceId = OrganizationA, RequestId = operation.Id });

        using var verify = factory.Open();
        var artifact = verify.Single<DataExportArtifact>(x => x.LifecycleRequestId == operation.Id);
        Assert.That(artifact.WorkspaceId, Is.EqualTo(OrganizationA));
        await using var stream = await files.ForWorkspace(OrganizationA).OpenReadAsync(artifact.ObjectKey);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.GetEntry("manifest.json")!.Open());
        var json = await reader.ReadToEndAsync();
        using var manifest = JsonDocument.Parse(json);
        var tables = manifest.RootElement.GetProperty("tables");

        Assert.Multiple(() =>
        {
            Assert.That(tables.EnumerateObject().Select(x => x.Name),
                Is.EquivalentTo(WorkspaceData.ExportedTables.Select(x => x.Name)));
            foreach (var table in tables.EnumerateObject())
            {
                Assert.That(table.Value.GetArrayLength(), Is.GreaterThan(0), table.Name);
                Assert.That(table.Value.EnumerateArray().Select(x => Property(x, "WorkspaceId")),
                    Has.All.EqualTo(OrganizationA), table.Name);
            }
            Assert.That(Property(manifest.RootElement.GetProperty("workspace"), "Id"), Is.EqualTo(OrganizationA));
            Assert.That(tables.TryGetProperty(nameof(SaasAuditEvent), out _), Is.True, "The audit log is exported");
            Assert.That(json, Does.Not.Contain(OrganizationB));
            Assert.That(json, Does.Not.Contain("secret-hash"));
            Assert.That(json, Does.Not.Contain("workspaces/"), "Storage keys aren't exported");
            Assert.That(archive.Entries.Count(x => x.FullName.StartsWith("files/")), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Work_for_one_organization_cannot_be_pointed_at_anothers_rows()
    {
        var operation = Schedule(OrganizationB, LifecycleRequestType.Delete);

        // The request for B is found by a command confined to A as if it didn't exist
        await RunAsync(new ProcessWorkspaceLifecycle { WorkspaceId = OrganizationA, RequestId = operation.Id });

        using var verify = factory.Open();
        Assert.Multiple(() =>
        {
            Assert.That(verify.SingleById<WorkspaceLifecycleRequest>(operation.Id).Status, Is.EqualTo(LifecycleRequestStatus.Scheduled));
            Assert.That(verify.Count<WorkspaceMember>(), Is.EqualTo(2));
            Assert.That(verify.Select<Workspace>().Select(x => x.Status), Has.All.EqualTo(WorkspaceStatus.Active));
        });
    }

    [Test]
    public void The_file_store_of_an_organization_rejects_keys_outside_it()
    {
        var organizationFiles = files.ForWorkspace(OrganizationA);
        var other = files.ForWorkspace(OrganizationB).NewFileKey();

        Assert.Multiple(() =>
        {
            Assert.That(organizationFiles.NewFileKey(), Does.StartWith($"workspaces/{OrganizationA}/files/"));
            Assert.ThrowsAsync<InvalidOperationException>(() => organizationFiles.OpenReadAsync(other));
            Assert.ThrowsAsync<InvalidOperationException>(() => organizationFiles.DeleteAsync(other));
            Assert.ThrowsAsync<InvalidOperationException>(() => organizationFiles.WriteAsync(other, new MemoryStream(), 1));
            Assert.ThrowsAsync<InvalidOperationException>(() =>
                organizationFiles.OpenReadAsync($"workspaces/{OrganizationA}/../{OrganizationB}/files/x"));
            Assert.ThrowsAsync<InvalidOperationException>(() => organizationFiles.OpenReadAsync("/etc/passwd"));
            Assert.Throws<ArgumentException>(() => files.ForWorkspace("../organization-b"));
            Assert.Throws<ArgumentException>(() => files.ForWorkspace(""));
        });
    }

    // The app serializes with camelCase names, which isn't configured in unit tests
    private static string? Property(JsonElement row, string name) =>
        row.EnumerateObject().First(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Value.GetString();

    private WorkspaceLifecycleRequest Schedule(string organization, LifecycleRequestType type)
    {
        using var db = factory.Open();
        var operation = new WorkspaceLifecycleRequest {
            WorkspaceId = organization, Type = type, Status = LifecycleRequestStatus.Scheduled, RequestedBy = "user-1",
        };
        db.Insert(operation);
        return operation;
    }

    private Task RunAsync(ProcessWorkspaceLifecycle request) =>
        new ProcessWorkspaceLifecycleCommand(factory, files, new SaasConfig()).ExecuteAsync(request);

    private static List<string> WorkspaceIds(System.Data.IDbConnection db, Type table) =>
        db.CreateTypedApi(table).Select().Cast<IHasWorkspaceId>().Select(x => x.WorkspaceId).ToList();

    private static void InsertRow(System.Data.IDbConnection db, Type table, string organization)
    {
        var row = (IHasWorkspaceId)table.CreateInstance();
        // Give columns in unique constraints a different value in each row
        foreach (var property in table.GetProperties().Where(x => x.PropertyType == typeof(string) && x.CanWrite))
        {
            if (property.Name != nameof(IHasWorkspaceId.WorkspaceId) && (string?)property.GetValue(row) == "")
                property.SetValue(row, Guid.NewGuid().ToString("N"));
        }
        row.WorkspaceId = organization;
        db.CreateTypedApi(table).Insert(row);
    }
}

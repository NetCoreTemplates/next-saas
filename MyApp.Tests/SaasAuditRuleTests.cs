using MyApp.ServiceInterface;
using MyApp.ServiceModel;
using NUnit.Framework;
using ServiceStack.OrmLite;

namespace MyApp.Tests;

/// <summary>
/// The audit columns of SaasAuditBase rows are set by the rules of the connection that writes them, see SaasDb.
/// </summary>
public class SaasAuditRuleTests
{
    private static System.Data.IDbConnection Open()
    {
        var db = new OrmLiteConnectionFactory(":memory:", SqliteDialect.Provider).Open();
        db.CreateTable<Workspace>();
        return db;
    }

    private static Workspace NewWorkspace() => new() { Name = "Acme", Slug = "acme-" + Guid.NewGuid().ToString("N") };

    [Test]
    public void Connections_write_as_system_until_told_who_is_writing()
    {
        using var db = Open();
        var before = DateTime.UtcNow.AddSeconds(-1);

        db.Insert(NewWorkspace());

        var row = db.Select<Workspace>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(row.CreatedBy, Is.EqualTo(SaasDb.SystemUserId));
            Assert.That(row.ModifiedBy, Is.EqualTo(SaasDb.SystemUserId));
            Assert.That(row.CreatedDate, Is.GreaterThan(before));
            Assert.That(row.ModifiedDate, Is.GreaterThan(before));
        });
    }

    [Test]
    public void Rows_record_the_actor_of_the_connection()
    {
        using var db = Open().SetUserId("user-1");

        db.Insert(NewWorkspace());

        var row = db.Select<Workspace>().Single();
        Assert.That(row.CreatedBy, Is.EqualTo("user-1"));
        Assert.That(row.ModifiedBy, Is.EqualTo("user-1"));
    }

    [Test]
    public void Rules_replace_audit_values_set_by_the_caller()
    {
        using var db = Open().SetUserId("user-1");
        var workspace = NewWorkspace();
        workspace.CreatedBy = workspace.ModifiedBy = "someone-else";
        workspace.CreatedDate = workspace.ModifiedDate = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        db.Insert(workspace);

        var row = db.Select<Workspace>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(row.CreatedBy, Is.EqualTo("user-1"));
            Assert.That(row.ModifiedBy, Is.EqualTo("user-1"));
            Assert.That(row.CreatedDate.Year, Is.GreaterThan(2001));
            Assert.That(row.ModifiedDate.Year, Is.GreaterThan(2001));
        });
    }

    [Test]
    public void Updates_keep_who_created_the_row()
    {
        using var db = Open().SetUserId("user-1");
        db.Insert(NewWorkspace());
        var created = db.Select<Workspace>().Single();

        db.SetUserId("user-2");
        // A row that wasn't read from the database, e.g. one populated from a request
        db.Update(new Workspace { Id = created.Id, Slug = created.Slug, Name = "updated" });

        var row = db.Select<Workspace>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(row.Name, Is.EqualTo("updated"));
            Assert.That(row.CreatedBy, Is.EqualTo("user-1"));
            Assert.That(row.CreatedDate, Is.EqualTo(created.CreatedDate));
            Assert.That(row.ModifiedBy, Is.EqualTo("user-2"));
            Assert.That(row.ModifiedDate, Is.GreaterThanOrEqualTo(created.ModifiedDate));
        });
    }

    [Test]
    public void ActAs_records_a_different_actor_until_it_is_disposed()
    {
        using var db = Open().SetUserId("user-1");
        db.Insert(NewWorkspace());
        var workspace = db.Select<Workspace>().Single();

        using (db.WithUserId("lifecycle-policy"))
        {
            db.Update(workspace);
            Assert.That(db.SingleById<Workspace>(workspace.Id).ModifiedBy, Is.EqualTo("lifecycle-policy"));

            using (db.WithUserId("nested"))
                db.Update(workspace);
            db.Update(workspace);
            Assert.That(db.SingleById<Workspace>(workspace.Id).ModifiedBy, Is.EqualTo("lifecycle-policy"));
        }

        db.Update(workspace);
        var row = db.SingleById<Workspace>(workspace.Id);
        Assert.That(row.ModifiedBy, Is.EqualTo("user-1"));
        Assert.That(row.CreatedBy, Is.EqualTo("user-1"));
    }

    [Test]
    public void Partial_updates_set_who_modified_the_row()
    {
        using var db = Open().SetUserId("user-1");
        db.Insert(NewWorkspace());
        var workspace = db.Select<Workspace>().Single();

        db.SetUserId("user-2");
        db.UpdateOnly(() => new Workspace { Name = "updated" }, x => x.Id == workspace.Id);

        var row = db.SingleById<Workspace>(workspace.Id);
        Assert.That(row.Name, Is.EqualTo("updated"));
        Assert.That(row.ModifiedBy, Is.EqualTo("user-2"));
        Assert.That(row.CreatedBy, Is.EqualTo("user-1"));
    }

    [Test]
    public void Written_rows_have_the_audit_values_they_were_written_with()
    {
        using var db = Open().SetUserId("user-1");

        var workspace = NewWorkspace();
        db.Insert(workspace);

        // The row can be returned or used without reading it back
        Assert.Multiple(() =>
        {
            Assert.That(workspace.CreatedDate, Is.Not.EqualTo(default(DateTime)));
            Assert.That(workspace.ModifiedDate, Is.Not.EqualTo(default(DateTime)));
            Assert.That(workspace.CreatedBy, Is.EqualTo("user-1"));
            Assert.That(workspace.ModifiedBy, Is.EqualTo("user-1"));
        });

        db.SetUserId("user-2");
        db.Update(workspace);
        Assert.That(workspace.ModifiedBy, Is.EqualTo("user-2"));
        Assert.That(workspace.CreatedBy, Is.EqualTo("user-1"));
    }

    [Test]
    public void WithoutFilters_writes_the_audit_values_it_is_given()
    {
        using var db = Open().SetUserId("user-1");
        var seeded = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var workspace = NewWorkspace();
        workspace.CreatedBy = workspace.ModifiedBy = "seed";
        workspace.CreatedDate = workspace.ModifiedDate = seeded;

        db.WithoutFilters().Insert(workspace);

        var row = db.Select<Workspace>().Single();
        Assert.That(row.CreatedBy, Is.EqualTo("seed"));
        Assert.That(row.CreatedDate.Year, Is.EqualTo(2001));
    }
}

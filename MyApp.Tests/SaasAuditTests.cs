using MyApp.ServiceInterface;
using MyApp.ServiceModel;
using NUnit.Framework;
using ServiceStack.Data;
using ServiceStack.OrmLite;
using ServiceStack.OrmLite.Sqlite;

namespace MyApp.Tests;

public class SaasAuditTests
{
    private static IDbConnectionFactory CreateFactory() =>
        new OrmLiteConnectionFactory(":memory:", SqliteOrmLiteDialectProvider.Instance);

    // The organization an event is about has to exist
    private static void CreateOrganization(System.Data.IDbConnection db, string id)
    {
        db.CreateTable<Workspace>();
        db.AcrossWorkspaces().Insert(new Workspace { Id = id, Name = id, Slug = id });
    }

    [Test]
    public void Audit_insert_uses_policy_and_redacts_secrets()
    {
        using var db = CreateFactory().Open();
        CreateOrganization(db, "workspace-1");
        db.CreateTable<SaasAuditEvent>();
        db.CreateTable<PlatformAuditEvent>();

        db.Insert(new SaasAuditEvent
        {
            WorkspaceId = "workspace-1",
            Category = "support",
            Action = "note.created",
            UserId = "user-1",
            DetailJson = """{"password":"hunter2","nested":{"apiKey":"ak-abcdef123456","message":"Stripe sk_test_abc123"}}""",
        });

        var row = db.Select<SaasAuditEvent>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(row.CreatedDate, Is.Not.EqualTo(default(DateTime)));
            Assert.That(row.Outcome, Is.EqualTo("Succeeded"));
            Assert.That(row.DetailJson, Does.Not.Contain("hunter2"));
            Assert.That(row.DetailJson, Does.Not.Contain("ak-abcdef123456"));
            Assert.That(row.DetailJson, Does.Not.Contain("sk_test_abc123"));
            Assert.That(row.DetailJson, Does.Contain("[REDACTED]"));
        });
    }

    [Test]
    public void Audit_insert_rejects_unregistered_actions()
    {
        using var db = CreateFactory().Open();
        db.CreateTable<SaasAuditEvent>();
        db.CreateTable<PlatformAuditEvent>();

        var exception = Assert.Throws<InvalidOperationException>(() => db.Insert(new SaasAuditEvent
        {
            Category = "stripe",
            Action = "unknown.action",
            UserId = "system",
        }));

        Assert.That(exception!.Message, Does.Contain("is not registered"));
        Assert.That(db.Count<SaasAuditEvent>(), Is.Zero);
    }

    [Test]
    public void Tenant_audit_requires_an_organization()
    {
        using var db = CreateFactory().Open();
        db.CreateTable<SaasAuditEvent>();
        db.CreateTable<PlatformAuditEvent>();

        var exception = Assert.Throws<InvalidOperationException>(() => db.Insert(new SaasAuditEvent
        {
            Category = "support",
            Action = "note.created",
            UserId = "user-1",
        }));

        Assert.That(exception!.Message, Does.Contain("requires an organization ID"));
    }

    [Test]
    public void Audit_events_get_the_organization_of_a_confined_connection()
    {
        using var db = CreateFactory().Open().ForWorkspace("workspace-1");
        CreateOrganization(db, "workspace-1");
        db.CreateTable<SaasAuditEvent>();

        db.Insert(new SaasAuditEvent { Category = "support", Action = "note.created", UserId = "user-1" });

        Assert.That(db.Select<SaasAuditEvent>().Single().WorkspaceId, Is.EqualTo("workspace-1"));
        Assert.Throws<InvalidOperationException>(() => db.Insert(new SaasAuditEvent {
            WorkspaceId = "workspace-2", Category = "support", Action = "note.created", UserId = "user-1",
        }));
    }

    [Test]
    public void Events_are_written_to_the_log_their_category_belongs_to()
    {
        using var db = CreateFactory().Open();
        db.CreateTable<SaasAuditEvent>();
        db.CreateTable<PlatformAuditEvent>();

        db.Insert(new PlatformAuditEvent { Category = "plan", Action = "draft.saved", UserId = "admin-1" });
        Assert.That(db.Count<PlatformAuditEvent>(), Is.EqualTo(1));

        // An organization's log only has events about it, and the platform's has none about an organization
        Assert.That(Assert.Throws<InvalidOperationException>(() => db.Insert(new SaasAuditEvent {
            WorkspaceId = "workspace-1", Category = "plan", Action = "draft.saved", UserId = "admin-1",
        }))!.Message, Does.Contain(nameof(PlatformAuditEvent)));
        Assert.That(Assert.Throws<InvalidOperationException>(() => db.Insert(new PlatformAuditEvent {
            Category = "support", Action = "note.created", UserId = "admin-1",
        }))!.Message, Does.Contain(nameof(SaasAuditEvent)));
    }

    [TestCase("checkout.session.completed")]
    [TestCase("invoice.paid")]
    [TestCase("customer.subscription.updated")]
    public void Stripe_webhook_actions_are_registered(string action) =>
        Assert.That(SaasAudit.IsRegistered("billing", action), Is.True);
}

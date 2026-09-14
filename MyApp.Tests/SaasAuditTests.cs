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

    [Test]
    public void Audit_insert_uses_policy_and_redacts_secrets()
    {
        using var db = CreateFactory().Open();
        db.CreateTable<SaasAuditEvent>();

        db.Insert(new SaasAuditEvent
        {
            WorkspaceId = "workspace-1",
            Category = "support",
            Action = "note.created",
            ActorId = "user-1",
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

        var exception = Assert.Throws<InvalidOperationException>(() => db.Insert(new SaasAuditEvent
        {
            Category = "stripe",
            Action = "unknown.action",
            ActorId = "system",
        }));

        Assert.That(exception!.Message, Does.Contain("is not registered"));
        Assert.That(db.Count<SaasAuditEvent>(), Is.Zero);
    }

    [Test]
    public void Tenant_audit_requires_an_organization()
    {
        using var db = CreateFactory().Open();
        db.CreateTable<SaasAuditEvent>();

        var exception = Assert.Throws<InvalidOperationException>(() => db.Insert(new SaasAuditEvent
        {
            Category = "support",
            Action = "note.created",
            ActorId = "user-1",
        }));

        Assert.That(exception!.Message, Does.Contain("requires an organization ID"));
    }

    [TestCase("checkout.session.completed")]
    [TestCase("invoice.paid")]
    [TestCase("customer.subscription.updated")]
    public void Stripe_webhook_actions_are_registered(string action) =>
        Assert.That(SaasAudit.IsRegistered("billing", action), Is.True);
}

using MyApp.ServiceInterface;
using NUnit.Framework;
using ServiceStack.OrmLite;

namespace MyApp.Tests;

[SetUpFixture]
public sealed class AssemblySetup
{
    [OneTimeSetUp]
    public void RegisterServiceStackLicense() => AppHost.RegisterKey();

    // Connections opened by tests set audit columns the same way as the App's, see Configure.Db.cs
    [OneTimeSetUp]
    public void RegisterAuditRules()
    {
        SqliteDialect.Provider.OnOpenConnection = db => db.WithAuditRules();
        PostgreSqlDialect.Provider.OnOpenConnection = db => db.WithAuditRules();
    }
}

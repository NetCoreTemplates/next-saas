using NUnit.Framework;

namespace MyApp.Tests;

[SetUpFixture]
public sealed class AssemblySetup
{
    [OneTimeSetUp]
    public void RegisterServiceStackLicense() => AppHost.RegisterKey();
}

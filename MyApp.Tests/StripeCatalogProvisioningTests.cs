using MyApp.ServiceInterface;
using MyApp.ServiceModel;
using NUnit.Framework;
using ServiceStack;

namespace MyApp.Tests;

public class StripeCatalogProvisioningTests
{
    [TestCase("", false)]
    [TestCase("sk_test_x", true)]
    [TestCase("sk_live_x", false)]
    [TestCase("rk_live_x", false)]
    public void Catalog_provisioning_defaults_are_safe(string secretKey, bool expected)
    {
        var gateway = Gateway(new StripeConfig { SecretKey = secretKey, EnableCatalogProvisioning = true });

        Assert.That(gateway.IsCatalogProvisioningEnabled, Is.EqualTo(expected));
    }

    [Test]
    public void Live_catalog_creation_requires_explicit_opt_in()
    {
        var gateway = Gateway(new StripeConfig {
            SecretKey = "sk_live_x",
            EnableCatalogProvisioning = true,
            AllowLiveCatalogProvisioning = false,
        });

        var error = Assert.ThrowsAsync<HttpError>(() => gateway.ProvisionCatalogAsync(
            new SaasPlan { Id = "plan.pro", Code = "pro" },
            new ProvisionSaasPlanStripeCatalog {
                PlanId = "plan.pro", Name = "Pro", Description = "Pro plan",
                Prices = [new SavePlanPrice { Currency = "usd", Interval = BillingInterval.Month, UnitAmount = 4900 }],
            }));

        Assert.That(error!.ErrorCode, Is.EqualTo("StripeLiveCatalogProvisioningDisabled"));
    }

    private static StripeBillingGateway Gateway(StripeConfig config) => new(config, new SaasConfig(), new ProductConfig());
}

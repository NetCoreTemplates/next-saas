using System.Data;
using System.Text.Json;
using MyApp.ServiceInterface;
using MyApp.ServiceModel;
using ServiceStack.Data;
using ServiceStack.OrmLite;
using Stripe;
using Checkout = Stripe.Checkout;
using BillingPortal = Stripe.BillingPortal;

[assembly: HostingStartup(typeof(MyApp.ConfigureSaas))]

namespace MyApp;

public class ConfigureSaas : IHostingStartup
{
    public void Configure(IWebHostBuilder builder) => builder.ConfigureServices((context, services) =>
    {
        var defaults = new SaasConfig();
        var saas = new SaasConfig {
            SupportedCurrencies = [], QuotaWarningPercentages = [], Features = [], Meters = [],
        };
        context.Configuration.GetSection("Saas").Bind(saas);
        if (saas.SupportedCurrencies.Count == 0) saas.SupportedCurrencies = defaults.SupportedCurrencies;
        if (saas.QuotaWarningPercentages.Count == 0) saas.QuotaWarningPercentages = defaults.QuotaWarningPercentages;
        if (saas.Features.Count == 0) saas.Features = defaults.Features;
        if (saas.Meters.Count == 0) saas.Meters = defaults.Meters;
        AssertValid(saas);
        var stripe = context.Configuration.GetSection("Stripe").Get<StripeConfig>() ?? new StripeConfig();
        var product = context.Configuration.GetSection("Product").Get<ProductConfig>() ?? new ProductConfig();
        var notifications = context.Configuration.GetSection("Notifications").Get<NotificationConfig>() ?? new NotificationConfig();
        var deployment = context.Configuration.GetSection("Deployment").Get<DeploymentConfig>() ?? new DeploymentConfig();
        var app = context.Configuration.GetSection("AppConfig").Get<AppConfig>() ?? new AppConfig();
        if (notifications.Provider == EmailProvider.Smtp && context.Configuration.GetSection(nameof(SmtpConfig)).Get<SmtpConfig>() == null)
            throw new InvalidOperationException("Notifications.Provider is Smtp but the SmtpConfig section is missing.");

        if (string.IsNullOrEmpty(stripe.SecretKey))
        {
            stripe.SecretKey = Environment.GetEnvironmentVariable("STRIPE_SECRET_KEY") ?? "";
        }

        if (context.HostingEnvironment.IsProduction() && deployment.EnforceStartupChecks)
        {
            var readiness = ProductionReadiness.Evaluate(context.Configuration, deployment, app, product, notifications, stripe);
            if (!readiness.IsReady)
                throw new InvalidOperationException("Production configuration is incomplete:\n - " + string.Join("\n - ", readiness.Errors));
        }

        services.AddSingleton(saas);
        services.AddSingleton(stripe);
        services.AddSingleton(product);
        services.AddSingleton(notifications);
        services.AddSingleton(deployment);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ISaasManager, SaasManager>();
        services.AddSingleton<IEntitlementResolver, EntitlementResolver>();
        services.AddSingleton<IWorkspaceContextResolver, WorkspaceContextResolver>();
        services.AddSingleton<IAccountDeletionManager, AccountDeletionManager>();
        services.AddSingleton<INotificationManager, NotificationManager>();
        services.AddSingleton<IStripeBillingGateway, StripeBillingGateway>();
    }).ConfigureAppHost(appHost =>
    {
        appHost.GlobalRequestFilters.Add((request, response, dto) =>
        {
            var requirements = dto.GetType().GetCustomAttributes(typeof(RequiresFeatureAttribute), true)
                .Cast<RequiresFeatureAttribute>().ToList();
            if (requirements.Count == 0) return;

            var session = request.GetSession();
            if (!session.IsAuthenticated || session.UserAuthId.IsNullOrEmpty()) return;
            using var db = appHost.Resolve<IDbConnectionFactory>().Open();
            var context = appHost.Resolve<IWorkspaceContextResolver>().Resolve(db, session, request);
            var subscription = db.Single<BillingSubscription>(x => x.WorkspaceId == context.Workspace.Id)
                ?? throw new HttpError(409, "SubscriptionNotFound", "The organization does not have a subscription projection.");
            var entitlements = appHost.Resolve<IEntitlementResolver>();
            foreach (var requirement in requirements)
            {
                if (!entitlements.HasFeature(db, context.Workspace, subscription, requirement.FeatureKey))
                    throw new HttpError(403, "FeatureNotEntitled",
                        $"Feature '{requirement.FeatureKey}' is not included in the current plan.");
            }
        });
    });

    private static void AssertValid(SaasConfig config)
    {
        static void AssertUnique(IEnumerable<string> values, string label)
        {
            var duplicate = values.GroupBy(x => x, StringComparer.OrdinalIgnoreCase).FirstOrDefault(x => x.Count() > 1)?.Key;
            if (duplicate != null) throw new InvalidOperationException($"Duplicate {label} key '{duplicate}' in SaaS configuration.");
        }
        AssertUnique(config.Features.Select(x => x.Key), "feature");
        AssertUnique(config.Meters.Select(x => x.Key), "meter");
        if (config.Features.Any(x => x.Key.IsNullOrEmpty())) throw new InvalidOperationException("Every SaaS feature requires a stable key.");
        if (config.Meters.Any(x => x.Key.IsNullOrEmpty())) throw new InvalidOperationException("Every SaaS meter requires a stable key.");
        if (config.PastDueGraceDays < 0 || config.WorkspaceDeletionDelayDays < 0 || config.ExportExpiryDays < 1 || config.ApiKeyRequestsPerMinute < 1 ||
            config.AnalyticsRetentionDays < 1 || config.AuditRetentionDays < 1 || config.NotificationRetentionDays < 1 ||
            config.DeletedFileRetentionDays < 1 || config.LifecycleHistoryRetentionDays < 1 || config.RetentionBatchSize is < 1 or > 10000)
            throw new InvalidOperationException("SaaS lifecycle durations are invalid.");
    }
}

public class StripeBillingGateway(StripeConfig config, SaasConfig saas, ProductConfig productConfig) : IStripeBillingGateway
{
    public bool IsConfigured => !config.SecretKey.IsNullOrEmpty();
    public bool IsLiveMode => config.SecretKey.StartsWith("sk_live_", StringComparison.OrdinalIgnoreCase) ||
        config.SecretKey.StartsWith("rk_live_", StringComparison.OrdinalIgnoreCase);
    public bool IsCatalogProvisioningEnabled => config.EnableCatalogProvisioning && IsConfigured &&
        (!IsLiveMode || config.AllowLiveCatalogProvisioning);

    private StripeClient Client => IsConfigured
        ? new StripeClient(config.SecretKey)
        : throw new HttpError(503, "StripeNotConfigured", "Set Stripe__SecretKey before using paid billing.");

    public async Task<ProvisionSaasPlanStripeCatalogResponse> ProvisionCatalogAsync(SaasPlan plan, string name,
        string description, IReadOnlyCollection<SavePlanPrice> prices, CancellationToken token = default)
    {
        if (!config.EnableCatalogProvisioning)
            throw new HttpError(409, "StripeCatalogProvisioningDisabled", "Enable Stripe.EnableCatalogProvisioning before creating catalog objects.");
        if (IsLiveMode && !config.AllowLiveCatalogProvisioning)
            throw new HttpError(409, "StripeLiveCatalogProvisioningDisabled", "Automatic live Stripe catalog creation is disabled. Enable Stripe.AllowLiveCatalogProvisioning explicitly to proceed.");

        var missing = prices
            .Where(x => x.IsActive && x.UnitAmount > 0 && x.StripePriceId.IsNullOrEmpty())
            .ToList();
        var managedMetadata = new Dictionary<string, string> {
            ["managedBy"] = "next-saas",
            ["planId"] = plan.Id,
            ["planCode"] = plan.Code,
        };
        var productService = new ProductService(Client);
        Product? stripeProduct = null;
        await foreach (var candidate in productService.ListAutoPagingAsync(
            new ProductListOptions { Active = true, Limit = 100 }, cancellationToken: token))
        {
            if (candidate.Metadata.TryGetValue("managedBy", out var managedBy) && managedBy == "next-saas" &&
                candidate.Metadata.TryGetValue("planId", out var planId) && planId == plan.Id)
            {
                stripeProduct = candidate;
                break;
            }
        }
        var productCreated = stripeProduct == null;
        if (stripeProduct == null)
        {
            var organization = productConfig.OrganizationName.Trim();
            var productName = name.Trim();
            if (!organization.IsNullOrEmpty() && !productName.StartsWith(organization + " ", StringComparison.OrdinalIgnoreCase))
                productName = $"{organization} {productName}";
            stripeProduct = await productService.CreateAsync(new ProductCreateOptions {
                Name = productName,
                Description = description.Trim(),
                Metadata = managedMetadata,
            }, new RequestOptions { IdempotencyKey = $"next-saas-product-{plan.Id}" }, token);
        }

        var priceService = new PriceService(Client);
        var existing = new List<Price>();
        await foreach (var candidate in priceService.ListAutoPagingAsync(new PriceListOptions {
            Active = true, Product = stripeProduct.Id, Limit = 100,
        }, cancellationToken: token))
            existing.Add(candidate);
        var mappings = new List<StripeCatalogPriceMapping>();
        foreach (var price in missing)
        {
            var currency = price.Currency.Trim().ToLowerInvariant();
            var interval = price.Interval == BillingInterval.Year ? "year" : "month";
            var match = existing.FirstOrDefault(x =>
                x.Currency.Equals(currency, StringComparison.OrdinalIgnoreCase) &&
                x.UnitAmount == price.UnitAmount &&
                x.Recurring?.Interval == interval &&
                x.Metadata.TryGetValue("planId", out var planId) && planId == plan.Id);
            var created = match == null;
            if (match == null)
            {
                var metadata = new Dictionary<string, string>(managedMetadata) {
                    ["billingInterval"] = interval,
                    ["unitAmount"] = price.UnitAmount.ToString(),
                };
                match = await priceService.CreateAsync(new PriceCreateOptions {
                    Product = stripeProduct.Id,
                    Currency = currency,
                    UnitAmount = price.UnitAmount,
                    Recurring = new PriceRecurringOptions { Interval = interval },
                    Nickname = $"{name.Trim()} {interval}ly",
                    Metadata = metadata,
                }, new RequestOptions {
                    IdempotencyKey = $"next-saas-price-{plan.Id}-{currency}-{interval}-{price.UnitAmount}",
                }, token);
            }
            mappings.Add(new StripeCatalogPriceMapping {
                Currency = currency,
                Interval = price.Interval,
                UnitAmount = price.UnitAmount,
                StripePriceId = match.Id,
                Created = created,
            });
        }

        return new ProvisionSaasPlanStripeCatalogResponse {
            StripeProductId = stripeProduct.Id,
            Livemode = stripeProduct.Livemode,
            ProductCreated = productCreated,
            Prices = mappings,
        };
    }

    public async Task<List<SaasCouponInfo>> GetCouponsAsync(CancellationToken token = default)
    {
        if (!IsConfigured) return [];

        var couponTask = new CouponService(Client).ListAsync(new CouponListOptions { Limit = 100 }, cancellationToken: token);
        var promotionTask = new PromotionCodeService(Client).ListAsync(new PromotionCodeListOptions { Limit = 100 }, cancellationToken: token);
        await Task.WhenAll(couponTask, promotionTask);
        var coupons = couponTask.Result.ToDictionary(x => x.Id);

        return promotionTask.Result
            .Select(promotion => coupons.TryGetValue(promotion.Promotion?.CouponId ?? "", out var coupon)
                ? ToCouponInfo(promotion, coupon)
                : null)
            .Where(x => x != null)
            .OrderByDescending(x => x!.CreatedDate)
            .Cast<SaasCouponInfo>()
            .ToList();
    }

    public async Task<SaasCouponInfo> CreateCouponAsync(CreateSaasCoupon request, CancellationToken token = default)
    {
        var coupons = new CouponService(Client);
        var coupon = await coupons.CreateAsync(new CouponCreateOptions {
            Name = request.Name.Trim(),
            PercentOff = request.PercentOff,
            AmountOff = request.AmountOff,
            Currency = request.AmountOff != null ? request.Currency?.Trim().ToLowerInvariant() : null,
            Duration = request.Duration.ToString().ToLowerInvariant(),
            DurationInMonths = request.Duration == CouponDuration.Repeating ? request.DurationInMonths : null,
            Metadata = new Dictionary<string, string> { ["managedBy"] = "next-saas" },
        }, cancellationToken: token);

        try
        {
            var promotion = await new PromotionCodeService(Client).CreateAsync(new PromotionCodeCreateOptions {
                Code = request.Code.Trim().ToUpperInvariant(),
                Active = true,
                ExpiresAt = request.ExpiresAt,
                MaxRedemptions = request.MaxRedemptions,
                Promotion = new PromotionCodePromotionOptions { Type = "coupon", Coupon = coupon.Id },
                Restrictions = new PromotionCodeRestrictionsOptions { FirstTimeTransaction = request.FirstTimeTransaction },
                Metadata = new Dictionary<string, string> { ["managedBy"] = "next-saas" },
            }, cancellationToken: token);
            return ToCouponInfo(promotion, coupon);
        }
        catch
        {
            try { await coupons.DeleteAsync(coupon.Id, cancellationToken: token); } catch { /* Preserve the original Stripe error. */ }
            throw;
        }
    }

    public async Task<SaasCouponInfo> DeactivateCouponAsync(string promotionCodeId, CancellationToken token = default)
    {
        var promotions = new PromotionCodeService(Client);
        var promotion = await promotions.UpdateAsync(promotionCodeId,
            new PromotionCodeUpdateOptions { Active = false }, cancellationToken: token);
        var couponId = promotion.Promotion?.CouponId
            ?? throw new HttpError(409, "StripeCouponNotFound", "The promotion code is not linked to a Stripe coupon.");
        var coupon = await new CouponService(Client).GetAsync(couponId, cancellationToken: token);
        return ToCouponInfo(promotion, coupon);
    }

    public async Task<string> CreateCheckoutAsync(Workspace workspace, SaasPlanPrice price, string successUrl,
        string cancelUrl, int? trialDays, CancellationToken token = default)
    {
        var effectiveTrialDays = saas.EnableTrials && trialDays is > 0 ? trialDays : null;
        var metadata = new Dictionary<string, string> { ["workspaceId"] = workspace.Id, ["planVersionId"] = price.PlanVersionId };
        var options = new Checkout.SessionCreateOptions {
            Mode = "subscription",
            SuccessUrl = successUrl,
            CancelUrl = cancelUrl,
            ClientReferenceId = workspace.Id,
            Customer = workspace.StripeCustomerId,
            CustomerEmail = workspace.StripeCustomerId.IsNullOrEmpty() ? workspace.BillingEmail : null,
            PaymentMethodCollection = effectiveTrialDays != null && !saas.TrialRequiresPaymentMethod ? "if_required" : "always",
            AllowPromotionCodes = true,
            LineItems = [new Checkout.SessionLineItemOptions { Price = price.StripePriceId, Quantity = 1 }],
            Metadata = metadata,
            SubscriptionData = new Checkout.SessionSubscriptionDataOptions {
                Metadata = metadata,
                TrialPeriodDays = effectiveTrialDays,
                TrialSettings = effectiveTrialDays != null && !saas.TrialRequiresPaymentMethod
                    ? new Checkout.SessionSubscriptionDataTrialSettingsOptions {
                        EndBehavior = new Checkout.SessionSubscriptionDataTrialSettingsEndBehaviorOptions {
                            MissingPaymentMethod = "cancel",
                        },
                    }
                    : null,
            },
        };
        var session = await new Checkout.SessionService(Client).CreateAsync(options, cancellationToken: token);
        return session.Url;
    }

    public async Task<bool> ConfirmCheckoutAsync(IDbConnection db, Workspace workspace, string? sessionId,
        CancellationToken token = default)
    {
        var sessions = new Checkout.SessionService(Client);
        Checkout.Session? checkout = null;
        if (!sessionId.IsNullOrEmpty())
        {
            checkout = await sessions.GetAsync(sessionId, cancellationToken: token);
        }
        else
        {
            await foreach (var candidate in sessions.ListAutoPagingAsync(
                new Checkout.SessionListOptions { Limit = 100 }, cancellationToken: token))
            {
                var candidateWorkspaceId = candidate.Metadata.TryGetValue("workspaceId", out var candidateValue) ? candidateValue : null;
                if (candidate.ClientReferenceId == workspace.Id && candidateWorkspaceId == workspace.Id && candidate.Status == "complete")
                {
                    checkout = candidate;
                    break;
                }
            }
        }
        if (checkout == null)
            throw new HttpError(404, "CheckoutSessionNotFound", "No completed Stripe Checkout Session was found for this organization.");

        var metadataWorkspaceId = checkout.Metadata.TryGetValue("workspaceId", out var checkoutValue) ? checkoutValue : null;
        if (checkout.ClientReferenceId != workspace.Id || metadataWorkspaceId != workspace.Id)
            throw new HttpError(403, "CheckoutWorkspaceMismatch", "This Checkout Session belongs to a different organization.");
        if (checkout.Status != "complete" || checkout.Mode != "subscription" || checkout.SubscriptionId.IsNullOrEmpty())
            return false;

        var remote = await new SubscriptionService(Client).GetAsync(checkout.SubscriptionId, cancellationToken: token);
        if (remote.CustomerId != checkout.CustomerId)
            throw new HttpError(409, "CheckoutSubscriptionMismatch", "Stripe returned an inconsistent Checkout subscription.");

        if (!checkout.CustomerId.IsNullOrEmpty() && workspace.StripeCustomerId != checkout.CustomerId)
        {
            workspace.StripeCustomerId = checkout.CustomerId;
            workspace.ModifiedDate = DateTime.UtcNow;
            workspace.ModifiedBy = "stripe-checkout";
            db.Update(workspace);
        }

        ApplySubscription(db, workspace, remote);
        if (!db.Exists<SaasAuditEvent>(x => x.Category == "billing" && x.SubjectId == checkout.Id))
        {
            db.Insert(new SaasAuditEvent {
                WorkspaceId = workspace.Id,
                Category = "billing",
                Action = "checkout.session.confirmed",
                ActorId = "stripe",
                SubjectId = checkout.Id,
                CreatedDate = DateTime.UtcNow,
            });
        }
        return true;
    }

    private static SaasCouponInfo ToCouponInfo(PromotionCode promotion, Coupon coupon) => new() {
        PromotionCodeId = promotion.Id,
        CouponId = coupon.Id,
        Code = promotion.Code,
        Name = coupon.Name ?? promotion.Code,
        PercentOff = coupon.PercentOff,
        AmountOff = coupon.AmountOff,
        Currency = coupon.Currency,
        Duration = coupon.Duration switch {
            "forever" => CouponDuration.Forever,
            "repeating" => CouponDuration.Repeating,
            _ => CouponDuration.Once,
        },
        DurationInMonths = coupon.DurationInMonths,
        Active = promotion.Active,
        Valid = coupon.Valid && promotion.Active && (promotion.ExpiresAt == null || promotion.ExpiresAt > DateTime.UtcNow),
        MaxRedemptions = promotion.MaxRedemptions,
        TimesRedeemed = promotion.TimesRedeemed,
        ExpiresAt = promotion.ExpiresAt,
        FirstTimeTransaction = promotion.Restrictions?.FirstTimeTransaction ?? false,
        CreatedDate = promotion.Created,
        Livemode = promotion.Livemode,
    };

    public async Task<string> CreatePortalAsync(Workspace workspace, string returnUrl, CancellationToken token = default)
    {
        if (workspace.StripeCustomerId.IsNullOrEmpty())
            throw new HttpError(409, "StripeCustomerNotFound", "Start a paid subscription before opening the billing portal.");
        var options = new BillingPortal.SessionCreateOptions {
            Customer = workspace.StripeCustomerId,
            ReturnUrl = returnUrl,
            Configuration = string.IsNullOrEmpty(config.PortalConfigurationId) ? null : config.PortalConfigurationId,
        };
        var session = await new BillingPortal.SessionService(Client).CreateAsync(options, cancellationToken: token);
        return session.Url;
    }

    public async Task<bool> ReconcileSubscriptionAsync(IDbConnection db, Workspace workspace, CancellationToken token = default)
    {
        var local = db.Single<BillingSubscription>(x => x.WorkspaceId == workspace.Id)
            ?? throw new HttpError(404, "SubscriptionNotFound", "The organization subscription was not found.");
        if (local.StripeSubscriptionId.IsNullOrEmpty()) return false;
        var remote = await new SubscriptionService(Client).GetAsync(local.StripeSubscriptionId, cancellationToken: token);
        ApplySubscription(db, workspace, remote);
        return true;
    }

    public StripeWebhookEnvelope ValidateWebhook(string payload, string signature)
    {
        if (config.WebhookSecret.IsNullOrEmpty())
            throw new HttpError(503, "StripeWebhookNotConfigured", "Set Stripe__WebhookSecret before accepting webhooks.");
        try
        {
            var evt = EventUtility.ConstructEvent(payload, signature, config.WebhookSecret, throwOnApiVersionMismatch: false);
            return new StripeWebhookEnvelope(evt.Id, evt.Type, payload);
        }
        catch (StripeException ex)
        {
            throw new HttpError(400, "InvalidStripeSignature", ex.Message);
        }
    }

    public Task ApplyWebhookAsync(IDbConnection db, StripeEventInbox inbox, CancellationToken token = default)
    {
        using var document = JsonDocument.Parse(inbox.PayloadJson);
        var obj = document.RootElement.GetProperty("data").GetProperty("object");
        var customerId = String(obj, "customer");
        var workspaceId = Metadata(obj, "workspaceId");
        var workspace = !workspaceId.IsNullOrEmpty() ? db.SingleById<Workspace>(workspaceId)
            : !customerId.IsNullOrEmpty() ? db.Single<Workspace>(x => x.StripeCustomerId == customerId) : null;
        if (workspace == null) return Task.CompletedTask;

        if (!customerId.IsNullOrEmpty() && workspace.StripeCustomerId != customerId)
        {
            workspace.StripeCustomerId = customerId;
            workspace.ModifiedDate = DateTime.UtcNow;
            workspace.ModifiedBy = "stripe";
            db.Update(workspace);
        }

        if (inbox.EventType == "checkout.session.completed")
        {
            var subscriptionId = String(obj, "subscription");
            var subscription = db.Single<BillingSubscription>(x => x.WorkspaceId == workspace.Id);
            subscription.StripeSubscriptionId = subscriptionId;
            subscription.ModifiedDate = DateTime.UtcNow;
            subscription.ModifiedBy = "stripe";
            db.Update(subscription);
        }
        else if (inbox.EventType.StartsWith("customer.subscription."))
        {
            ApplySubscription(db, workspace, obj);
        }
        else if (inbox.EventType == "invoice.payment_failed")
        {
            var subscription = db.Single<BillingSubscription>(x => x.WorkspaceId == workspace.Id);
            subscription.Status = SubscriptionStatus.PastDue;
            subscription.StripeStatus = "past_due";
            subscription.GraceEnd = DateTime.UtcNow.AddDays(saas.PastDueGraceDays);
            subscription.ModifiedDate = DateTime.UtcNow;
            subscription.ModifiedBy = "stripe";
            db.Update(subscription);
        }
        else if (inbox.EventType == "invoice.paid")
        {
            var subscription = db.Single<BillingSubscription>(x => x.WorkspaceId == workspace.Id);
            if (subscription.StripeSubscriptionId != null)
                subscription.Status = SubscriptionStatus.Active;
            subscription.StripeStatus = "active";
            subscription.GraceEnd = null;
            subscription.ModifiedDate = DateTime.UtcNow;
            subscription.ModifiedBy = "stripe";
            db.Update(subscription);
        }

        db.Insert(new SaasAuditEvent {
            WorkspaceId = workspace.Id, Category = "billing", Action = inbox.EventType,
            ActorId = "stripe", SubjectId = inbox.StripeEventId, CreatedDate = DateTime.UtcNow,
        });
        return Task.CompletedTask;
    }

    private static void ApplySubscription(IDbConnection db, Workspace workspace, JsonElement obj)
    {
        var local = db.Single<BillingSubscription>(x => x.WorkspaceId == workspace.Id);
        var priceId = NestedString(obj, "items", "data", 0, "price", "id");
        var mappedPrice = priceId.IsNullOrEmpty() ? null : db.Single<SaasPlanPrice>(x => x.StripePriceId == priceId);
        local.StripeSubscriptionId = String(obj, "id");
        local.StripePriceId = priceId;
        if (mappedPrice != null)
        {
            local.PlanVersionId = mappedPrice.PlanVersionId;
            local.Interval = mappedPrice.Interval;
        }
        local.StripeStatus = String(obj, "status");
        local.Status = local.StripeStatus switch {
            "trialing" => SubscriptionStatus.Trialing,
            "active" => SubscriptionStatus.Active,
            "past_due" or "unpaid" => SubscriptionStatus.PastDue,
            "paused" => SubscriptionStatus.Paused,
            "canceled" => SubscriptionStatus.Canceled,
            _ => local.Status,
        };
        local.PeriodStart = UnixDate(obj, "current_period_start") ?? NestedUnixDate(obj, "items", "data", 0, "current_period_start") ?? local.PeriodStart;
        local.PeriodEnd = UnixDate(obj, "current_period_end") ?? NestedUnixDate(obj, "items", "data", 0, "current_period_end") ?? local.PeriodEnd;
        local.TrialEnd = UnixDate(obj, "trial_end");
        local.CancelAt = UnixDate(obj, "cancel_at");
        local.ModifiedDate = DateTime.UtcNow;
        local.ModifiedBy = "stripe";
        db.Update(local);
    }

    private static void ApplySubscription(IDbConnection db, Workspace workspace, Subscription remote)
    {
        var local = db.Single<BillingSubscription>(x => x.WorkspaceId == workspace.Id);
        var item = remote.Items?.Data.FirstOrDefault();
        var priceId = item?.Price?.Id;
        var mappedPrice = priceId.IsNullOrEmpty() ? null : db.Single<SaasPlanPrice>(x => x.StripePriceId == priceId);
        local.StripeSubscriptionId = remote.Id;
        local.StripePriceId = priceId;
        if (mappedPrice != null)
        {
            local.PlanVersionId = mappedPrice.PlanVersionId;
            local.Interval = mappedPrice.Interval;
        }
        local.StripeStatus = remote.Status;
        local.Status = remote.Status switch {
            "trialing" => SubscriptionStatus.Trialing,
            "active" => SubscriptionStatus.Active,
            "past_due" or "unpaid" => SubscriptionStatus.PastDue,
            "paused" => SubscriptionStatus.Paused,
            "canceled" => SubscriptionStatus.Canceled,
            _ => local.Status,
        };
        local.PeriodStart = item?.CurrentPeriodStart ?? local.PeriodStart;
        local.PeriodEnd = item?.CurrentPeriodEnd ?? local.PeriodEnd;
        local.TrialEnd = remote.TrialEnd;
        local.CancelAt = remote.CancelAt;
        local.ModifiedDate = DateTime.UtcNow;
        local.ModifiedBy = "stripe-checkout";
        db.Update(local);
    }

    private static string? String(JsonElement value, string name) => value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null;
    private static string? Metadata(JsonElement value, string key) => value.TryGetProperty("metadata", out var metadata) ? String(metadata, key) : null;
    private static DateTime? UnixDate(JsonElement value, string name) => value.TryGetProperty(name, out var property) && property.TryGetInt64(out var seconds) ? DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime : null;
    private static string? NestedString(JsonElement value, string a, string b, int index, string c, string d)
    {
        try { return String(value.GetProperty(a).GetProperty(b)[index].GetProperty(c), d); } catch { return null; }
    }
    private static DateTime? NestedUnixDate(JsonElement value, string a, string b, int index, string name)
    {
        try { return UnixDate(value.GetProperty(a).GetProperty(b)[index], name); } catch { return null; }
    }
}

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using MyApp.ServiceInterface;
using MyApp.ServiceModel;
using ServiceStack;
using ServiceStack.Auth;
using ServiceStack.Data;
using ServiceStack.OrmLite;
using ServiceStack.Web;

[assembly: HostingStartup(typeof(MyApp.ConfigureApiKeys))]

namespace MyApp;

public class ConfigureApiKeys : IHostingStartup
{
    public void Configure(IWebHostBuilder builder) => builder
        .ConfigureServices(services => {
            services.AddSingleton<SaasApiRateLimiter>();
            services.AddPlugin(new ApiKeysFeature {
                Scopes = [..SaasApiKeys.Scopes],
                UserScopes = [..SaasApiKeys.Scopes],
            });
        })
        .ConfigureAppHost(appHost =>
        {
            appHost.GlobalRequestFilters.Add((request, response, dto) =>
                SaasApiKeyGuard.ValidateRequest(appHost, request, dto));
            appHost.GlobalResponseFilters.Add((request, response, dto) =>
                SaasApiKeyGuard.FilterResponse(appHost, request, dto));

            // API keys are created for an organization by its members, see ExampleDataSeeder for an example key
            using var db = appHost.Resolve<IDbConnectionFactory>().OpenAcrossWorkspaces(SaasDb.SystemUserId);
            appHost.GetPlugin<ApiKeysFeature>().InitSchema(db);
        });
}

internal static class SaasApiKeyGuard
{
    public static void ValidateRequest(ServiceStackHost appHost, IRequest request, object dto)
    {
        var suppliedKey = SaasApiKeys.GetSuppliedApiKey(request);
        if (!suppliedKey.IsNullOrEmpty())
        {
            // The API key that authenticated the request, see AddApiKeyAuth(). Requests whose key wasn't accepted
            // have none, and are limited by their IP address.
            var authenticatedKey = request.GetApiKey();
            if (!appHost.Resolve<SaasApiRateLimiter>().TryAcquire(authenticatedKey?.RefIdStr, suppliedKey!, request.RemoteIp ?? "",
                    DateTime.UtcNow, out var retryAfterSeconds))
            {
                SaasTelemetry.RateLimitRejected.Add(1);
                request.Response.AddHeader("Retry-After", retryAfterSeconds.ToString());
                throw new HttpError(429, "RateLimitExceeded", "This API credential has exceeded its short-term request limit.");
            }
        }

        // A request authenticated with an API key can only call APIs that need a scope, which [ValidateHasScope]
        // checks the key has. Everything else needs a signed-in user: managing credentials, members, billing and
        // the organization.
        if (request.GetClaimsPrincipal().IsApiKeyUser())
        {
            // The request works in the organization its API key was created for, see SaasDb.ForRequest()
            if (request.GetApiKey()?.RefIdStr.IsNullOrEmpty() != false)
                throw new HttpError(403, "OrganizationKeyRequired", "This API key is not assigned to an organization. Create a new API key.");

            if (!dto.GetType().IsDefined(typeof(ValidateHasScopeAttribute), true))
                throw new HttpError(403, "InteractiveSessionRequired", "This API cannot be called with an API key.");
        }

        if (dto is not (QueryUserApiKeys or CreateUserApiKey or UpdateUserApiKey or DeleteUserApiKey)) return;

        var userId = request.GetUserId();
        if (userId.IsNullOrEmpty()) return;

        // Lists the user's own API keys, of every organization they have one for
        if (dto is QueryUserApiKeys) return;

        // The API Keys feature uses the request's connection for its own APIs. Confining it to the key's
        // organization, once the user is checked to be a member of it, means only that organization's keys can
        // be created, updated and deleted.
        using var db = appHost.GetDbConnection(request);
        var manager = appHost.Resolve<ISaasManager>();

        // An API key is created for an organization, which the request says with its RefIdStr
        if (dto is CreateUserApiKey create)
        {
            if (create.RefIdStr.IsNullOrEmpty())
                throw new HttpError(400, "OrganizationRequired", "The request needs the RefIdStr of the organization the API key is for.");
            var (workspace, member) = manager.AssertMembership(db, create.RefIdStr, WorkspaceAccess.Write);
            db.GetWorkspaceScope().Confine(workspace, member);

            var subscription = db.GetSubscription();
            if (!appHost.Resolve<IEntitlementResolver>().HasFeature(db, workspace, subscription, "api.access"))
                throw new HttpError(403, "FeatureNotEntitled", "API access is not included in the current plan.");
            create.RefId = null;
            return;
        }

        // An API key is updated and deleted in the organization it was created for.
        // Keys aren't filtered until the connection is confined, so the user's key is found in any organization.
        var id = dto is UpdateUserApiKey update ? update.Id : ((DeleteUserApiKey)dto).Id;
        var apiKey = id == null ? null : db.SingleById<ApiKeysFeature.ApiKey>(id.Value);
        if (apiKey == null || apiKey.UserId != userId || apiKey.RefIdStr.IsNullOrEmpty())
            throw new HttpError(404, "ApiKeyNotFound", "The API key was not found.");

        var (keyWorkspace, keyMember) = manager.AssertMembership(db, apiKey.RefIdStr!, WorkspaceAccess.Write);
        db.GetWorkspaceScope().Confine(keyWorkspace, keyMember);

        // An API key can't be moved to another organization
        if (dto is UpdateUserApiKey edit)
        {
            edit.RefId = null;
            edit.RefIdStr = apiKey.RefIdStr;
            edit.Reset?.RemoveAll(x => x.Equals("refId", StringComparison.OrdinalIgnoreCase) || x.Equals("refIdStr", StringComparison.OrdinalIgnoreCase));
        }
    }

    public static void FilterResponse(ServiceStackHost appHost, IRequest request, object dto)
    {
        if (request.Dto is not (CreateUserApiKey or UpdateUserApiKey or DeleteUserApiKey)) return;
        if (dto is IHttpError or Exception) return;
        var session = request.GetSession();
        if (!session.IsAuthenticated || session.UserAuthId.IsNullOrEmpty()) return;
        // The organization the request was confined to by ValidateRequest
        var workspaceId = request.GetWorkspaceId();
        if (workspaceId == null) return;


        var (action, subjectId) = request.Dto switch
        {
            CreateUserApiKey create => ("created", create.Name ?? "credential"),
            UpdateUserApiKey update => ("updated", update.Id.ToString()),
            DeleteUserApiKey delete => ("deleted", delete.Id?.ToString() ?? "credential"),
            _ => throw new InvalidOperationException(),
        };
        // The request's connection is confined to the organization, which it writes the event for
        using var db = appHost.GetDbConnection(request);
        db.Insert(new SaasAuditEvent
        {
            Category = "api-key",
            Action = action,
            UserId = session.UserAuthId!,
            SubjectId = subjectId,
            RequestId = request.GetHeader("X-Request-Id"),
            IpAddress = request.RemoteIp,
            UserAgent = request.UserAgent,
        });
    }
}

/// <summary>
/// A fixed one-minute window limit for API-key requests, applied to each credential and to each organization
/// across all of its credentials, so an organization can't raise its limit by creating more keys.
/// It's kept in memory, so each application instance has its own limits. Replace it with a distributed limiter
/// before running more than one instance.
/// </summary>
public class SaasApiRateLimiter(SaasConfig config)
{
    private sealed class Window
    {
        public DateTime StartedAt;
        public int Count;
    }

    private static readonly TimeSpan Period = TimeSpan.FromMinutes(1);
    private readonly ConcurrentDictionary<string, Window> windows = new(StringComparer.Ordinal);
    private readonly object sync = new();
    private DateTime lastPruned = DateTime.MinValue;

    public int WindowCount => windows.Count;

    /// <param name="workspaceId">The organization the credential is bound to, or null if it isn't a known credential</param>
    /// <param name="clientAddress">Used to limit requests with unknown credentials, which share one window per address</param>
    public bool TryAcquire(string? workspaceId, string credential, string clientAddress, DateTime now, out int retryAfterSeconds)
    {
        lock (sync)
        {
            Prune(now);

            // Unknown credentials don't get a window each, or random keys would grow this without bound
            if (workspaceId.IsNullOrEmpty())
                return TryAcquire([(Get($"unknown:{clientAddress}", now), config.ApiKeyRequestsPerMinute)], now, out retryAfterSeconds);

            var credentialHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(credential)));
            return TryAcquire([
                (Get($"key:{workspaceId}:{credentialHash}", now), config.ApiKeyRequestsPerMinute),
                (Get($"org:{workspaceId}", now), config.OrganizationApiRequestsPerMinute),
            ], now, out retryAfterSeconds);
        }
    }

    // A request counts against every window, and is only counted if all of them have capacity
    private static bool TryAcquire((Window Window, int Limit)[] limits, DateTime now, out int retryAfterSeconds)
    {
        retryAfterSeconds = 0;
        foreach (var (window, limit) in limits)
        {
            if (window.Count < limit) continue;
            retryAfterSeconds = Math.Max(retryAfterSeconds,
                Math.Max(1, (int)Math.Ceiling((window.StartedAt + Period - now).TotalSeconds)));
        }
        if (retryAfterSeconds > 0) return false;
        foreach (var (window, _) in limits)
            window.Count++;
        return true;
    }

    private Window Get(string key, DateTime now)
    {
        var window = windows.GetOrAdd(key, _ => new Window { StartedAt = now });
        if (now - window.StartedAt >= Period)
        {
            window.StartedAt = now;
            window.Count = 0;
        }
        return window;
    }

    private void Prune(DateTime now)
    {
        if (now - lastPruned < Period) return;
        lastPruned = now;
        foreach (var entry in windows)
        {
            if (now - entry.Value.StartedAt >= Period)
                windows.TryRemove(entry.Key, out _);
        }
    }
}

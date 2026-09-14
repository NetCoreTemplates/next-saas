using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using MyApp.Data;
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
                Scopes = ["usage:read", "usage:write", "workspace:read"],
                UserScopes = ["usage:read", "usage:write", "workspace:read"],
            });
        })
        .ConfigureAppHost(appHost =>
        {
            appHost.GlobalRequestFilters.Add((request, response, dto) =>
                SaasApiKeyGuard.ValidateRequest(appHost, request, dto));
            appHost.GlobalResponseFilters.Add((request, response, dto) =>
                SaasApiKeyGuard.FilterResponse(appHost, request, dto));

            using var db = appHost.Resolve<IDbConnectionFactory>().Open();
            var feature = appHost.GetPlugin<ApiKeysFeature>();
            feature.InitSchema(db);
            if (db.TableExists<UserWorkspacePreference>())
            {
                foreach (var apiKey in db.Select<ApiKeysFeature.ApiKey>(x => x.RefIdStr == null))
                {
                    var preference = db.SingleById<UserWorkspacePreference>(apiKey.UserId);
                    if (preference == null) continue;
                    apiKey.RefIdStr = preference.ActiveWorkspaceId;
                    db.Update(apiKey);
                }
            }

            if (feature.ApiKeyCount(db) == 0 && db.TableExists(IdentityUsers.TableName))
            {
                var users = IdentityUsers.GetByUserNames(db, ["admin@email.com"]);
                foreach (var user in users)
                {
                    var preference = db.TableExists<UserWorkspacePreference>()
                        ? db.SingleById<UserWorkspacePreference>(user.Id)
                        : null;
                    feature.Insert(db, new() {
                        Name = "Development key", UserId = user.Id, UserName = user.UserName,
                        RefIdStr = preference?.ActiveWorkspaceId,
                        Scopes = ["usage:read", "usage:write", "workspace:read"],
                    });
                }
            }
        });
}

internal static class SaasApiKeyGuard
{
    public static void ValidateRequest(ServiceStackHost appHost, IRequest request, object dto)
    {
        var suppliedKey = WorkspaceContextResolver.GetSuppliedApiKey(request);
        if (!suppliedKey.IsNullOrEmpty())
        {
            using var rateDb = appHost.Resolve<IDbConnectionFactory>().Open();
            var storedKey = rateDb.Single<ApiKeysFeature.ApiKey>(x => x.Key == suppliedKey);
            var organizationId = storedKey?.RefIdStr ?? "unknown";
            if (!appHost.Resolve<SaasApiRateLimiter>().TryAcquire(organizationId, suppliedKey!, DateTime.UtcNow, out var retryAfterSeconds))
            {
                SaasTelemetry.RateLimitRejected.Add(1);
                request.Response.AddHeader("Retry-After", retryAfterSeconds.ToString());
                throw new HttpError(429, "RateLimitExceeded", "This API credential has exceeded its short-term request limit.");
            }
        }

        if (dto is not (CreateUserApiKey or UpdateUserApiKey or DeleteUserApiKey)) return;
        if (WorkspaceContextResolver.IsApiKeyRequest(request))
            throw new HttpError(403, "InteractiveSessionRequired", "API keys cannot manage organization credentials.");

        var session = request.GetSession();
        if (!session.IsAuthenticated || session.UserAuthId.IsNullOrEmpty()) return;
        using var db = appHost.Resolve<IDbConnectionFactory>().Open();
        var preference = db.SingleById<UserWorkspacePreference>(session.UserAuthId!);
        var member = preference == null ? null : db.Single<WorkspaceMember>(x =>
            x.WorkspaceId == preference.ActiveWorkspaceId && x.UserId == session.UserAuthId && x.Status == WorkspaceMemberStatus.Active);
        if (member == null)
            throw new HttpError(403, "WorkspaceAccessDenied", "Select an organization before managing API keys.");

        if (dto is CreateUserApiKey create)
        {
            var workspace = db.SingleById<Workspace>(member.WorkspaceId);
            var subscription = db.Single<BillingSubscription>(x => x.WorkspaceId == member.WorkspaceId);
            if (workspace == null || subscription == null ||
                !appHost.Resolve<IEntitlementResolver>().HasFeature(db, workspace, subscription, "api.access"))
                throw new HttpError(403, "FeatureNotEntitled", "API access is not included in the current plan.");
            create.RefId = null;
            create.RefIdStr = member.WorkspaceId;
            return;
        }

        var id = dto is UpdateUserApiKey update ? update.Id : ((DeleteUserApiKey)dto).Id;
        var apiKey = id == null ? null : db.SingleById<ApiKeysFeature.ApiKey>(id.Value);
        if (apiKey == null || apiKey.UserId != session.UserAuthId || apiKey.RefIdStr != member.WorkspaceId)
            throw new HttpError(404, "ApiKeyNotFound", "The API key was not found in the active organization.");
        if (dto is UpdateUserApiKey edit)
        {
            edit.RefId = null;
            edit.RefIdStr = member.WorkspaceId;
            edit.Reset?.RemoveAll(x => x.Equals("refId", StringComparison.OrdinalIgnoreCase) || x.Equals("refIdStr", StringComparison.OrdinalIgnoreCase));
        }
    }

    public static void FilterResponse(ServiceStackHost appHost, IRequest request, object dto)
    {
        var session = request.GetSession();
        if (!session.IsAuthenticated || session.UserAuthId.IsNullOrEmpty()) return;
        using var db = appHost.Resolve<IDbConnectionFactory>().Open();
        var workspaceId = db.SingleById<UserWorkspacePreference>(session.UserAuthId!)?.ActiveWorkspaceId;

        if (!workspaceId.IsNullOrEmpty() && request.Dto is CreateUserApiKey or UpdateUserApiKey or DeleteUserApiKey)
        {
            var (action, subjectId) = request.Dto switch
            {
                CreateUserApiKey create => ("created", create.Name ?? "credential"),
                UpdateUserApiKey update => ("updated", update.Id.ToString()),
                DeleteUserApiKey delete => ("deleted", delete.Id?.ToString() ?? "credential"),
                _ => throw new InvalidOperationException(),
            };
            db.Insert(new SaasAuditEvent
            {
                WorkspaceId = workspaceId,
                Category = "api-key",
                Action = action,
                ActorId = session.UserAuthId!,
                SubjectId = subjectId,
                RequestId = request.GetHeader("X-Request-Id"),
                IpAddress = request.RemoteIp,
                UserAgent = request.UserAgent,
            });
        }

        if (request.Dto is not QueryUserApiKeys || dto is not UserApiKeysResponse apiKeys) return;
        apiKeys.Results = apiKeys.Results?.Where(x => x.RefIdStr == workspaceId).ToList();
    }
}

public class SaasApiRateLimiter(SaasConfig config)
{
    private sealed class Window
    {
        public readonly object Sync = new();
        public DateTime StartedAt { get; set; }
        public int Count { get; set; }
    }

    private readonly ConcurrentDictionary<string, Window> windows = new(StringComparer.Ordinal);

    public bool TryAcquire(string workspaceId, string credential, DateTime now, out int retryAfterSeconds)
    {
        var credentialHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(credential)));
        var key = $"{workspaceId}:{credentialHash}";
        var window = windows.GetOrAdd(key, _ => new Window { StartedAt = now });
        lock (window.Sync)
        {
            if (now - window.StartedAt >= TimeSpan.FromMinutes(1))
            {
                window.StartedAt = now;
                window.Count = 0;
            }
            if (window.Count >= config.ApiKeyRequestsPerMinute)
            {
                retryAfterSeconds = Math.Max(1, (int)Math.Ceiling((window.StartedAt.AddMinutes(1) - now).TotalSeconds));
                return false;
            }
            window.Count++;
            retryAfterSeconds = 0;
            return true;
        }
    }
}

using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using MyApp.ServiceInterface;
using AspNetHttpMethods = Microsoft.AspNetCore.Http.HttpMethods;

[assembly: HostingStartup(typeof(MyApp.ConfigureSecurity))]

namespace MyApp;

public class ConfigureSecurity : IHostingStartup
{
    public void Configure(IWebHostBuilder builder) => builder.ConfigureServices((context, services) =>
    {
        var config = context.Configuration.GetSection("Security").Get<SecurityConfig>() ?? new SecurityConfig();
        if (config.AuthenticationRequestsPerMinute < 1)
            throw new InvalidOperationException("Security.AuthenticationRequestsPerMinute must be greater than zero.");

        services.AddSingleton(config);
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
            {
                if (!WebSecurityPolicy.IsSensitiveAuthenticationRequest(httpContext.Request))
                    return RateLimitPartition.GetNoLimiter("unlimited");

                var address = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                var path = httpContext.Request.Path.Value?.ToLowerInvariant() ?? "unknown";
                return RateLimitPartition.GetFixedWindowLimiter($"{address}:{path}", _ =>
                    new FixedWindowRateLimiterOptions {
                        PermitLimit = config.AuthenticationRequestsPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    });
            });
            options.OnRejected = async (context, token) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    context.HttpContext.Response.Headers.RetryAfter =
                        Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                await context.HttpContext.Response.WriteAsJsonAsync(new {
                    responseStatus = new {
                        errorCode = "AuthenticationRateLimitExceeded",
                        message = "Too many authentication requests. Try again shortly.",
                    },
                }, token);
            };
        });
        services.AddTransient<IStartupFilter, SecurityStartupFilter>();
    });
}

public class SecurityStartupFilter(
    SecurityConfig config,
    AppConfig appConfig,
    IHostEnvironment environment) : IStartupFilter
{
    // Resolved once: the derivation is pure and the policy cannot change between requests.
    private readonly string toolingPolicy = !string.IsNullOrWhiteSpace(config.ToolingContentSecurityPolicy)
        ? config.ToolingContentSecurityPolicy
        : WebSecurityPolicy.WithUnsafeEval(config.ContentSecurityPolicy);

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, continuation) =>
        {
            if (config.EnableSecurityHeaders && !environment.IsDevelopment())
            {
                context.Response.OnStarting(() =>
                {
                    var headers = context.Response.Headers;
                    headers["X-Content-Type-Options"] = "nosniff";
                    headers["X-Frame-Options"] = "DENY";
                    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
                    headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
                    headers["Cross-Origin-Opener-Policy"] = "same-origin";
                    var policy = WebSecurityPolicy.IsToolingPath(context.Request.Path, config.ToolingPaths)
                        ? toolingPolicy
                        : config.ContentSecurityPolicy;
                    if (!string.IsNullOrWhiteSpace(policy))
                        headers["Content-Security-Policy"] = policy;
                    return Task.CompletedTask;
                });
            }

            if (config.RequireSameOriginForCookieApi &&
                WebSecurityPolicy.RequiresSameOriginValidation(context.Request) &&
                !WebSecurityPolicy.IsSameOrigin(context.Request, appConfig.BaseUrl))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new {
                    responseStatus = new {
                        errorCode = "CsrfValidationFailed",
                        message = "This cookie-authenticated request must originate from this application.",
                    },
                });
                return;
            }

            await continuation();
        });
        app.UseRateLimiter();
        next(app);
    };
}

public static class WebSecurityPolicy
{
    private static readonly HashSet<string> SensitiveAuthenticationPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/authenticate",
        "/api/register",
        "/identity/account/login",
        "/identity/account/register",
        "/identity/account/forgotpassword",
        "/identity/account/resetpassword",
        "/identity/account/resendemailconfirmation",
        "/identity/account/loginwith2fa",
        "/identity/account/loginwithrecoverycode",
        "/saas/invitations/accept",
    };

    /// <summary>
    /// True when the request targets one of ServiceStack's built-in operator UIs, which need a
    /// relaxed script-src. Uses segment matching so "/admin-uix" is not treated as "/admin-ui".
    /// </summary>
    public static bool IsToolingPath(PathString path, IEnumerable<string> toolingPaths)
    {
        foreach (var toolingPath in toolingPaths)
        {
            if (string.IsNullOrWhiteSpace(toolingPath)) continue;
            if (path.StartsWithSegments(toolingPath, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>
    /// Adds 'unsafe-eval' to the policy's script-src, leaving every other directive untouched.
    /// A policy with no script-src gains one, because otherwise default-src would still block eval.
    /// </summary>
    public static string WithUnsafeEval(string policy)
    {
        if (string.IsNullOrWhiteSpace(policy)) return policy;

        var directives = policy.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        for (var i = 0; i < directives.Count; i++)
        {
            var directive = directives[i];
            var name = directive.Split(' ', 2)[0];
            if (!name.Equals("script-src", StringComparison.OrdinalIgnoreCase)) continue;
            if (directive.Contains("'unsafe-eval'", StringComparison.OrdinalIgnoreCase)) return policy;
            directives[i] = directive + " 'unsafe-eval'";
            return string.Join("; ", directives);
        }

        directives.Add("script-src 'self' 'unsafe-eval'");
        return string.Join("; ", directives);
    }

    public static bool IsSensitiveAuthenticationRequest(HttpRequest request) =>
        AspNetHttpMethods.IsPost(request.Method) && SensitiveAuthenticationPaths.Contains(request.Path.Value ?? "");

    public static bool RequiresSameOriginValidation(HttpRequest request)
    {
        if (AspNetHttpMethods.IsGet(request.Method) || AspNetHttpMethods.IsHead(request.Method) ||
            AspNetHttpMethods.IsOptions(request.Method) || AspNetHttpMethods.IsTrace(request.Method)) return false;
        if (!(request.Path.Value?.StartsWith("/saas/", StringComparison.OrdinalIgnoreCase) ?? false)) return false;
        if (HasApiCredential(request)) return false;
        return request.Headers.ContainsKey("Cookie");
    }

    public static bool IsSameOrigin(HttpRequest request, string? configuredBaseUrl)
    {
        var value = request.Headers.Origin.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(value))
            value = request.Headers.Referer.FirstOrDefault();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var source)) return false;

        if (Uri.TryCreate($"{request.Scheme}://{request.Host}", UriKind.Absolute, out var requestOrigin) &&
            SameAuthority(source, requestOrigin)) return true;
        return Uri.TryCreate(configuredBaseUrl, UriKind.Absolute, out var configuredOrigin) &&
               SameAuthority(source, configuredOrigin);
    }

    private static bool HasApiCredential(HttpRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.Headers["X-Api-Key"].FirstOrDefault())) return true;
        var authorization = request.Headers.Authorization.FirstOrDefault();
        return authorization?.StartsWith("Bearer ak-", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool SameAuthority(Uri left, Uri right) =>
        left.Scheme.Equals(right.Scheme, StringComparison.OrdinalIgnoreCase) &&
        left.Host.Equals(right.Host, StringComparison.OrdinalIgnoreCase) &&
        left.Port == right.Port;
}

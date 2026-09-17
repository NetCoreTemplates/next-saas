using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using MyApp.Data;
using MyApp.ServiceInterface;

ApplyRuntimeSettingsEnvironment();
ApplyDevelopmentEnvFile();
AppHost.RegisterKey();

var builder = WebApplication.CreateBuilder(args);
var services = builder.Services;

services.AddAuthorization();
services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies(options => options.ApplicationCookie!.Configure(cookie =>
    {
        cookie.Cookie.HttpOnly = true;
        cookie.Cookie.SameSite = SameSiteMode.Lax;
        cookie.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        cookie.ExpireTimeSpan = TimeSpan.FromHours(8);
        cookie.SlidingExpiration = true;
    }));
services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo("App_Data"));

services.AddDatabaseDeveloperPageExceptionFilter();

services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = true;
        options.User.RequireUniqueEmail = true;
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

services.AddRazorPages();

var emailProvider = builder.Configuration.GetValue<EmailProvider>("Notifications:Provider");
if (emailProvider == EmailProvider.Smtp)
    services.AddSingleton<IEmailSender<ApplicationUser>, EmailSender>();
else
    services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();
services.AddScoped<IUserClaimsPrincipalFactory<ApplicationUser>, AdditionalUserClaimsPrincipalFactory>();

// Register all services
services.AddServiceStack(typeof(MyServices).Assembly);

var app = builder.Build();
var nodeProxy = new NodeProxy("http://127.0.0.1:3000") {
    Log = app.Logger
};

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseMigrationsEndPoint();

    app.MapNotFoundToNode(nodeProxy);
    // ServiceStack owns its diagnostic page at "/" in development. Rewrite
    // only that request to a Next.js alias so the product landing page remains
    // the application entry point while preserving the browser URL.
    app.Use(async (context, next) => {
        if (context.Request.Path == "/")
            context.Request.Path = "/landing";
        await next();
    });
}
else
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapCleanUrls();

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapRazorPages();

app.UseServiceStack(new AppHost(), options => {
    options.MapEndpoints();
});

// Proxy HMR WebSocket and fallback routes to Node dev server in Development
if (app.Environment.IsDevelopment())
{
    app.RunNodeProcess(nodeProxy, "../MyApp.Client"); // Start Node if not running
    app.UseWebSockets();
    app.MapNextHmr(nodeProxy);
    app.MapFallbackToNode(nodeProxy); // Fallback to Node dev server in development
}
else
{
    app.MapFallbackToFile("index.html"); // Fallback to index.html in production (MyApp.Client/dist > wwwroot)
}

app.Run();

static void ApplyRuntimeSettingsEnvironment()
{
    var encodedJson = Environment.GetEnvironmentVariable("APPSETTINGS_JSON_BASE64");
    var plainJson = Environment.GetEnvironmentVariable("APPSETTINGS_JSON");
    if (string.IsNullOrWhiteSpace(encodedJson) && string.IsNullOrWhiteSpace(plainJson))
        return;

    try
    {
        var json = !string.IsNullOrWhiteSpace(encodedJson)
            ? Encoding.UTF8.GetString(Convert.FromBase64String(encodedJson))
            : plainJson!;
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new JsonException("The root value must be a JSON object.");

        // HostingStartup configuration is evaluated while CreateBuilder runs, so
        // flatten the runtime bundle first. ASP.NET Core then reads these values
        // through its normal environment provider without writing a secrets file.
        Flatten(document.RootElement, []);
    }
    catch (Exception ex) when (ex is FormatException or System.Text.Json.JsonException)
    {
        throw new InvalidOperationException(
            "APPSETTINGS_JSON_BASE64 or APPSETTINGS_JSON does not contain valid application settings JSON.", ex);
    }

    static void Flatten(JsonElement value, string[] path)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in value.EnumerateObject())
                Flatten(property.Value, [.. path, property.Name]);
            return;
        }
        if (value.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in value.EnumerateArray())
                Flatten(item, [.. path, (index++).ToString()]);
            return;
        }

        var text = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Null => "",
            _ => value.GetRawText(),
        };
        Environment.SetEnvironmentVariable(string.Join("__", path), text);
    }
}

/// <summary>
/// Applies the developer's private .env in Development, so local overrides such as the database
/// provider scripts/dev-db.sh writes never touch a source-controlled settings file. Keys use the
/// ASP.NET Core environment convention, for example Database__Provider.
///
/// This runs before CreateBuilder because HostingStartup configuration, including the database
/// provider in Configure.Db.cs, is composed while the builder is created. It follows the same
/// Dotenv semantics as the operator scripts and config/deploy.yml: a variable already present in
/// the environment always wins, so an explicit override on the command line still applies.
/// </summary>
static void ApplyDevelopmentEnvFile()
{
    if (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") != Environments.Development)
        return;

    // The application runs from MyApp while .env sits beside the solution.
    var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (directory != null && !File.Exists(Path.Combine(directory.FullName, ".env")))
        directory = directory.Parent;
    if (directory == null)
        return;

    foreach (var line in File.ReadAllLines(Path.Combine(directory.FullName, ".env")))
    {
        var text = line.Trim();
        if (text.Length == 0 || text.StartsWith('#'))
            continue;
        var separator = text.IndexOf('=');
        if (separator <= 0)
            continue;
        var key = text[..separator].Trim();
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
            continue;
        var value = text[(separator + 1)..].Trim();
        if (value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
            value = value[1..^1];
        Environment.SetEnvironmentVariable(key, value);
    }
}

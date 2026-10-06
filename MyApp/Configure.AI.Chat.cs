using ServiceStack;
using ServiceStack.AI;
using MyApp.ServiceModel;

[assembly: HostingStartup(typeof(NextSaas.ConfigureAiChat))]

namespace NextSaas;

/// <summary>
/// AI Chat using this host's ASP.NET Identity users
/// </summary>
public class ConfigureAiChat : IHostingStartup
{
    public void Configure(IWebHostBuilder builder) => builder
        .ConfigureServices((context, services) => {

            services.AddPlugin(new ChatFeature {
                // Require authentication to access /chat
                RequireAuth = true,
                // RequiredRole = "Admin",
#if DEBUG
                Tools =
                {
                    // WARNING: Expands what AI Models can do by letting them execute
                    // code on the server and read/write files in allowed directories.
                    // Only enable for trusted users on trusted environments.
                    EnableCodeExecution = true,
                    EnableFilesystemTools = true,
                },
                // Share your best Projects, Threads or AI Media with everyone
                // ShareLlmspy = { Enabled = true },
#endif
                
                // Expose APIs with these tags to API & MCP Tools. Each API's own auth, roles and scopes still apply,
                // so platform APIs are only usable by operators, and writes need the user's approval.
                ApiTools = {
                    IncludeTags = [
                        ApiTags.Organizations, ApiTags.Team, ApiTags.Billing, ApiTags.Usage, ApiTags.Documents,
                        ApiTags.ApiKeys, ApiTags.Notifications, ApiTags.Audit, ApiTags.Platform,
                    ],
                    // Metering is for the product's own API clients, not for an assistant to consume quota
                    ExcludeTypes = [nameof(RecordUsage)],
                },

                // Add this App's own tools to the built-in extensions
                // Expose these tools to external AI Agents over MCP at /chat/mcp
                Mcp = {
                    // allow Agents to call any tool in the above groups without approval
                    // RejectToolsRequiringApproval = false, 
                },
                
                Setup = (ctx => {
                    // Advanced setup
                }),
            });

            services.ConfigurePlugin<MetadataFeature>(feature => {
                feature.AddPluginLink("/chat", "AI Chat");
            });

            // Enable https://servicestack.net/pdf
            services.AddPlugin(new PdfFeature {
                PdfCodeGen = new() {
                    Namespace = "NextSaas.ServiceModel.Pdf",
                    OutputPath = System.IO.Path.Combine(context.HostingEnvironment.ContentRootPath, "../NextSaas.ServiceModel/Pdf"),
                }
            });
       })
       .ConfigureAppHost(afterAppHostInit:appHost => {
            var log = appHost.GetApplicationServices().GetRequiredService<ILogger<ConfigureAiChat>>();
            log.LogInformation("AI Chat configured");
            
            // Keep typed PDF models in sync with published templates on each debug restart.
            // Templates you've since edited the model of are left alone, as are any named in Exclude.
            StartupTasks.Register("pdf", () => appHost.GetPlugin<PdfFeature>().GeneratePdfs());
        });
}

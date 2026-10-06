namespace MyApp.ServiceModel;

/// <summary>
/// [Tag] groups for this App's APIs, which group them in API Explorer (/ui), /schema and OpenAPI,
/// and select them in bulk for AI Chat's API Tools and MCP with ChatFeature.ApiTools.IncludeTags.
/// APIs for platform operators are also tagged with Platform, listed after their group.
/// </summary>
public static class ApiTags
{
    public const string Organizations = "organizations";
    public const string Team = "team";
    public const string Billing = "billing";
    public const string Usage = "usage";
    public const string Documents = "documents";
    public const string ApiKeys = "api-keys";
    public const string Notifications = "notifications";
    public const string Audit = "audit";

    // Platform operator groups
    public const string Customers = "customers";
    public const string Plans = "plans";
    public const string Support = "support";
    public const string Operations = "operations";

    /// <summary>
    /// Every API for platform operators
    /// </summary>
    public const string Platform = "platform";

    // Infrastructure and examples, not for API Tools
    public const string Webhooks = "webhooks";
    public const string Examples = "examples";
}

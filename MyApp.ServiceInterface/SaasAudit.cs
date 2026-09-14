using System.Data;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MyApp.ServiceModel;
using ServiceStack.OrmLite;

namespace MyApp.ServiceInterface;

public static partial class SaasAudit
{
    private const int MaxDetailLength = 16_384;
    private static readonly HashSet<string> TenantCategories = new(StringComparer.OrdinalIgnoreCase)
        { "workspace", "membership", "usage", "billing", "entitlement", "storage", "notification", "lifecycle", "support", "api-key" };
    private static readonly HashSet<string> RegisteredActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "workspace:created", "workspace:workspace.selected", "workspace:profile.updated", "workspace:status.changed",
        "membership:member.invited", "membership:invitation.resent", "membership:invitation.accepted",
        "membership:invitation.revoked", "membership:member.removed", "membership:member.role-changed",
        "membership:ownership.transferred", "membership:member.left",
        "usage:quota.warning", "usage:quota.rejected", "usage:upload-counter.failed", "usage:gauge.adjusted",
        "plan:draft.saved", "plan:version.published",
        "billing:access-mode.changed", "billing:checkout.session.confirmed", "billing:subscription.reconciled",
        "billing:subscription.reconciliation-failed",
        "coupon:coupon.created", "coupon:coupon.deactivated",
        "stripe:catalog.provisioned", "stripe:event.retry-requested",
        "entitlement:override.saved", "entitlement:override.deleted",
        "storage:file.uploaded", "storage:file.downloaded", "storage:file.deletion-requested",
        "storage:file.deleted", "storage:export.downloaded",
        "notification:preferences.updated", "notification:delivery.retry-requested",
        "lifecycle:export.requested", "lifecycle:export.completed", "lifecycle:export.expired", "lifecycle:export.expiry-failed",
        "lifecycle:deletion.requested", "lifecycle:deletion.canceled", "lifecycle:deletion.completed",
        "lifecycle:operation.retry-requested", "lifecycle:retention.policy-updated",
        "support:access.granted", "support:access.revoked", "support:access.started", "support:access.ended", "support:access.denied", "support:note.created",
        "api-key:created", "api-key:updated", "api-key:deleted",
    };

    public static long Write(IDbConnection db, SaasAuditEvent auditEvent)
    {
        try
        {
            Validate(auditEvent);
        }
        catch
        {
            SaasTelemetry.AuditRejected.Add(1);
            throw;
        }
        auditEvent.CreatedDate = auditEvent.CreatedDate == default ? DateTime.UtcNow : auditEvent.CreatedDate.ToUniversalTime();
        auditEvent.Outcome = string.IsNullOrEmpty(auditEvent.Outcome) ? "Succeeded" : Limit(auditEvent.Outcome, 64)!;
        auditEvent.Reason = Limit(auditEvent.Reason, 2_000);
        auditEvent.RequestId = Limit(auditEvent.RequestId, 128);
        auditEvent.IpAddress = Limit(auditEvent.IpAddress, 128);
        auditEvent.UserAgent = Limit(auditEvent.UserAgent, 512);
        auditEvent.DetailJson = RedactDetail(auditEvent.DetailJson);
        var id = db.Insert<SaasAuditEvent>(auditEvent);
        SaasTelemetry.AuditWritten.Add(1);
        return id;
    }

    public static bool IsRegistered(string category, string action) =>
        RegisteredActions.Contains($"{category}:{action}") ||
        category.Equals("billing", StringComparison.OrdinalIgnoreCase) &&
        (action.Equals("checkout.session.completed", StringComparison.OrdinalIgnoreCase) ||
         action.Equals("invoice.payment_failed", StringComparison.OrdinalIgnoreCase) ||
         action.Equals("invoice.paid", StringComparison.OrdinalIgnoreCase) ||
         action.StartsWith("customer.subscription.", StringComparison.OrdinalIgnoreCase));

    public static string? RedactDetail(string? detailJson)
    {
        if (string.IsNullOrEmpty(detailJson)) return null;
        try
        {
            var node = JsonNode.Parse(detailJson!);
            RedactNode(node);
            return Limit(node?.ToJsonString(new JsonSerializerOptions { WriteIndented = false }), MaxDetailLength);
        }
        catch (JsonException)
        {
            return Limit(SecretValuePattern().Replace(detailJson!, "[REDACTED]"), MaxDetailLength);
        }
    }

    private static void Validate(SaasAuditEvent value)
    {
        if (string.IsNullOrEmpty(value.Category)) throw new InvalidOperationException("Audit category is required.");
        if (string.IsNullOrEmpty(value.Action)) throw new InvalidOperationException("Audit action is required.");
        if (string.IsNullOrEmpty(value.ActorId)) throw new InvalidOperationException("Audit actor is required.");
        if (!IsRegistered(value.Category, value.Action))
            throw new InvalidOperationException($"Audit action '{value.Category}:{value.Action}' is not registered.");
        if (TenantCategories.Contains(value.Category) && string.IsNullOrEmpty(value.WorkspaceId))
            throw new InvalidOperationException($"Audit category '{value.Category}' requires an organization ID.");
    }

    private static void RedactNode(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            foreach (var property in obj.ToList())
            {
                if (SensitiveNamePattern().IsMatch(property.Key))
                    obj[property.Key] = "[REDACTED]";
                else if (property.Value is JsonValue value && value.TryGetValue<string>(out var text))
                    obj[property.Key] = SecretValuePattern().Replace(text, "[REDACTED]");
                else
                    RedactNode(property.Value);
            }
        }
        else if (node is JsonArray array)
        {
            for (var i = 0; i < array.Count; i++)
            {
                if (array[i] is JsonValue value && value.TryGetValue<string>(out var text))
                    array[i] = SecretValuePattern().Replace(text, "[REDACTED]");
                else
                    RedactNode(array[i]);
            }
        }
    }

    private static string? Limit(string? value, int length) =>
        string.IsNullOrEmpty(value) ? null : value!.Length <= length ? value : value[..length];

    [GeneratedRegex("password|secret|token|authorization|cookie|api[-_ ]?key|signature", RegexOptions.IgnoreCase)]
    private static partial Regex SensitiveNamePattern();

    [GeneratedRegex(@"\b(?:(?:sk|rk)_(?:test|live)_[A-Za-z0-9]+|whsec_[A-Za-z0-9]+|ak-[A-Za-z0-9]+)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SecretValuePattern();
}

/// <summary>
/// Exact overload ensures every existing and future db.Insert(SaasAuditEvent)
/// call is routed through the audit policy instead of OrmLite's generic insert.
/// </summary>
public static class SaasAuditDbExtensions
{
    public static long Insert(this IDbConnection db, SaasAuditEvent auditEvent) => SaasAudit.Write(db, auditEvent);
}

using MyApp.ServiceModel;
using ServiceStack;
using ServiceStack.Auth;
using ServiceStack.Data;
using ServiceStack.OrmLite;

namespace MyApp.ServiceInterface;

public interface IAccountDeletionManager
{
    List<string> GetOwnedOrganizationNames(string userId);
    void RemoveSaasAccess(string userId);
}

public class AccountDeletionManager(IDbConnectionFactory dbFactory) : IAccountDeletionManager
{
    public List<string> GetOwnedOrganizationNames(string userId)
    {
        using var db = dbFactory.Open();
        if (!db.TableExists<WorkspaceMember>() || !db.TableExists<Workspace>()) return [];

        var ownedIds = db.Select<WorkspaceMember>(x =>
                x.UserId == userId && x.Role == WorkspaceMemberRole.Owner && x.Status == WorkspaceMemberStatus.Active)
            .Select(x => x.WorkspaceId).Distinct().ToList();
        if (ownedIds.Count == 0) return [];

        return db.SelectByIds<Workspace>(ownedIds)
            .Where(x => x.Status != WorkspaceStatus.Deleted)
            .OrderBy(x => x.Name)
            .Select(x => x.Name)
            .ToList();
    }

    public void RemoveSaasAccess(string userId)
    {
        using var db = dbFactory.Open();
        var owned = GetOwnedOrganizationNames(db, userId);
        if (owned.Count > 0)
            throw new InvalidOperationException(
                $"Transfer or delete the owned organization{(owned.Count == 1 ? "" : "s")} before deleting this account: {string.Join(", ", owned)}.");

        using var transaction = db.OpenTransaction();
        var now = DateTime.UtcNow;

        if (db.TableExists<WorkspaceMember>())
        {
            var memberships = db.Select<WorkspaceMember>(x => x.UserId == userId);
            if (db.TableExists<SaasAuditEvent>())
            {
                foreach (var member in memberships.Where(x => x.Status == WorkspaceMemberStatus.Active))
                {
                    db.Insert(new SaasAuditEvent {
                        WorkspaceId = member.WorkspaceId,
                        Category = "membership",
                        Action = "member.left",
                        ActorId = userId,
                        SubjectId = member.Id,
                        Reason = "Personal account deletion",
                        CreatedDate = now,
                    });
                }
            }
            db.Delete<WorkspaceMember>(x => x.UserId == userId);
        }

        if (db.TableExists<ApiKeysFeature.ApiKey>())
        {
            if (db.TableExists<SaasAuditEvent>())
            {
                foreach (var apiKey in db.Select<ApiKeysFeature.ApiKey>(x => x.UserId == userId)
                             .Where(x => !string.IsNullOrEmpty(x.RefIdStr)))
                {
                    db.Insert(new SaasAuditEvent {
                        WorkspaceId = apiKey.RefIdStr,
                        Category = "api-key",
                        Action = "deleted",
                        ActorId = userId,
                        SubjectId = apiKey.Id.ToString(),
                        Reason = "Personal account deletion",
                        CreatedDate = now,
                    });
                }
            }
            db.Delete<ApiKeysFeature.ApiKey>(x => x.UserId == userId);
        }
        if (db.TableExists<UserWorkspacePreference>())
            db.DeleteById<UserWorkspacePreference>(userId);
        if (db.TableExists<NotificationPreference>())
            db.Delete<NotificationPreference>(x => x.UserId == userId);
        if (db.TableExists<NotificationDelivery>())
            db.Delete<NotificationDelivery>(x => x.UserId == userId);

        if (db.TableExists<SupportAccessGrant>())
        {
            foreach (var grant in db.Select<SupportAccessGrant>(x => x.OperatorId == userId &&
                         x.RevokedAt == null && x.AccessEndedAt == null))
            {
                grant.RevokedAt = now;
                grant.AccessEndedAt = now;
                grant.ModifiedDate = now;
                grant.ModifiedBy = "account-deletion";
                db.Update(grant);
                if (db.TableExists<SaasAuditEvent>())
                {
                    db.Insert(new SaasAuditEvent {
                        WorkspaceId = grant.WorkspaceId,
                        Category = "support",
                        Action = "access.revoked",
                        ActorId = "account-deletion",
                        SubjectId = grant.Id,
                        Reason = "Operator account deletion",
                        CreatedDate = now,
                    });
                }
            }
        }

        transaction.Commit();
    }

    private static List<string> GetOwnedOrganizationNames(System.Data.IDbConnection db, string userId)
    {
        if (!db.TableExists<WorkspaceMember>() || !db.TableExists<Workspace>()) return [];
        var ownedIds = db.Select<WorkspaceMember>(x =>
                x.UserId == userId && x.Role == WorkspaceMemberRole.Owner && x.Status == WorkspaceMemberStatus.Active)
            .Select(x => x.WorkspaceId).Distinct().ToList();
        return ownedIds.Count == 0
            ? []
            : db.SelectByIds<Workspace>(ownedIds)
                .Where(x => x.Status != WorkspaceStatus.Deleted)
                .OrderBy(x => x.Name)
                .Select(x => x.Name)
                .ToList();
    }
}

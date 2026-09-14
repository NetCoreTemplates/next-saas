# Data lifecycle

Organization lifecycle tools cover portable exports, delayed deletion, cancellation, ownership transfer, member departure, retention overrides, and legal holds.

[Documentation home](../README.md) · [Organizations and members](organizations-and-members.md)

## Customer APIs

| Operation | Route |
| --- | --- |
| Request export | `POST /saas/lifecycle/export` |
| View status | `GET /saas/lifecycle` |
| Download export | `GET /saas/lifecycle/exports/{Id}` |
| Request deletion | `POST /saas/lifecycle/delete` |
| Cancel deletion | `POST /saas/lifecycle/delete/cancel` |
| Transfer ownership | `POST /saas/lifecycle/transfer` |
| Leave organization | `POST /saas/lifecycle/leave` |

## Organization export

Export requests enqueue a background job that creates a ZIP containing a manifest, structured tenant records, and available files. The artifact is tenant-bound and expires after `Saas.ExportExpiryDays`. An expiry job removes the object and marks its metadata expired; later retention removes old lifecycle history.

An export is a point-in-time portability package, not a backup or database restore format. Extend the manifest whenever a derived product adds customer-owned tables or objects.

## Organization deletion

Only the Owner can request deletion. The UI explains that the organization, subscription projection, memberships, files, keys, usage, and customer-owned records are removed, while individual user accounts remain available for other organizations.

The request requires the exact organization confirmation and the Owner's current password. Paid subscriptions must first be Free or canceled. The organization enters `PendingDeletion` and becomes read-only during the configured delay. The Owner can cancel before processing starts.

The deletion worker checks legal hold again, deletes tenant data and physical objects, revokes organization API keys, and anonymizes the organization tombstone. It is designed for safe retry.

## Retention and legal holds

Global defaults are:

- `Saas.AnalyticsRetentionDays`
- `Saas.AuditRetentionDays`
- `Saas.NotificationRetentionDays`
- `Saas.DeletedFileRetentionDays`
- `Saas.LifecycleHistoryRetentionDays`
- `Saas.RetentionBatchSize`

Authorized operators may store per-organization overrides and a legal hold in `WorkspaceRetentionPolicy` through Customer 360 at `/admin/customers`. Legal hold prevents destructive lifecycle processing and must be checked both when requested and when a delayed worker executes. Global retention runs and the platform audit stream appear at `/admin/security`.

## Ownership and leaving

Ownership transfer targets an active member by user ID and atomically changes roles. The Owner cannot simply leave; ownership must be transferred first. Non-owners may leave, which removes their access without deleting their account or the organization.

## Verify

Inspect an export, download it only from the owning organization, expire it, and confirm object cleanup. Test deletion validation, read-only delay, cancellation, paid-plan blocking, legal hold, retry, API-key revocation, user-account survival, ownership transfer, and last-owner safeguards.

## Related documentation

- [File storage](file-storage.md)
- [Audit logs](audit-logs.md)
- [Support operations](support-operations.md)

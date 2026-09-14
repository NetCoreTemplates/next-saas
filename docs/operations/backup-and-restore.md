# Backup and restore

Backups are only complete when the RDBMS, file objects, protected keys, configuration version, and restore procedure can reproduce a consistent service.

[Operations](README.md) · [Database and storage](database-and-storage.md)

## What to protect

- PostgreSQL database containing Identity and SaaS product data;
- file/object store containing uploads and export artifacts;
- ASP.NET data-protection key ring;
- non-secret configuration/version and secret-manager references;
- optionally the durable ServiceStack job store when queued work must survive disaster.

Request logs and rebuildable analytics rollups may have a different recovery objective. Stripe financial data remains in Stripe, but local Customer/Subscription identifiers and inbox state are necessary to reconnect safely.

## Recovery objectives

Define and test:

- RPO: maximum acceptable data loss;
- RTO: maximum acceptable service interruption;
- backup frequency and retention;
- encryption and access control;
- region/account failure boundaries;
- owner and escalation path.

Use PostgreSQL-native snapshots or managed point-in-time recovery. Use object-store versioning/replication or a coordinated file-store snapshot. A raw copy of a live SQLite file is not a safe generic backup method.

## Consistency strategy

The database references file objects by opaque key. Prefer coordinated snapshots, or record the database restore point and reconcile object state afterward. Extra orphaned objects are safer than metadata pointing to missing customer files; retain enough version history to recover either side.

Treat data-protection keys as sensitive state and restore them before accepting authentication traffic.

## Restore drill

1. restore into an isolated environment with no outbound email/webhook processing;
2. restore the database, object store, and data-protection keys to the chosen point;
3. apply only migrations required by the restored application image;
4. run `/ready` and integrity checks for organizations, memberships, plans, subscriptions, files, and usage;
5. sample file hashes/downloads and compare Stripe identifiers without mutating live Stripe;
6. inspect pending inbox, lifecycle, notification, and job work before enabling workers;
7. rotate any credentials exposed during the incident;
8. cut traffic over, enable workers deliberately, and monitor reconciliation;
9. document achieved RPO/RTO and repair gaps.

Do not replay restored emails, deletions, or provider mutations blindly. Their external side effect may have occurred after the restored database point.

## Development SQLite

Development state is disposable by design and can be recreated with `scripts/reset-dev.sh`. Do not mistake that workflow for a production restore procedure.

## Related documentation

- [Deployment](deployment.md)
- [Background jobs and recovery](background-jobs-and-recovery.md)
- [Retention and lifecycle](retention-and-lifecycle.md)


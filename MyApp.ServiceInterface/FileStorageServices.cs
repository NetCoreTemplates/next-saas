using System.Data;
using System.IO.Compression;
using MyApp.ServiceModel;
using ServiceStack;
using ServiceStack.Data;
using ServiceStack.Jobs;
using ServiceStack.OrmLite;

namespace MyApp.ServiceInterface;

public class FileStorageServices(
    FileStorageConfig fileConfig,
    ISaasManager manager,
    IWorkspaceContextResolver workspaceContexts,
    IEntitlementResolver entitlements,
    IFileStore files,
    IBackgroundJobs jobs) : Service
{
    public async Task<object> Any(QueryStoredFiles request)
    {
        var context = await GetContextAsync();
        RequireFiles(context, write: false);
        var rows = Db.Select<StoredFile>(x => x.WorkspaceId == context.Workspace.Id && x.Status != StoredFileStatus.Deleted)
            .Where(x => request.Search.IsNullOrEmpty() || x.Name.Contains(request.Search!, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.CreatedDate).ToList();
        return new QueryStoredFilesResponse {
            Total = rows.Count,
            Results = rows.Skip(Math.Max(0, request.Skip)).Take(Math.Clamp(request.Take, 1, 100)).Select(ToInfo).ToList(),
        };
    }

    public async Task<object> Any(UploadStoredFile request)
    {
        var context = await GetContextAsync();
        RequireFiles(context, write: true);
        var existing = Db.Single<StoredFile>(x => x.WorkspaceId == context.Workspace.Id && x.IdempotencyKey == request.IdempotencyKey);
        if (existing?.Status == StoredFileStatus.Available) return ToInfo(existing);
        if (existing != null)
            throw new HttpError(409, "UploadAlreadyStarted", "An earlier upload with this idempotency key did not complete. Use a new key to retry.");
        var upload = Request?.Files?.FirstOrDefault()
            ?? throw new HttpError(400, "FileRequired", "Attach one file using multipart form data.");
        var name = Path.GetFileName(upload.FileName).Trim();
        if (name.IsNullOrEmpty()) throw new HttpError(400, "FileNameRequired", "The uploaded file needs a filename.");
        var extension = Path.GetExtension(name).ToLowerInvariant();
        if (fileConfig.AllowedExtensions.Count > 0 && !fileConfig.AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            throw new HttpError(415, "FileTypeNotAllowed", $"Files with the '{extension}' extension are not allowed.");
        if (upload.ContentLength > fileConfig.MaxFileBytes)
            throw new HttpError(413, "FileTooLarge", $"The uploaded file exceeds the {fileConfig.MaxFileBytes} byte limit.");

        var subscription = GetSubscription(context.Workspace.Id);
        var maximumBytes = upload.ContentLength > 0 ? upload.ContentLength : fileConfig.MaxFileBytes;
        var documentReservation = manager.ReserveUsage(Db, context.Workspace, subscription, context.UserId,
            "documents.stored", 1, request.IdempotencyKey + ":document", new { name }.ToJson());
        UsageReservation? byteReservation = null;
        StoredFile? row = null;
        try
        {
            byteReservation = manager.ReserveUsage(Db, context.Workspace, subscription, context.UserId,
                "storage.bytes", maximumBytes, request.IdempotencyKey + ":bytes", new { name }.ToJson());
            var now = DateTime.UtcNow;
            row = new StoredFile {
                WorkspaceId = context.Workspace.Id, IdempotencyKey = request.IdempotencyKey, Name = name,
                ObjectKey = $"workspaces/{context.Workspace.Id}/files/{Guid.NewGuid():N}",
                ContentType = upload.ContentType ?? "application/octet-stream", ByteLength = maximumBytes,
                Status = StoredFileStatus.Pending, UploadedBy = context.UserId,
                CreatedDate = now, ModifiedDate = now, CreatedBy = context.UserId, ModifiedBy = context.UserId,
            };
            Db.Insert(row);
            var stored = await files.WriteAsync(row.ObjectKey, upload.InputStream, maximumBytes);
            manager.SettleUsage(Db, context.Workspace, subscription, context.UserId, byteReservation.Id, stored.ByteLength);
            manager.SettleUsage(Db, context.Workspace, subscription, context.UserId, documentReservation.Id, 1);
            row.ByteLength = stored.ByteLength;
            row.Sha256 = stored.Sha256;
            row.Status = StoredFileStatus.Available;
            row.ModifiedDate = DateTime.UtcNow;
            row.ModifiedBy = context.UserId;
            Db.Update(row);
            Audit(context, "file.uploaded", row.Id, new { row.Name, row.ByteLength, row.ContentType });
            try
            {
                // This period counter is analytical rather than an admission control for
                // the stored object. A transient metering failure must not turn a fully
                // persisted, quota-accounted file into a failed upload.
                manager.RecordUsage(Db, context.Workspace, subscription, context.UserId, new RecordUsage {
                    MeterKey = "documents.uploaded", Units = 1, IdempotencyKey = request.IdempotencyKey + ":uploaded",
                    MetadataJson = new { fileId = row.Id }.ToJson(),
                });
            }
            catch (Exception ex)
            {
                Db.Insert(new SaasAuditEvent {
                    WorkspaceId = context.Workspace.Id, Category = "usage", Action = "upload-counter.failed",
                    ActorId = context.UserId, SubjectId = row.Id, Outcome = "Failed", Reason = Truncate(ex.Message, 1000),
                    CreatedDate = DateTime.UtcNow,
                });
            }
            return ToInfo(row);
        }
        catch (Exception ex)
        {
            CompensateReservation(subscription, context, byteReservation, request.IdempotencyKey + ":cleanup:bytes");
            CompensateReservation(subscription, context, documentReservation, request.IdempotencyKey + ":cleanup:document");
            if (row != null)
            {
                try { await files.DeleteAsync(row.ObjectKey); } catch { /* Retain the original upload error. */ }
                row.Status = StoredFileStatus.Failed;
                row.LastError = Truncate(ex.Message, 1000);
                row.ModifiedDate = DateTime.UtcNow;
                row.ModifiedBy = context.UserId;
                Db.Update(row);
            }
            throw;
        }
    }

    public async Task<object> Any(DownloadStoredFile request)
    {
        var context = await GetContextAsync();
        RequireFiles(context, write: false);
        var row = Db.Single<StoredFile>(x => x.Id == request.Id && x.WorkspaceId == context.Workspace.Id && x.Status == StoredFileStatus.Available)
            ?? throw new HttpError(404, "StoredFileNotFound", "The file was not found.");
        var stream = await files.OpenReadAsync(row.ObjectKey);
        Audit(context, "file.downloaded", row.Id);
        return new HttpResult(stream, row.ContentType) {
            Headers = { [HttpHeaders.ContentDisposition] = $"attachment; filename=\"{SafeDownloadName(row.Name)}\"" },
        };
    }

    public async Task<object> Any(DeleteStoredFile request)
    {
        var context = await GetContextAsync();
        RequireFiles(context, write: true);
        var row = Db.Single<StoredFile>(x => x.Id == request.Id && x.WorkspaceId == context.Workspace.Id)
            ?? throw new HttpError(404, "StoredFileNotFound", "The file was not found.");
        if (row.Status is StoredFileStatus.Deleted or StoredFileStatus.Deleting) return new EmptyResponse();
        row.Status = StoredFileStatus.Deleting;
        row.ModifiedDate = DateTime.UtcNow;
        row.ModifiedBy = context.UserId;
        Db.Update(row);
        Audit(context, "file.deletion-requested", row.Id);
        jobs.EnqueueCommand<DeleteStoredFileCommand>(new DeleteStoredFileWork { FileId = row.Id });
        return new EmptyResponse();
    }

    public async Task<object> Any(DownloadWorkspaceExport request)
    {
        var context = await GetContextAsync();
        if (!context.IsAdmin) throw new HttpError(403, "WorkspaceAdminRequired", "Organization Owner or Admin role is required.");
        var artifact = Db.Single<DataExportArtifact>(x => x.Id == request.Id && x.WorkspaceId == context.Workspace.Id && x.ExpiresAt > DateTime.UtcNow)
            ?? throw new HttpError(404, "ExportNotFound", "The export was not found or has expired.");
        var stream = await files.OpenReadAsync(artifact.ObjectKey);
        artifact.DownloadedAt = DateTime.UtcNow;
        artifact.ModifiedDate = DateTime.UtcNow;
        artifact.ModifiedBy = context.UserId;
        Db.Update(artifact);
        Audit(context, "export.downloaded", artifact.Id);
        return new HttpResult(stream, "application/zip") {
            Headers = { [HttpHeaders.ContentDisposition] = $"attachment; filename=\"{context.Workspace.Slug}-export.zip\"" },
        };
    }

    private async Task<WorkspaceContext> GetContextAsync() => workspaceContexts.Resolve(Db, await GetSessionAsync(), Request);

    private BillingSubscription GetSubscription(string workspaceId) =>
        Db.Single<BillingSubscription>(x => x.WorkspaceId == workspaceId)
        ?? throw new HttpError(404, "SubscriptionNotFound", "The organization subscription was not found.");

    private void RequireFiles(WorkspaceContext context, bool write)
    {
        var subscription = GetSubscription(context.Workspace.Id);
        manager.EvaluateAccess(Db, context.Workspace, subscription);
        if (subscription.AccessMode == WorkspaceAccessMode.Suspended || context.Workspace.Status == WorkspaceStatus.Deleted ||
            write && (context.Workspace.Status != WorkspaceStatus.Active || subscription.AccessMode == WorkspaceAccessMode.ReadOnly))
            throw new HttpError(423, "WorkspaceReadOnly", "File changes are unavailable while the organization is not active.");
        if (!entitlements.HasFeature(Db, context.Workspace, subscription, "files.basic"))
            throw new HttpError(403, "FeatureNotEntitled", "File storage is not included in the current plan.");
    }

    private void Audit(WorkspaceContext context, string action, string subjectId, object? detail = null) => Db.Insert(new SaasAuditEvent {
        WorkspaceId = context.Workspace.Id, Category = "storage", Action = action, ActorId = context.UserId,
        SubjectId = subjectId, DetailJson = detail?.ToJson(), IpAddress = Request?.RemoteIp,
        UserAgent = Request?.UserAgent, CreatedDate = DateTime.UtcNow,
    });

    private void CompensateReservation(BillingSubscription subscription, WorkspaceContext context,
        UsageReservation? reservation, string adjustmentKey)
    {
        if (reservation == null) return;
        try
        {
            var current = Db.SingleById<UsageReservation>(reservation.Id);
            if (current?.Status == UsageReservationStatus.Pending)
            {
                manager.ReleaseUsage(Db, context.Workspace, subscription, context.UserId, current.Id);
            }
            else if (current?.Status == UsageReservationStatus.Settled && current.SettledUnits > 0)
            {
                manager.AdjustGauge(Db, context.Workspace, subscription, context.UserId, current.MeterKey,
                    -current.SettledUnits.Value, adjustmentKey, "upload-compensation");
            }
        }
        catch
        {
            // Keep the originating exception. The immutable reservation plus the failed
            // file row gives operations enough state for repair if the database itself failed.
        }
    }

    private static StoredFileInfo ToInfo(StoredFile row) => new() {
        Id = row.Id, Name = row.Name, ContentType = row.ContentType, ByteLength = row.ByteLength,
        Sha256 = row.Sha256, Status = row.Status, CreatedDate = row.CreatedDate, UploadedBy = row.UploadedBy,
    };

    private static string SafeDownloadName(string value) => value.Replace("\"", "").Replace("\r", "").Replace("\n", "");
    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length];
}

public class DeleteStoredFileWork
{
    public string FileId { get; set; } = "";
}

[Worker("storage")]
public class DeleteStoredFileCommand(
    IDbConnectionFactory dbFactory,
    IFileStore files,
    ISaasManager manager) : AsyncCommand<DeleteStoredFileWork>
{
    protected override async Task RunAsync(DeleteStoredFileWork request, CancellationToken token)
    {
        using var db = dbFactory.Open();
        var row = db.SingleById<StoredFile>(request.FileId);
        if (row == null || row.Status == StoredFileStatus.Deleted) return;
        var workspace = db.SingleById<Workspace>(row.WorkspaceId);
        var subscription = db.Single<BillingSubscription>(x => x.WorkspaceId == row.WorkspaceId);
        try
        {
            await files.DeleteAsync(row.ObjectKey, token);
            manager.AdjustGauge(db, workspace, subscription, row.ModifiedBy, "storage.bytes", -row.ByteLength, $"file:{row.Id}:delete:bytes", "file-delete");
            manager.AdjustGauge(db, workspace, subscription, row.ModifiedBy, "documents.stored", -1, $"file:{row.Id}:delete:document", "file-delete");
            row.Status = StoredFileStatus.Deleted;
            row.DeletedDate = DateTime.UtcNow;
            row.LastError = null;
            row.ModifiedDate = DateTime.UtcNow;
            db.Update(row);
            db.Insert(new SaasAuditEvent { WorkspaceId = row.WorkspaceId, Category = "storage", Action = "file.deleted", ActorId = row.ModifiedBy, SubjectId = row.Id, CreatedDate = DateTime.UtcNow });
        }
        catch (Exception ex)
        {
            row.Status = StoredFileStatus.Deleting;
            row.LastError = ex.Message.Length <= 1000 ? ex.Message : ex.Message[..1000];
            row.ModifiedDate = DateTime.UtcNow;
            db.Update(row);
            throw;
        }
    }
}

public class ProcessWorkspaceLifecycleCommand(
    IDbConnectionFactory dbFactory,
    IFileStore files,
    SaasConfig config) : AsyncCommand<ProcessWorkspaceLifecycle>
{
    protected override async Task RunAsync(ProcessWorkspaceLifecycle request, CancellationToken token)
    {
        using var db = dbFactory.Open();
        var operation = db.SingleById<WorkspaceLifecycleRequest>(request.RequestId);
        if (operation == null || operation.Status is LifecycleRequestStatus.Completed or LifecycleRequestStatus.Canceled) return;
        if (operation.ScheduledAt != null && operation.ScheduledAt > DateTime.UtcNow) return;
        operation.Status = LifecycleRequestStatus.Processing;
        operation.ModifiedDate = DateTime.UtcNow;
        operation.ModifiedBy = "lifecycle-job";
        db.Update(operation);
        try
        {
            if (operation.Type == LifecycleRequestType.Export)
                await ExportAsync(db, operation, token);
            else if (operation.Type == LifecycleRequestType.Delete)
                await DeleteAsync(db, operation, token);
            operation.Status = LifecycleRequestStatus.Completed;
            operation.CompletedAt = DateTime.UtcNow;
            operation.LastError = null;
            operation.ModifiedDate = DateTime.UtcNow;
            db.Update(operation);
            SaasTelemetry.LifecycleCompleted.Add(1,
                new KeyValuePair<string, object?>("type", operation.Type.ToString()));
        }
        catch (Exception ex)
        {
            SaasTelemetry.LifecycleFailed.Add(1,
                new KeyValuePair<string, object?>("type", operation.Type.ToString()));
            if (operation.Status != LifecycleRequestStatus.Blocked)
                operation.Status = LifecycleRequestStatus.Failed;
            operation.LastError = ex.Message.Length <= 2000 ? ex.Message : ex.Message[..2000];
            operation.ModifiedDate = DateTime.UtcNow;
            db.Update(operation);
            throw;
        }
    }

    private async Task ExportAsync(IDbConnection db, WorkspaceLifecycleRequest operation, CancellationToken token)
    {
        if (db.Exists<DataExportArtifact>(x => x.LifecycleRequestId == operation.Id)) return;
        var workspace = db.SingleById<Workspace>(operation.WorkspaceId);
        var storedFiles = db.Select<StoredFile>(x => x.WorkspaceId == workspace.Id && x.Status == StoredFileStatus.Available);
        var temporaryPath = Path.Combine(Path.GetTempPath(), $"acme-export-{operation.Id}.zip");
        try
        {
            await using (var output = new FileStream(temporaryPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                using var archive = new ZipArchive(output, ZipArchiveMode.Create, true);
                var manifest = archive.CreateEntry("manifest.json", CompressionLevel.Fastest);
                await using (var manifestStream = manifest.Open())
                await using (var writer = new StreamWriter(manifestStream))
                {
                    var value = new {
                        exportedAt = DateTime.UtcNow,
                        workspace,
                        members = db.Select<WorkspaceMember>(x => x.WorkspaceId == workspace.Id),
                        usage = db.Select<UsageEvent>(x => x.WorkspaceId == workspace.Id),
                        files = storedFiles.Select(x => new { x.Id, x.Name, x.ContentType, x.ByteLength, x.Sha256, x.CreatedDate }),
                    };
                    await writer.WriteAsync(value.ToJson());
                }
                foreach (var row in storedFiles)
                {
                    var entry = archive.CreateEntry($"files/{row.Id}-{Path.GetFileName(row.Name)}", CompressionLevel.Fastest);
                    await using var entryStream = entry.Open();
                    await using var source = await files.OpenReadAsync(row.ObjectKey, token);
                    await source.CopyToAsync(entryStream, token);
                }
            }
            await using var input = new FileStream(temporaryPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
            var objectKey = $"workspaces/{workspace.Id}/exports/{operation.Id}.zip";
            var stored = await files.WriteAsync(objectKey, input, long.MaxValue, token);
            var now = DateTime.UtcNow;
            db.Insert(new DataExportArtifact {
                LifecycleRequestId = operation.Id, WorkspaceId = workspace.Id, ObjectKey = objectKey,
                ByteLength = stored.ByteLength, Sha256 = stored.Sha256, ExpiresAt = now.AddDays(config.ExportExpiryDays),
                CreatedDate = now, ModifiedDate = now, CreatedBy = "lifecycle-job", ModifiedBy = "lifecycle-job",
            });
            db.Insert(new SaasAuditEvent { WorkspaceId = workspace.Id, Category = "lifecycle", Action = "export.completed", ActorId = "lifecycle-job", SubjectId = operation.Id, CreatedDate = now });
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private async Task DeleteAsync(IDbConnection db, WorkspaceLifecycleRequest operation, CancellationToken token)
    {
        var workspace = db.SingleById<Workspace>(operation.WorkspaceId);
        if (workspace.Status == WorkspaceStatus.Deleted) return;
        if (db.SingleById<WorkspaceRetentionPolicy>(workspace.Id)?.LegalHold == true)
        {
            operation.Status = LifecycleRequestStatus.Blocked;
            operation.LastError = "Organization deletion is blocked by a legal hold.";
            operation.ModifiedDate = DateTime.UtcNow;
            operation.ModifiedBy = "lifecycle-job";
            db.Update(operation);
            throw new InvalidOperationException(operation.LastError);
        }
        foreach (var row in db.Select<StoredFile>(x => x.WorkspaceId == workspace.Id))
            await files.DeleteAsync(row.ObjectKey, token);
        foreach (var artifact in db.Select<DataExportArtifact>(x => x.WorkspaceId == workspace.Id))
            await files.DeleteAsync(artifact.ObjectKey, token);
        var now = DateTime.UtcNow;
        foreach (var apiKey in db.Select<ApiKeysFeature.ApiKey>(x => x.RefIdStr == workspace.Id && x.CancelledDate == null))
        {
            apiKey.CancelledDate = now;
            db.Update(apiKey);
        }
        var periods = db.Select<UsagePeriod>(x => x.WorkspaceId == workspace.Id);
        foreach (var period in periods)
        {
            db.Delete<UsageReservation>(x => x.UsagePeriodId == period.Id);
            db.Delete<UsageAggregate>(x => x.UsagePeriodId == period.Id);
        }
        db.Delete<UsageEvent>(x => x.WorkspaceId == workspace.Id);
        db.Delete<UsageDailyRollup>(x => x.WorkspaceId == workspace.Id);
        db.Delete<UsagePeriod>(x => x.WorkspaceId == workspace.Id);
        db.Delete<StoredFile>(x => x.WorkspaceId == workspace.Id);
        db.Delete<DataExportArtifact>(x => x.WorkspaceId == workspace.Id);
        db.Delete<NotificationPreference>(x => x.WorkspaceId == workspace.Id);
        db.Delete<NotificationDelivery>(x => x.WorkspaceId == workspace.Id);
        db.Delete<CustomerEntitlementOverride>(x => x.WorkspaceId == workspace.Id);
        db.Delete<SupportAccessGrant>(x => x.WorkspaceId == workspace.Id);
        db.Delete<SupportNote>(x => x.WorkspaceId == workspace.Id);
        db.Delete<WorkspaceRetentionPolicy>(x => x.WorkspaceId == workspace.Id);
        db.Delete<UserWorkspacePreference>(x => x.ActiveWorkspaceId == workspace.Id);
        db.Delete<WorkspaceMember>(x => x.WorkspaceId == workspace.Id);
        db.Delete<BillingSubscription>(x => x.WorkspaceId == workspace.Id);
        workspace.Name = "Deleted organization";
        workspace.Slug = $"deleted-{workspace.Id}";
        workspace.BillingEmail = null;
        workspace.Status = WorkspaceStatus.Deleted;
        workspace.ModifiedDate = now;
        workspace.ModifiedBy = "lifecycle-job";
        db.Update(workspace);
        db.Insert(new SaasAuditEvent { WorkspaceId = workspace.Id, Category = "lifecycle", Action = "deletion.completed", ActorId = "lifecycle-job", SubjectId = operation.Id, CreatedDate = now });
    }
}

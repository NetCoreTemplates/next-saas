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
    IEntitlementResolver entitlements,
    IFileStore files,
    IBackgroundJobs jobs) : Service
{
    public async Task<object> Any(QueryStoredFiles request)
    {
        var scope = Db.GetWorkspaceScope();
        RequireFiles(scope);
        // Searched and paged by the database, so the cost doesn't grow with how many files the organization has
        var q = Db.From<StoredFile>().Where(x => x.Status != StoredFileStatus.Deleted);
        if (!request.Search.IsNullOrEmpty())
        {
            var search = request.Search!.Trim().ToLowerInvariant();
            q.And(x => x.Name.ToLower().Contains(search));
        }
        return new QueryStoredFilesResponse {
            Total = Db.Count(q),
            Results = Db.Select(q.OrderByDescending(x => x.CreatedDate).ThenBy(x => x.Id)
                .Limit(Math.Max(0, request.Skip), Math.Clamp(request.Take, 1, 100))).Select(ToInfo).ToList(),
        };
    }

    public async Task<object> Any(UploadStoredFile request)
    {
        var scope = Db.GetWorkspaceScope();
        RequireFiles(scope);
        var existing = Db.Single<StoredFile>(x => x.IdempotencyKey == request.IdempotencyKey);
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

        var subscription = Db.GetSubscription();
        var maximumBytes = upload.ContentLength > 0 ? upload.ContentLength : fileConfig.MaxFileBytes;
        var documentReservation = manager.ReserveUsage(Db, scope.Workspace, subscription, scope.UserId,
            "documents.stored", 1, request.IdempotencyKey + ":document", new { name }.ToJson());
        UsageReservation? byteReservation = null;
        StoredFile? row = null;
        var workspaceFiles = files.ForWorkspace(scope.Workspace.Id);
        try
        {
            byteReservation = manager.ReserveUsage(Db, scope.Workspace, subscription, scope.UserId,
                "storage.bytes", maximumBytes, request.IdempotencyKey + ":bytes", new { name }.ToJson());
            row = new StoredFile {
                IdempotencyKey = request.IdempotencyKey, Name = name,
                ObjectKey = workspaceFiles.NewFileKey(),
                ContentType = upload.ContentType ?? "application/octet-stream", ByteLength = maximumBytes,
                Status = StoredFileStatus.Pending, UploadedBy = scope.UserId,
            };
            Db.Insert(row);
            var stored = await workspaceFiles.WriteAsync(row.ObjectKey, upload.InputStream, maximumBytes);
            manager.SettleUsage(Db, scope.Workspace, subscription, scope.UserId, byteReservation.Id, stored.ByteLength);
            manager.SettleUsage(Db, scope.Workspace, subscription, scope.UserId, documentReservation.Id, 1);
            row.ByteLength = stored.ByteLength;
            row.Sha256 = stored.Sha256;
            row.Status = StoredFileStatus.Available;
            Db.Update(row);
            Audit(scope, "file.uploaded", row.Id, new { row.Name, row.ByteLength, row.ContentType });
            try
            {
                // This period counter is analytical rather than an admission control for
                // the stored object. A transient metering failure must not turn a fully
                // persisted, quota-accounted file into a failed upload.
                manager.RecordUsage(Db, scope.Workspace, subscription, scope.UserId, new RecordUsage {
                    MeterKey = "documents.uploaded", Units = 1, IdempotencyKey = request.IdempotencyKey + ":uploaded",
                    MetadataJson = new { fileId = row.Id }.ToJson(),
                });
            }
            catch (Exception ex)
            {
                Db.Insert(new SaasAuditEvent {
                    Category = "usage", Action = "upload-counter.failed",
                    UserId = scope.UserId, SubjectId = row.Id, Outcome = "Failed", Reason = Truncate(ex.Message, 1000),
                    CreatedDate = DateTime.UtcNow,
                });
            }
            return ToInfo(row);
        }
        catch (Exception ex)
        {
            CompensateReservation(subscription, scope, byteReservation, request.IdempotencyKey + ":cleanup:bytes");
            CompensateReservation(subscription, scope, documentReservation, request.IdempotencyKey + ":cleanup:document");
            if (row != null)
            {
                try { await workspaceFiles.DeleteAsync(row.ObjectKey); } catch { /* Retain the original upload error. */ }
                row.Status = StoredFileStatus.Failed;
                row.LastError = Truncate(ex.Message, 1000);
                Db.Update(row);
            }
            throw;
        }
    }

    public async Task<object> Any(DownloadStoredFile request)
    {
        var scope = Db.GetWorkspaceScope();
        RequireFiles(scope);
        var row = Db.Single<StoredFile>(x => x.Id == request.Id && x.Status == StoredFileStatus.Available)
            ?? throw new HttpError(404, "StoredFileNotFound", "The file was not found.");
        var stream = await files.ForWorkspace(scope.Workspace.Id).OpenReadAsync(row.ObjectKey);
        Audit(scope, "file.downloaded", row.Id);
        return new HttpResult(stream, row.ContentType) {
            Headers = { [HttpHeaders.ContentDisposition] = $"attachment; filename=\"{SafeDownloadName(row.Name)}\"" },
        };
    }

    public async Task<object> Any(DeleteStoredFile request)
    {
        var scope = Db.GetWorkspaceScope();
        RequireFiles(scope);
        var row = Db.SingleById<StoredFile>(request.Id)
            ?? throw new HttpError(404, "StoredFileNotFound", "The file was not found.");
        if (row.Status is StoredFileStatus.Deleted or StoredFileStatus.Deleting) return new EmptyResponse();
        row.Status = StoredFileStatus.Deleting;
        Db.Update(row);
        Audit(scope, "file.deletion-requested", row.Id);
        jobs.EnqueueForWorkspace<DeleteStoredFileCommand>(scope.Workspace.Id, new DeleteStoredFileWork { WorkspaceId = scope.Workspace.Id, FileId = row.Id });
        return new EmptyResponse();
    }

    public async Task<object> Any(DownloadWorkspaceExport request)
    {
        var scope = Db.GetWorkspaceScope();
        if (!scope.IsAdmin) throw new HttpError(403, "WorkspaceAdminRequired", "Organization Owner or Admin role is required.");
        var artifact = Db.Single<DataExportArtifact>(x => x.Id == request.Id && x.ExpiresAt > DateTime.UtcNow)
            ?? throw new HttpError(404, "ExportNotFound", "The export was not found or has expired.");
        var stream = await files.ForWorkspace(scope.Workspace.Id).OpenReadAsync(artifact.ObjectKey);
        artifact.DownloadedAt = DateTime.UtcNow;
        Db.Update(artifact);
        Audit(scope, "export.downloaded", artifact.Id);
        return new HttpResult(stream, "application/zip") {
            Headers = { [HttpHeaders.ContentDisposition] = $"attachment; filename=\"{scope.Workspace.Slug}-export.zip\"" },
        };
    }

    // Whether the organization's state allows the request was decided when its connection was opened
    private void RequireFiles(WorkspaceScope scope)
    {
        var subscription = Db.GetSubscription();
        if (!entitlements.HasFeature(Db, scope.Workspace, subscription, "files.basic"))
            throw new HttpError(403, "FeatureNotEntitled", "File storage is not included in the current plan.");
    }

    private void Audit(WorkspaceScope scope, string action, string subjectId, object? detail = null) => Db.Insert(new SaasAuditEvent {
        Category = "storage", Action = action, UserId = scope.UserId,
        SubjectId = subjectId, DetailJson = detail?.ToJson(), IpAddress = Request?.RemoteIp,
        UserAgent = Request?.UserAgent, CreatedDate = DateTime.UtcNow,
    });

    private void CompensateReservation(BillingSubscription subscription, WorkspaceScope scope,
        UsageReservation? reservation, string adjustmentKey)
    {
        if (reservation == null) return;
        try
        {
            var current = Db.SingleById<UsageReservation>(reservation.Id);
            if (current?.Status == UsageReservationStatus.Pending)
            {
                manager.ReleaseUsage(Db, scope.Workspace, subscription, scope.UserId, current.Id);
            }
            else if (current?.Status == UsageReservationStatus.Settled && current.SettledUnits > 0)
            {
                manager.AdjustGauge(Db, scope.Workspace, subscription, scope.UserId, current.MeterKey,
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

// Work for one organization says which, so the command can confine its connection before it reads anything
public class DeleteStoredFileWork
{
    public string WorkspaceId { get; set; } = default!;
    public string FileId { get; set; } = default!;
}

[Worker("storage")]
public class DeleteStoredFileCommand(
    IDbConnectionFactory dbFactory,
    IFileStore files,
    ISaasManager manager) : AsyncCommand<DeleteStoredFileWork>
{
    protected override async Task RunAsync(DeleteStoredFileWork request, CancellationToken token)
    {
        using var db = dbFactory.OpenForWorkspace(request.WorkspaceId, "file-deletion-job");
        var row = db.SingleById<StoredFile>(request.FileId);
        if (row == null || row.Status == StoredFileStatus.Deleted) return;
        // The file is deleted for the user who requested it
        var requestedBy = row.ModifiedBy;
        using var _ = db.WithUserId(requestedBy);
        var workspace = db.SingleById<Workspace>(request.WorkspaceId);
        var subscription = db.GetSubscription();
        try
        {
            await files.ForWorkspace(request.WorkspaceId).DeleteAsync(row.ObjectKey, token);
            manager.AdjustGauge(db, workspace, subscription, requestedBy, "storage.bytes", -row.ByteLength, $"file:{row.Id}:delete:bytes", "file-delete");
            manager.AdjustGauge(db, workspace, subscription, requestedBy, "documents.stored", -1, $"file:{row.Id}:delete:document", "file-delete");
            row.Status = StoredFileStatus.Deleted;
            row.DeletedDate = DateTime.UtcNow;
            row.LastError = null;
            db.Update(row);
            db.Insert(new SaasAuditEvent { Category = "storage", Action = "file.deleted", UserId = requestedBy, SubjectId = row.Id, CreatedDate = DateTime.UtcNow });
        }
        catch (Exception ex)
        {
            row.Status = StoredFileStatus.Deleting;
            row.LastError = ex.Message.Length <= 1000 ? ex.Message : ex.Message[..1000];
            db.Update(row);
            throw;
        }
    }
}

[Worker("lifecycle")]
public class ProcessWorkspaceLifecycleCommand(
    IDbConnectionFactory dbFactory,
    IFileStore files,
    SaasConfig config) : AsyncCommand<ProcessWorkspaceLifecycle>
{
    protected override async Task RunAsync(ProcessWorkspaceLifecycle request, CancellationToken token)
    {
        // Exporting and deleting an organization runs on a connection that can't reach any other
        using var db = dbFactory.OpenForWorkspace(request.WorkspaceId, "lifecycle-job");
        var operation = db.SingleById<WorkspaceLifecycleRequest>(request.RequestId);
        if (operation == null || operation.Status is LifecycleRequestStatus.Completed or LifecycleRequestStatus.Canceled) return;
        if (operation.ScheduledAt != null && operation.ScheduledAt > DateTime.UtcNow) return;
        var workspace = db.SingleById<Workspace>(request.WorkspaceId);
        operation.Status = LifecycleRequestStatus.Processing;
        db.Update(operation);
        try
        {
            if (operation.Type == LifecycleRequestType.Export)
                await ExportAsync(db, workspace, operation, token);
            else if (operation.Type == LifecycleRequestType.Delete)
                await DeleteAsync(db, workspace, operation, token);
            operation.Status = LifecycleRequestStatus.Completed;
            operation.CompletedAt = DateTime.UtcNow;
            operation.LastError = null;
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
            db.Update(operation);
            throw;
        }
    }

    private async Task ExportAsync(IDbConnection db, Workspace workspace, WorkspaceLifecycleRequest operation, CancellationToken token)
    {
        if (db.Exists<DataExportArtifact>(x => x.LifecycleRequestId == operation.Id)) return;
        var workspaceFiles = files.ForWorkspace(workspace.Id);
        var storedFiles = db.Select<StoredFile>(x => x.Status == StoredFileStatus.Available);
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
                    // Every table the organization owns is exported, see WorkspaceData
                    var tables = WorkspaceData.ExportJson(db).Select(x => $"{x.Key.ToJson()}:{x.Value}");
                    await writer.WriteAsync("{" +
                        $"\"exportedAt\":{DateTime.UtcNow.ToJson()}," +
                        $"\"workspace\":{workspace.ToJson()}," +
                        $"\"tables\":{{{string.Join(",", tables)}}}" +
                        "}");
                }
                foreach (var row in storedFiles)
                {
                    var entry = archive.CreateEntry($"files/{row.Id}-{Path.GetFileName(row.Name)}", CompressionLevel.Fastest);
                    await using var entryStream = entry.Open();
                    await using var source = await workspaceFiles.OpenReadAsync(row.ObjectKey, token);
                    await source.CopyToAsync(entryStream, token);
                }
            }
            await using var input = new FileStream(temporaryPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
            var objectKey = workspaceFiles.ExportKey(operation.Id);
            var stored = await workspaceFiles.WriteAsync(objectKey, input, long.MaxValue, token);
            var now = DateTime.UtcNow;
            db.Insert(new DataExportArtifact {
                LifecycleRequestId = operation.Id, ObjectKey = objectKey,
                ByteLength = stored.ByteLength, Sha256 = stored.Sha256, ExpiresAt = now.AddDays(config.ExportExpiryDays),
            });
            db.Insert(new SaasAuditEvent { Category = "lifecycle", Action = "export.completed", UserId = "lifecycle-job", SubjectId = operation.Id, CreatedDate = now });
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private async Task DeleteAsync(IDbConnection db, Workspace workspace, WorkspaceLifecycleRequest operation, CancellationToken token)
    {
        if (workspace.Status == WorkspaceStatus.Deleted) return;
        if (db.SingleById<WorkspaceRetentionPolicy>(workspace.Id)?.LegalHold == true)
        {
            operation.Status = LifecycleRequestStatus.Blocked;
            operation.LastError = "Organization deletion is blocked by a legal hold.";
            db.Update(operation);
            throw new InvalidOperationException(operation.LastError);
        }
        var workspaceFiles = files.ForWorkspace(workspace.Id);
        foreach (var row in db.Select<StoredFile>())
            await workspaceFiles.DeleteAsync(row.ObjectKey, token);
        foreach (var artifact in db.Select<DataExportArtifact>())
            await workspaceFiles.DeleteAsync(artifact.ObjectKey, token);
        var now = DateTime.UtcNow;
        foreach (var apiKey in db.Select<ApiKeysFeature.ApiKey>(x => x.CancelledDate == null))
        {
            apiKey.CancelledDate = now;
            db.Update(apiKey);
        }

        // Every table the organization owns, see WorkspaceData
        var deleted = WorkspaceData.DeleteAll(db);
        // Not owned by the organization: members of it who had it selected
        db.Delete<UserWorkspacePreference>(x => x.ActiveWorkspaceId == workspace.Id);

        workspace.Name = "Deleted organization";
        workspace.Slug = $"deleted-{workspace.Id}";
        workspace.BillingEmail = null;
        workspace.Status = WorkspaceStatus.Deleted;
        db.Update(workspace);
        db.Insert(new SaasAuditEvent {
            Category = "lifecycle", Action = "deletion.completed", UserId = "lifecycle-job",
            SubjectId = operation.Id, DetailJson = new { deleted }.ToJson(), CreatedDate = now,
        });
    }
}

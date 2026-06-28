using DriveData = global::Google.Apis.Drive.v3.Data;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Upload;
using MimeKit;
using OspreyRelay.Core.Config;
using OspreyRelay.Core.Logging;
using OspreyRelay.Core.Routing;
using OspreyRelay.Core.Smtp;

namespace OspreyRelay.Workspace.Google;

/// <summary>
/// Stores email attachments (or full .eml) to Google Drive via the Drive v3 API
/// using a service account with Domain-Wide Delegation.
///
/// RouteDecision field mapping (same as GraphFileStorer):
///   OneDriveUser  → Gmail/Drive user email to impersonate (My Drive when DriveId is blank)
///   DriveId       → Shared Drive ID (null/blank = My Drive of OneDriveUser)
///   FolderPath    → Folder path (supports %variable% tokens)
/// </summary>
public class GoogleDriveFileStorer : IFileStorer
{
    private readonly ConfigManager _configManager;
    private readonly RelayLogger   _logger;

    private const long SimpleUploadThreshold = 5 * 1024 * 1024;

    public GoogleDriveFileStorer(ConfigManager configManager, RelayLogger logger)
    {
        _configManager = configManager;
        _logger        = logger;
    }

    // ── IFileStorer: email ────────────────────────────────────────────────────

    public async Task StoreAsync(ReceivedEmail received, RouteDecision decision, CancellationToken ct)
    {
        var cfg       = _configManager.Config;
        var userEmail = !string.IsNullOrWhiteSpace(decision.OneDriveUser)
            ? decision.OneDriveUser
            : cfg.ImpersonationEmail;
        var sharedDriveId = string.IsNullOrWhiteSpace(decision.DriveId) ? null : decision.DriveId;

        var svc = BuildService(userEmail);

        MimeMessage mime;
        using (var ms = new MemoryStream(received.RawData))
            mime = await MimeMessage.LoadAsync(ms, ct);

        var varCtx    = BuildVarCtx(received, mime, decision);
        var delimiter = decision.SubjectDelimiter is { Length: > 0 } d ? d : " ";
        var folderPath = PathVariableResolver.ResolvePath(decision.FolderPath, varCtx, delimiter);

        var folderId = await EnsureFolderPathAsync(
            svc, folderPath, sharedDriveId, cfg.CreateMissingFolders, ct);

        var saveWhat = decision.SaveWhat;
        var noAtt    = decision.NoAttachmentBehavior;
        var attachments = mime.Attachments.ToList();
        bool hasAttachments = attachments.Count > 0;

        if (!hasAttachments && noAtt == NoAttachmentBehavior.Skip)
        {
            _logger.Info("[GoogleDrive] No attachments and behavior=Skip — message skipped.");
            return;
        }

        if (saveWhat == SaveWhat.FullEml || (!hasAttachments && noAtt == NoAttachmentBehavior.SaveAsEml))
        {
            var emlName = ResolveFilename(decision, varCtx, delimiter,
                mime.Subject ?? "email", ".eml");
            await UploadFileAsync(svc, folderId, emlName, "message/rfc822",
                received.RawData, sharedDriveId, ct);
            _logger.Success($"[GoogleDrive] .eml saved to {userEmail}:{folderPath}/{emlName}");
            return;
        }

        if (saveWhat == SaveWhat.AttachmentsAndBody)
        {
            var bodyText = mime.TextBody ?? mime.HtmlBody ?? "";
            if (!string.IsNullOrWhiteSpace(bodyText))
            {
                var ext      = mime.HtmlBody != null ? ".html" : ".txt";
                var bodyName = ResolveFilename(decision, varCtx, delimiter, "body", ext);
                var bytes    = System.Text.Encoding.UTF8.GetBytes(bodyText);
                await UploadFileAsync(svc, folderId, bodyName, "text/plain", bytes, sharedDriveId, ct);
            }
        }

        foreach (var part in attachments)
        {
            if (part is not MimePart mp) continue;
            if (!decision.SaveEmbeddedImages && IsEmbeddedImage(mp)) continue;

            var origName = mp.FileName ?? "attachment";
            char? spaceReplace = decision.FilenameSpaceReplacement is { Length: > 0 } sr ? sr[0] : null;
            string attName;
            if (!string.IsNullOrWhiteSpace(decision.FilenameTemplate))
                attName = PathVariableResolver.ResolveFilename(
                    decision.FilenameTemplate, varCtx, origName, delimiter, spaceReplace);
            else
                attName = origName;

            using var ms = new MemoryStream();
            await mp.Content!.DecodeToAsync(ms, ct);
            await UploadFileAsync(svc, folderId, attName, mp.ContentType.MimeType,
                ms.ToArray(), sharedDriveId, ct);
            _logger.Success($"[GoogleDrive] '{attName}' saved to {userEmail}:{folderPath}");
        }
    }

    // ── IFileStorer: FTP raw file ─────────────────────────────────────────────

    public async Task StoreRawFileAsync(
        string originalFilename, byte[] data, FtpRouteDecision decision,
        PathVariableContext varCtx, CancellationToken ct)
    {
        var cfg           = _configManager.Config;
        var userEmail     = !string.IsNullOrWhiteSpace(decision.OneDriveUser)
            ? decision.OneDriveUser
            : cfg.ImpersonationEmail;
        var sharedDriveId = string.IsNullOrWhiteSpace(decision.DriveId) ? null : decision.DriveId;

        var svc = BuildService(userEmail);

        var folderPath = PathVariableResolver.ResolvePath(decision.FolderPath, varCtx);
        var folderId   = await EnsureFolderPathAsync(
            svc, folderPath, sharedDriveId, cfg.CreateMissingFolders, ct);

        string filename = originalFilename;
        if (!string.IsNullOrWhiteSpace(decision.FilenameTemplate))
            filename = PathVariableResolver.ResolveFilename(
                decision.FilenameTemplate, varCtx, originalFilename);

        await UploadFileAsync(svc, folderId, filename, "application/octet-stream",
            data, sharedDriveId, ct);
        _logger.Success($"[GoogleDrive] FTP file '{filename}' stored to {userEmail}:{folderPath}");
    }

    // ── Drive helpers ─────────────────────────────────────────────────────────

    private DriveService BuildService(string impersonateEmail)
    {
        var cfg  = _configManager.Config;
        var cred = WorkspaceCredentialProvider.ForDrive(cfg.ServiceAccountKeyPath, impersonateEmail);
        return new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = cred,
            ApplicationName       = "OspreyRelayWorkspace"
        });
    }

    private async Task<string> EnsureFolderPathAsync(
        DriveService svc, string folderPath, string? sharedDriveId,
        bool createMissing, CancellationToken ct)
    {
        var segments = folderPath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var parentId = sharedDriveId ?? "root";

        foreach (var segment in segments)
        {
            var query = $"name='{EscapeQuery(segment)}' " +
                        $"and mimeType='application/vnd.google-apps.folder' " +
                        $"and '{parentId}' in parents " +
                        $"and trashed=false";

            var listReq = svc.Files.List();
            listReq.Q                         = query;
            listReq.Fields                    = "files(id,name)";
            listReq.IncludeItemsFromAllDrives = sharedDriveId != null;
            listReq.SupportsAllDrives         = sharedDriveId != null;
            listReq.DriveId  = sharedDriveId;
            listReq.Corpora  = sharedDriveId != null ? "drive" : "user";

            var result = await listReq.ExecuteAsync(ct);
            if (result.Files.Count > 0)
            {
                parentId = result.Files[0].Id;
                continue;
            }

            if (!createMissing)
                throw new DirectoryNotFoundException(
                    $"[GoogleDrive] Folder '{segment}' not found and CreateMissingFolders is disabled.");

            var folder = new DriveData.File
            {
                Name     = segment,
                MimeType = "application/vnd.google-apps.folder",
                Parents  = [parentId]
            };
            var createReq = svc.Files.Create(folder);
            createReq.SupportsAllDrives = sharedDriveId != null;
            createReq.Fields = "id";
            var created = await createReq.ExecuteAsync(ct);
            parentId = created.Id;
        }

        return parentId;
    }

    private async Task UploadFileAsync(
        DriveService svc, string folderId, string filename, string mimeType,
        byte[] data, string? sharedDriveId, CancellationToken ct)
    {
        var meta = new DriveData.File
        {
            Name    = filename,
            Parents = [folderId]
        };

        using var stream = new MemoryStream(data);
        var req = svc.Files.Create(meta, stream, mimeType);
        req.SupportsAllDrives = sharedDriveId != null;
        req.Fields = "id";
        if (data.Length > SimpleUploadThreshold)
            req.ChunkSize = ResumableUpload.MinimumChunkSize * 4;
        await req.UploadAsync(ct);
    }

    // ── Misc helpers ──────────────────────────────────────────────────────────

    private static PathVariableContext BuildVarCtx(
        ReceivedEmail received, MimeMessage mime, RouteDecision decision)
    {
        var mimeFrom  = mime.From.Mailboxes.FirstOrDefault()?.Address ?? received.EnvelopeFrom;
        var matchedTo = !string.IsNullOrWhiteSpace(decision.DeliveryToAddress)
            ? decision.DeliveryToAddress
            : string.IsNullOrWhiteSpace(decision.MatchedToAddress)
                ? (received.EnvelopeTo.FirstOrDefault() ?? "")
                : decision.MatchedToAddress;
        var atFrom    = mimeFrom.IndexOf('@');
        var atTo      = matchedTo.IndexOf('@');
        var receivedAt = received.ReceivedAt;

        return new PathVariableContext
        {
            From          = mimeFrom,
            FromUpn       = atFrom > 0 ? mimeFrom[..atFrom] : mimeFrom,
            FromDomain    = atFrom > 0 ? mimeFrom[(atFrom + 1)..] : "",
            To            = matchedTo,
            ToUpn         = atTo > 0 ? matchedTo[..atTo] : matchedTo,
            ToDomain      = atTo > 0 ? matchedTo[(atTo + 1)..] : "",
            ToBaseDomain  = decision.ToBaseDomain,
            Suffix        = decision.CapturedSuffix,
            Subject       = mime.Subject ?? "",
            Date          = receivedAt.ToString("yyyy-MM-dd"),
            DateTime      = receivedAt.ToString("yyyy-MM-dd_HHmmss"),
            RegexCaptures = decision.RegexCaptures
        };
    }

    private static string ResolveFilename(
        RouteDecision decision, PathVariableContext varCtx,
        string delimiter, string originalName, string ext)
    {
        char? spaceReplace = decision.FilenameSpaceReplacement is { Length: > 0 } sr ? sr[0] : null;
        var basePlusExt    = Path.GetFileNameWithoutExtension(originalName) + ext;
        if (!string.IsNullOrWhiteSpace(decision.FilenameTemplate))
            return PathVariableResolver.ResolveFilename(
                decision.FilenameTemplate, varCtx, basePlusExt, delimiter, spaceReplace);
        return basePlusExt;
    }

    private static bool IsEmbeddedImage(MimePart part) =>
        part.ContentDisposition?.Disposition == ContentDisposition.Inline
        && part.ContentType.MediaType.Equals("image", StringComparison.OrdinalIgnoreCase);

    private static string EscapeQuery(string value) =>
        value.Replace("\\", "\\\\").Replace("'", "\\'");
}

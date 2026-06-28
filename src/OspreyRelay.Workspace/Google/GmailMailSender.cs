using Google.Apis.Auth.OAuth2;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Services;
using MimeKit;
using OspreyRelay.Core.Config;
using OspreyRelay.Core.Logging;
using OspreyRelay.Core.Smtp;

namespace OspreyRelay.Workspace.Google;

/// <summary>
/// Sends email via the Gmail API using a service account with Domain-Wide Delegation.
/// The impersonation user is resolved from RelayConfig.ImpersonationEmail (admin mailbox)
/// or the rule's RelayVia override.
/// </summary>
public class GmailMailSender : IMailSender
{
    private readonly ConfigManager _configManager;
    private readonly RelayLogger   _logger;
    private GoogleCredential       _credential;

    public GmailMailSender(ConfigManager configManager, RelayLogger logger)
    {
        _configManager = configManager;
        _logger        = logger;
        _credential    = BuildCredential();
    }

    public void RefreshClient()
    {
        _credential = BuildCredential();
        _logger.Info("[Gmail] Credentials refreshed.");
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        var token = await _credential.UnderlyingCredential.GetAccessTokenForRequestAsync(
            cancellationToken: ct);
        return token;
    }

    public async Task SendAsync(ReceivedEmail email, RouteDecision decision, CancellationToken ct)
    {
        var cfg          = _configManager.Config;
        var senderEmail  = !string.IsNullOrWhiteSpace(decision.RelayVia)
            ? decision.RelayVia
            : !string.IsNullOrWhiteSpace(cfg.FallbackSenderEmail)
                ? cfg.FallbackSenderEmail
                : cfg.ImpersonationEmail;

        var userId = cfg.ImpersonationEmail;

        var mime = MimeMessage.Load(new MemoryStream(email.RawData));

        // Ensure From: is set to the service-account-impersonated sender
        if (!string.IsNullOrWhiteSpace(cfg.FallbackSenderEmail))
        {
            mime.From.Clear();
            mime.From.Add(MailboxAddress.Parse(cfg.FallbackSenderEmail));
        }

        if (decision.DeliveryToAddress != null)
        {
            var target = MailboxAddress.Parse(decision.DeliveryToAddress);
            mime.To.Clear();
            mime.To.Add(target);
            if (decision.RewriteToHeader)
            {
                mime.To.Clear();
                mime.To.Add(target);
            }
        }

        using var ms = new MemoryStream();
        await mime.WriteToAsync(ms, ct);
        var rawBase64 = WebSafeBase64(ms.ToArray());

        var svc = BuildService(userId);
        var request = svc.Users.Messages.Send(
            new Message { Raw = rawBase64 }, userId);

        await request.ExecuteAsync(ct);
        _logger.Success(
            $"[Gmail] Sent: {email.EnvelopeFrom} → {string.Join(",", email.EnvelopeTo)} via {userId}");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private GoogleCredential BuildCredential()
    {
        var cfg = _configManager.Config;
        if (!cfg.IsWorkspaceConfigured)
            throw new InvalidOperationException(
                "Workspace not configured — set ServiceAccountKeyPath and ImpersonationEmail.");

        return WorkspaceCredentialProvider.ForGmail(
            cfg.ServiceAccountKeyPath, cfg.ImpersonationEmail);
    }

    private GmailService BuildService(string userId)
    {
        var cred = File.Exists(_configManager.Config.ServiceAccountKeyPath)
            ? WorkspaceCredentialProvider.ForGmail(
                _configManager.Config.ServiceAccountKeyPath, userId)
            : throw new InvalidOperationException("Service account key file not found.");

        return new GmailService(new BaseClientService.Initializer
        {
            HttpClientInitializer = cred,
            ApplicationName       = "OspreyRelayWorkspace"
        });
    }

    private static string WebSafeBase64(byte[] data) =>
        Convert.ToBase64String(data)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
}

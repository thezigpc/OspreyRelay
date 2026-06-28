using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Gmail.v1;

namespace OspreyRelay.Workspace.Google;

/// <summary>
/// Loads a service account JSON key and creates per-user scoped credentials
/// via Domain-Wide Delegation (DWD).
/// </summary>
public static class WorkspaceCredentialProvider
{
    private static readonly string[] GmailScopes  = [GmailService.Scope.GmailSend];
    private static readonly string[] DriveScopes  = [DriveService.Scope.Drive];
    private static readonly string[] CombinedScopes =
        [GmailService.Scope.GmailSend, DriveService.Scope.Drive];

    public static GoogleCredential ForGmail(string keyFilePath, string impersonatedEmail) =>
        Load(keyFilePath, GmailScopes, impersonatedEmail);

    public static GoogleCredential ForDrive(string keyFilePath, string impersonatedEmail) =>
        Load(keyFilePath, DriveScopes, impersonatedEmail);

    public static GoogleCredential ForAll(string keyFilePath, string impersonatedEmail) =>
        Load(keyFilePath, CombinedScopes, impersonatedEmail);

    private static GoogleCredential Load(string path, string[] scopes, string user)
    {
        using var stream = File.OpenRead(path);
        return GoogleCredential
            .FromStream(stream)
            .CreateScoped(scopes)
            .CreateWithUser(user);
    }
}

using OspreyRelay.Core.Config;
using OspreyRelay.Core.Logging;
using OspreyRelay.Workspace.Google;

namespace OspreyRelay.WorkspaceApp.Forms;

/// <summary>
/// One-time setup: upload service account JSON key and set the impersonation email.
/// Tests the credential by requesting an OAuth token before accepting.
/// </summary>
public class WorkspaceSetupForm : Form
{
    private readonly ConfigManager _configManager;
    private readonly RelayLogger   _logger;

    private TextBox  _txtKeyPath    = null!;
    private TextBox  _txtImpEmail   = null!;
    private Label    _lblStatus     = null!;
    private Button   _btnTest       = null!;
    private Button   _btnSave       = null!;

    public WorkspaceSetupForm(ConfigManager configManager, RelayLogger logger)
    {
        _configManager = configManager;
        _logger        = logger;
        InitializeComponent();
        LoadExisting();
    }

    private void InitializeComponent()
    {
        Text            = "Workspace Setup";
        ClientSize      = new Size(580, 400);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox     = false;
        StartPosition   = FormStartPosition.CenterParent;

        var scroll = new Panel { Dock = DockStyle.Fill, Padding = new Padding(18, 14, 18, 10) };
        int y = 14;

        scroll.Controls.Add(new Label
        {
            Text = "Configure Osprey Relay for Workspace with a Google service account.\n\n" +
                   "Prerequisites:\n" +
                   "  1. Create a service account in Google Cloud Console\n" +
                   "  2. Enable Domain-Wide Delegation on the service account\n" +
                   "  3. In Google Workspace Admin, grant the scopes:\n" +
                   "       https://www.googleapis.com/auth/gmail.send\n" +
                   "       https://www.googleapis.com/auth/drive\n" +
                   "  4. Download the JSON key file and place it on this server",
            Location = new Point(0, y), AutoSize = false, Width = 540, Height = 130,
            Font = new Font("Segoe UI", 9), ForeColor = Color.FromArgb(40, 40, 60)
        });
        y += 136;

        // Key file path
        scroll.Controls.Add(new Label { Text = "Service account JSON key file:", Location = new Point(0, y), AutoSize = true, Font = new Font("Segoe UI", 9) });
        y += 20;
        _txtKeyPath = new TextBox { Location = new Point(0, y), Width = 440, Font = new Font("Segoe UI", 9) };
        var btnBrowse = new Button
        {
            Text = "Browse…", Location = new Point(448, y - 2), Size = new Size(88, 28),
            FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = true
        };
        btnBrowse.Click += (_, _) => BrowseKeyFile();
        scroll.Controls.AddRange(new Control[] { _txtKeyPath, btnBrowse });
        y += 36;

        scroll.Controls.Add(new Label
        {
            Text = "Store this file in a secure location, e.g. %ProgramData%\\OspreyRelayWorkspace\\",
            Location = new Point(0, y), AutoSize = false, Width = 540, Height = 20,
            Font = new Font("Segoe UI", 8), ForeColor = Color.DimGray
        });
        y += 28;

        // Impersonation email
        scroll.Controls.Add(new Label { Text = "Impersonation email (admin account for domain-wide delegation):", Location = new Point(0, y), AutoSize = true, Font = new Font("Segoe UI", 9) });
        y += 20;
        _txtImpEmail = new TextBox { Location = new Point(0, y), Width = 400, Font = new Font("Segoe UI", 9), PlaceholderText = "admin@yourdomain.com" };
        scroll.Controls.Add(_txtImpEmail);
        y += 36;

        scroll.Controls.Add(new Label
        {
            Text = "Emails will be sent from FallbackSenderEmail (set in Settings). " +
                   "This account is used for impersonation and token acquisition.",
            Location = new Point(0, y), AutoSize = false, Width = 540, Height = 32,
            Font = new Font("Segoe UI", 8), ForeColor = Color.DimGray
        });
        y += 40;

        // Status
        _lblStatus = new Label
        {
            Location = new Point(0, y), AutoSize = false, Width = 540, Height = 24,
            Font = new Font("Segoe UI", 9), ForeColor = Color.DimGray
        };
        scroll.Controls.Add(_lblStatus);

        // Buttons
        _btnTest = new Button
        {
            Text = "Test Connection",
            Location = new Point(0, y + 28), Size = new Size(150, 30),
            FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = true,
            Font = new Font("Segoe UI", 9)
        };
        _btnSave = new Button
        {
            Text = "Save & Close",
            Location = new Point(160, y + 28), Size = new Size(130, 30),
            FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = true,
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        };
        var btnCancel = new Button
        {
            Text = "Cancel",
            Location = new Point(298, y + 28), Size = new Size(80, 30),
            FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = true
        };

        _btnTest.Click   += async (_, _) => await TestConnectionAsync();
        _btnSave.Click   += (_, _) => SaveAndClose();
        btnCancel.Click  += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        scroll.Controls.AddRange(new Control[] { _btnTest, _btnSave, btnCancel });

        Controls.Add(scroll);
    }

    private void LoadExisting()
    {
        var cfg = _configManager.Config;
        _txtKeyPath.Text  = cfg.ServiceAccountKeyPath;
        _txtImpEmail.Text = cfg.ImpersonationEmail;
    }

    private void BrowseKeyFile()
    {
        var initial = string.IsNullOrWhiteSpace(_txtKeyPath.Text)
            ? _configManager.GetConfigDir()
            : Path.GetDirectoryName(_txtKeyPath.Text);
        using var picker = new PathPickerDialog("Select Service Account JSON Key", initial, folderOnly: false);
        if (picker.ShowDialog(this) == DialogResult.OK && picker.SelectedFile != null)
            _txtKeyPath.Text = picker.SelectedFile;
    }

    private async Task TestConnectionAsync()
    {
        var keyPath = _txtKeyPath.Text.Trim();
        var email   = _txtImpEmail.Text.Trim();

        if (string.IsNullOrWhiteSpace(keyPath) || !File.Exists(keyPath))
        {
            SetStatus("Key file not found. Check the path.", Color.DarkOrange);
            return;
        }
        if (string.IsNullOrWhiteSpace(email))
        {
            SetStatus("Enter an impersonation email address.", Color.DarkOrange);
            return;
        }

        var cfg       = _configManager.Config;
        bool testDrive = cfg.EnableGoogleDrive;
        bool testGmail = cfg.EnableGmailRelay;

        if (!testDrive && !testGmail)
        {
            SetStatus("No services are enabled — enable Gmail or Google Drive in Settings → Services first.", Color.DarkOrange);
            return;
        }

        _btnTest.Enabled = false;
        SetStatus("Testing…", Color.DimGray);

        try
        {
            if (testDrive)
            {
                var cred  = WorkspaceCredentialProvider.ForDrive(keyPath, email);
                var token = await cred.UnderlyingCredential
                    .GetAccessTokenForRequestAsync(cancellationToken: CancellationToken.None);
                if (string.IsNullOrWhiteSpace(token))
                    throw new InvalidOperationException("Received empty Drive access token.");
            }

            if (testGmail)
            {
                var cred  = WorkspaceCredentialProvider.ForGmail(keyPath, email);
                var token = await cred.UnderlyingCredential
                    .GetAccessTokenForRequestAsync(cancellationToken: CancellationToken.None);
                if (string.IsNullOrWhiteSpace(token))
                    throw new InvalidOperationException("Received empty Gmail access token.");
            }

            var tested = string.Join(" + ", new[] { testDrive ? "Drive" : null, testGmail ? "Gmail" : null }
                .Where(s => s != null));
            SetStatus($"Connection successful — {tested} credentials valid.", Color.FromArgb(40, 140, 40));
            _logger.Success($"[Setup] Workspace credential test passed ({tested}) for {email}");
        }
        catch (Exception ex)
        {
            SetStatus($"Test failed: {ex.Message}", Color.DarkRed);
            _logger.Error($"[Setup] Credential test failed: {ex.Message}");
        }
        finally
        {
            _btnTest.Enabled = true;
        }
    }

    private void SaveAndClose()
    {
        var keyPath = _txtKeyPath.Text.Trim();
        var email   = _txtImpEmail.Text.Trim();

        if (string.IsNullOrWhiteSpace(keyPath) || !File.Exists(keyPath))
        {
            SetStatus("Key file not found. Check the path.", Color.DarkOrange);
            return;
        }
        if (string.IsNullOrWhiteSpace(email))
        {
            SetStatus("Enter an impersonation email address.", Color.DarkOrange);
            return;
        }

        var cfg = _configManager.Config;
        cfg.ServiceAccountKeyPath = keyPath;
        cfg.ImpersonationEmail    = email;
        _configManager.Save(cfg);

        _logger.Info($"[Setup] Workspace credentials saved. Impersonation: {email}");
        DialogResult = DialogResult.OK;
        Close();
    }

    private void SetStatus(string text, Color color)
    {
        _lblStatus.Text      = text;
        _lblStatus.ForeColor = color;
    }
}

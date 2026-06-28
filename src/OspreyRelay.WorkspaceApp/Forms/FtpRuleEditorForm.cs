using OspreyRelay.Core.Config;
using OspreyRelay.Core.Logging;

namespace OspreyRelay.WorkspaceApp.Forms;

/// <summary>
/// Editor for a single FtpRoutingRule. Google Drive variant — supports My Drive and Shared Drive.
/// </summary>
public class FtpRuleEditorForm : Form
{
    public FtpRoutingRule? Result { get; private set; }

    private readonly FtpRoutingRule? _edit;
    private readonly ConfigManager   _configManager;
    private readonly RelayLogger     _logger;

    // ── Match controls ────────────────────────────────────────────────────────
    private TextBox  _txtName        = null!;
    private CheckBox _chkEnabled     = null!;
    private TextBox  _txtVirtualPath = null!;
    private TextBox  _txtUsername    = null!;

    // ── Destination controls ──────────────────────────────────────────────────
    private RadioButton _rdoMyDrive     = null!;
    private RadioButton _rdoSharedDrive = null!;
    private Panel       _pnlMyDrive     = null!;
    private Panel       _pnlSharedDrive = null!;

    // My Drive
    private TextBox _txtDriveUser = null!;

    // Shared Drive
    private TextBox _txtSharedDriveId   = null!;
    private TextBox _txtSharedDriveUser = null!;

    // ── Common path controls ──────────────────────────────────────────────────
    private TextBox _txtFolderPath       = null!;
    private TextBox _txtFilenameTemplate = null!;

    private Panel _scroll = null!;

    public FtpRuleEditorForm(FtpRoutingRule? edit, ConfigManager configManager, RelayLogger logger)
    {
        _edit          = edit;
        _configManager = configManager;
        _logger        = logger;

        InitializeComponent();
        if (edit != null) PopulateFromEdit(edit);
        UpdateDestinationPanels();
    }

    private void InitializeComponent()
    {
        Text            = _edit == null ? "Add FTP Rule" : "Edit FTP Rule";
        Size            = new Size(560, 580);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox     = false;
        MinimizeBox     = false;
        StartPosition   = FormStartPosition.CenterParent;

        _scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(10, 10, 10, 0) };

        int y = 0;

        // ── Identity ──────────────────────────────────────────────────────────
        AddSectionLabel(_scroll, "Identity", ref y);
        _chkEnabled = AddCheckBox(_scroll, "Enabled", ref y, true);
        _txtName    = AddLabeledText(_scroll, "Name:", ref y, "", tip: null);

        // ── Match ─────────────────────────────────────────────────────────────
        AddSectionLabel(_scroll, "Match", ref y);
        _txtVirtualPath = AddLabeledText(_scroll, "Virtual path prefix:", ref y, "/",
            tip: "FTP directory that triggers this rule. Use / to match everything.");
        _txtUsername = AddLabeledText(_scroll, "Username (blank = any):", ref y, "", tip: null);

        // ── Destination type ──────────────────────────────────────────────────
        AddSectionLabel(_scroll, "Destination", ref y);
        _rdoMyDrive     = new RadioButton { Text = "Google Drive – My Drive",     Location = new Point(0, y),   AutoSize = true, Checked = true };
        _rdoSharedDrive = new RadioButton { Text = "Google Drive – Shared Drive", Location = new Point(200, y), AutoSize = true };
        _rdoMyDrive.CheckedChanged += (_, _) => UpdateDestinationPanels();
        _scroll.Controls.AddRange(new Control[] { _rdoMyDrive, _rdoSharedDrive });
        y += 28;

        // My Drive panel
        _pnlMyDrive = new Panel { Location = new Point(0, y), Width = 520, Height = 58 };
        {
            int py = 2;
            _txtDriveUser = AddLabeledTextInPanel(_pnlMyDrive, "User email:", ref py,
                "(blank = use ImpersonationEmail from config)");
            _pnlMyDrive.Controls.Add(new Label
            {
                Text      = "Leave blank to use the global impersonation email from workspace config.",
                Location  = new Point(4, py),
                Width     = 510,
                Font      = new Font("Segoe UI", 7.5f),
                ForeColor = Color.Gray,
                AutoSize  = false
            });
        }

        // Shared Drive panel
        _pnlSharedDrive = new Panel { Location = new Point(0, y), Width = 520, Height = 92, Visible = false };
        {
            int spy = 2;
            _txtSharedDriveUser = AddLabeledTextInPanel(_pnlSharedDrive, "User email:", ref spy,
                "(blank = use ImpersonationEmail from config)");
            _txtSharedDriveId = AddLabeledTextInPanel(_pnlSharedDrive, "Shared Drive ID:", ref spy,
                "e.g. 0ABC1234...");
            _pnlSharedDrive.Controls.Add(new Label
            {
                Text      = "Find the Shared Drive ID in the URL when browsing the drive in Google Drive.",
                Location  = new Point(4, spy),
                Width     = 510,
                Font      = new Font("Segoe UI", 7.5f),
                ForeColor = Color.Gray,
                AutoSize  = false
            });
        }

        _scroll.Controls.Add(_pnlMyDrive);
        _scroll.Controls.Add(_pnlSharedDrive);
        y += _pnlSharedDrive.Height + 8;

        // ── Storage path ──────────────────────────────────────────────────────
        AddSectionLabel(_scroll, "Storage path", ref y);
        _txtFolderPath = AddLabeledText(_scroll, "Folder path:", ref y, "/FtpRelay/%username%",
            tip: "Variables: %username%, %date%, %datetime%, %ftppath%");
        _txtFilenameTemplate = AddLabeledText(_scroll, "Filename template:", ref y, "",
            tip: "Optional. Variables: %filename%, %date%, %username%. Blank = keep original name.");

        // ── OK / Cancel ───────────────────────────────────────────────────────
        var btnRow = new FlowLayoutPanel
        {
            Dock          = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height        = 42,
            Padding       = new Padding(0, 6, 8, 0)
        };
        var btnOk     = new Button { Text = "OK",     DialogResult = DialogResult.OK,     Size = new Size(80, 28) };
        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Size = new Size(80, 28) };
        btnOk.Click   += (_, _) => TrySave();
        AcceptButton   = btnOk;
        CancelButton   = btnCancel;
        btnRow.Controls.AddRange(new Control[] { btnCancel, btnOk });

        Controls.Add(_scroll);
        Controls.Add(btnRow);
    }

    // ── Populate from existing rule ───────────────────────────────────────────

    private void PopulateFromEdit(FtpRoutingRule rule)
    {
        _txtName.Text        = rule.Name;
        _chkEnabled.Checked  = rule.Enabled;
        _txtVirtualPath.Text = rule.VirtualPath;
        _txtUsername.Text    = rule.Username;

        bool isShared = !string.IsNullOrWhiteSpace(rule.LibraryDriveId);
        if (isShared)
        {
            _rdoSharedDrive.Checked   = true;
            _txtSharedDriveUser.Text  = rule.OneDriveUser ?? "";
            _txtSharedDriveId.Text    = rule.LibraryDriveId;
        }
        else
        {
            _rdoMyDrive.Checked  = true;
            _txtDriveUser.Text   = rule.OneDriveUser ?? "";
        }

        _txtFolderPath.Text       = rule.FolderPath;
        _txtFilenameTemplate.Text = rule.FilenameTemplate ?? "";
    }

    private void UpdateDestinationPanels()
    {
        _pnlMyDrive.Visible     = _rdoMyDrive.Checked;
        _pnlSharedDrive.Visible = _rdoSharedDrive.Checked;
    }

    // ── Save ──────────────────────────────────────────────────────────────────

    private void TrySave()
    {
        var vpath = _txtVirtualPath.Text.Trim();
        if (string.IsNullOrWhiteSpace(vpath)) vpath = "/";
        if (!vpath.StartsWith('/')) vpath = "/" + vpath;

        var rule = new FtpRoutingRule
        {
            Id               = _edit?.Id ?? Guid.NewGuid().ToString("N")[..8],
            Enabled          = _chkEnabled.Checked,
            Name             = _txtName.Text.Trim(),
            VirtualPath      = vpath,
            Username         = _txtUsername.Text.Trim(),
            DestinationType  = FileDestinationType.GoogleDrive,
            FolderPath       = _txtFolderPath.Text.Trim(),
            FilenameTemplate = string.IsNullOrWhiteSpace(_txtFilenameTemplate.Text)
                ? null : _txtFilenameTemplate.Text.Trim()
        };

        if (_rdoSharedDrive.Checked)
        {
            var driveId = _txtSharedDriveId.Text.Trim();
            if (string.IsNullOrWhiteSpace(driveId))
            {
                MessageBox.Show("Enter the Shared Drive ID.", "Validation",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            rule.OneDriveUser   = _txtSharedDriveUser.Text.Trim();
            rule.LibraryDriveId = driveId;
        }
        else
        {
            rule.OneDriveUser = _txtDriveUser.Text.Trim();
        }

        Result       = rule;
        DialogResult = DialogResult.OK;
    }

    // ── Layout helpers ────────────────────────────────────────────────────────

    private static void AddSectionLabel(Panel p, string text, ref int y)
    {
        p.Controls.Add(new Label
        {
            Text      = text,
            Location  = new Point(0, y),
            AutoSize  = true,
            Font      = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(40, 100, 60)
        });
        y += 24;
    }

    private static CheckBox AddCheckBox(Panel p, string text, ref int y, bool defaultChecked)
    {
        var cb = new CheckBox { Text = text, Location = new Point(0, y), AutoSize = true, Checked = defaultChecked };
        p.Controls.Add(cb);
        y += 26;
        return cb;
    }

    private static TextBox AddLabeledText(Panel p, string label, ref int y, string defaultVal, string? tip)
    {
        p.Controls.Add(new Label { Text = label, Location = new Point(0, y + 3), AutoSize = true });
        var tb = new TextBox { Location = new Point(160, y), Width = 350, Text = defaultVal };
        p.Controls.Add(tb);
        y += 28;
        if (tip != null)
        {
            p.Controls.Add(new Label { Text = tip, Location = new Point(4, y), Width = 506, AutoSize = false,
                Font = new Font("Segoe UI", 7.5f), ForeColor = Color.Gray });
            y += 18;
        }
        return tb;
    }

    private static TextBox AddLabeledTextInPanel(Panel p, string label, ref int y, string placeholder)
    {
        p.Controls.Add(new Label { Text = label, Location = new Point(0, y + 3), AutoSize = true });
        var tb = new TextBox { Location = new Point(130, y), Width = 376, PlaceholderText = placeholder };
        p.Controls.Add(tb);
        y += 28;
        return tb;
    }
}

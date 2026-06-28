using System.Text.RegularExpressions;
using OspreyRelay.Core.Config;
using OspreyRelay.Core.Logging;

namespace OspreyRelay.WorkspaceApp.Forms;

/// <summary>
/// Editor for a single RoutingRule. Google Workspace variant — supports Gmail relay,
/// Google Drive My Drive, Google Drive Shared Drive, and Smarthost.
/// </summary>
public class FileRuleEditorForm : Form
{
    public RoutingRule? Result { get; private set; }

    private readonly RoutingRule? _editRule;
    private readonly ConfigManager _configManager;
    private readonly RelayLogger _logger;

    // ── Match mode ────────────────────────────────────────────────────────────
    private ComboBox _cboMatchMode = null!;

    // ── Match controls (rebuilt on mode change) ───────────────────────────────
    private TextBox? _txtSuffix;
    private TextBox? _txtBaseDomain;
    private TextBox? _txtPattern;
    private CheckBox? _chkCaseInsensitive;
    private TextBox? _txtTestInput;
    private Label? _lblTestResult;

    // ── Destination type ──────────────────────────────────────────────────────
    private RadioButton _rdoTypeRelay       = null!;
    private RadioButton _rdoTypeMyDrive     = null!;
    private RadioButton _rdoTypeSharedDrive = null!;
    private RadioButton _rdoTypeSmarthost   = null!;

    // Relay
    private TextBox _txtRelayVia = null!;

    // Google Drive – My Drive
    private TextBox _txtMyDriveUser = null!;
    private TextBox _txtMyDrivePath = null!;

    // Google Drive – Shared Drive
    private TextBox _txtSharedDriveUser   = null!;
    private TextBox _txtSharedDriveId     = null!;
    private TextBox _txtSharedDrivePath   = null!;

    // ── Per-rule overrides ────────────────────────────────────────────────────
    private CheckBox _chkOverrideSaveWhat      = null!;
    private ComboBox _cboSaveWhat              = null!;
    private CheckBox _chkOverrideSubfolder     = null!;
    private CheckBox _chkSubfolderValue        = null!;
    private CheckBox _chkOverrideFromSender    = null!;
    private ComboBox _cboFromSender            = null!;
    private CheckBox _chkOverrideSaveEmbedded  = null!;
    private CheckBox _chkSaveEmbeddedValue     = null!;
    private CheckBox _chkOverrideFilename      = null!;
    private TextBox  _txtFilenameTemplate      = null!;
    private CheckBox _chkOverrideDelimiter     = null!;
    private TextBox  _txtSubjectDelimiter      = null!;
    private CheckBox _chkEnabled               = null!;

    // Destination panels
    private Panel _pnlRelayDest       = null!;
    private Panel _pnlMyDriveDest     = null!;
    private Panel _pnlSharedDriveDest = null!;
    private Panel _pnlSmarthostDest   = null!;
    private CheckBox _chkSmarthostUseGlobal    = null!;
    private TextBox  _txtSmarthostHost         = null!;
    private NumericUpDown _nudSmarthostPort    = null!;
    private ComboBox _cboSmarthostTls          = null!;
    private TextBox  _txtSmarthostUser         = null!;
    private TextBox  _txtSmarthostPass         = null!;

    private CheckBox? _chkStripSuffixRelay;
    private TextBox   _txtDeliverToRelay      = null!;
    private CheckBox? _chkStripSuffixSmarthost;
    private TextBox   _txtDeliverToSmarthost  = null!;
    private CheckBox  _chkRewriteToHeader     = null!;

    private Panel _scroll     = null!;
    private int   _destPanelY;
    private int   _lastOverrideY;

    public FileRuleEditorForm(RoutingRule? editRule, ConfigManager configManager, RelayLogger logger)
    {
        _editRule      = editRule;
        _configManager = configManager;
        _logger        = logger;

        InitializeComponent();
        PopulateFromEdit();
        ApplyServiceFlags();
    }

    private MatchMode CurrentMode => _cboMatchMode.SelectedIndex switch
    {
        0 => MatchMode.DomainSuffix,
        1 => MatchMode.ExactTo,
        2 => MatchMode.RegexTo,
        3 => MatchMode.RegexFrom,
        4 => MatchMode.RegexSubject,
        _ => MatchMode.DomainSuffix
    };

    private void InitializeComponent()
    {
        Text = "Rule Editor";
        Size = new Size(700, 720);
        MinimumSize = new Size(640, 580);
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;

        var pnlTop = new Panel
        {
            Dock = DockStyle.Top, Height = 42,
            Padding = new Padding(10, 8, 10, 0),
            BackColor = Color.FromArgb(245, 245, 250)
        };
        var lblMode = new Label { Text = "Match mode:", Location = new Point(10, 12), AutoSize = true, Font = new Font("Segoe UI", 9) };
        _cboMatchMode = new ComboBox { Location = new Point(102, 9), Width = 270, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 9) };
        _cboMatchMode.Items.AddRange(new object[]
        {
            "Domain Suffix  (subdomain pattern)",
            "Exact To:  (case-insensitive address)",
            "Regex — To: address",
            "Regex — From: address",
            "Regex — Subject line"
        });
        _cboMatchMode.SelectedIndex = 0;
        _cboMatchMode.SelectedIndexChanged += (_, _) => RebuildMatchSection();
        pnlTop.Controls.AddRange(new Control[] { lblMode, _cboMatchMode });

        _scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };

        var btnSave   = new Button { Text = "Save",       Size = new Size(100, 30), Dock = DockStyle.Right, FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = true };
        var btnCancel = new Button { Text = "Cancel",     Size = new Size(100, 30), Dock = DockStyle.Right, FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = true };
        var btnVars   = new Button { Text = "Variables…", Size = new Size(100, 30), Dock = DockStyle.Left,  FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = true };
        btnSave.Click   += (_, _) => SaveRule();
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        btnVars.Click   += (_, _) => new VariablesHelpForm().ShowDialog(this);
        var pnlNav = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(245, 245, 248) };
        pnlNav.Controls.Add(btnSave);
        pnlNav.Controls.Add(btnCancel);
        pnlNav.Controls.Add(btnVars);

        var tlp = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Padding = new Padding(0), Margin = new Padding(0), CellBorderStyle = TableLayoutPanelCellBorderStyle.None };
        tlp.RowStyles.Clear(); tlp.ColumnStyles.Clear();
        tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        tlp.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        tlp.RowStyles.Add(new RowStyle(SizeType.Absolute, 46f));
        tlp.Controls.Add(_scroll, 0, 0);
        tlp.Controls.Add(pnlNav, 0, 1);

        Controls.Add(tlp);
        Controls.Add(pnlTop);

        BuildScrollContent();
    }

    private void BuildScrollContent()
    {
        _scroll.Controls.Clear();
        int y = 8;
        const int lx = 10;
        const int w  = 620;

        BuildMatchSection(lx, ref y, w);

        y += 8; Sep(_scroll, lx, y, w); y += 10;

        BoldLabel(_scroll, "Destination:", lx, y); y += 24;
        _rdoTypeRelay       = Rdo(_scroll, "Email Relay",          lx,       y);
        _rdoTypeMyDrive     = Rdo(_scroll, "Google Drive – My Drive",   lx + 120, y);
        _rdoTypeSharedDrive = Rdo(_scroll, "Google Drive – Shared Drive", lx + 290, y);
        _rdoTypeSmarthost   = Rdo(_scroll, "Smarthost",            lx + 490, y);
        _rdoTypeRelay.Checked = true;
        _rdoTypeRelay.CheckedChanged       += (_, _) => UpdateDestVisibility();
        _rdoTypeMyDrive.CheckedChanged     += (_, _) => UpdateDestVisibility();
        _rdoTypeSharedDrive.CheckedChanged += (_, _) => UpdateDestVisibility();
        _rdoTypeSmarthost.CheckedChanged   += (_, _) => UpdateDestVisibility();
        y += 30;

        _destPanelY = y;

        // Relay dest panel
        _pnlRelayDest = new Panel { Location = new Point(lx, y), Width = w };
        BuildRelayPanel();
        _scroll.Controls.Add(_pnlRelayDest);

        // My Drive panel
        _pnlMyDriveDest = new Panel { Location = new Point(lx, y), Width = w, Visible = false };
        Lbl(_pnlMyDriveDest, "User email  (blank = use impersonation email from config):", 0, 0);
        _txtMyDriveUser = Txt(_pnlMyDriveDest, 0, 18, 420, "e.g. user@company.com");
        Lbl(_pnlMyDriveDest, "Folder path  (supports %variables%):", 0, 44);
        _txtMyDrivePath = Txt(_pnlMyDriveDest, 0, 62, 520, "e.g. /EmailRelay/%date% or /%toupn%/%suffix%");
        _pnlMyDriveDest.Height = 84;
        _scroll.Controls.Add(_pnlMyDriveDest);

        // Shared Drive panel
        _pnlSharedDriveDest = new Panel { Location = new Point(lx, y), Width = w, Visible = false };
        {
            int sy = 0;
            Lbl(_pnlSharedDriveDest, "User email  (blank = use impersonation email from config):", 0, sy);
            _txtSharedDriveUser = Txt(_pnlSharedDriveDest, 0, sy + 18, 420, "e.g. user@company.com");
            sy += 44;
            Lbl(_pnlSharedDriveDest, "Shared Drive ID:", 0, sy);
            _txtSharedDriveId = Txt(_pnlSharedDriveDest, 0, sy + 18, 420, "e.g. 0ABC1234... (from the drive URL)");
            sy += 44;
            Lbl(_pnlSharedDriveDest, "Folder path  (supports %variables%):", 0, sy);
            _txtSharedDrivePath = Txt(_pnlSharedDriveDest, 0, sy + 18, 520, "e.g. /EmailRelay/%date%");
            sy += 46;
            _pnlSharedDriveDest.Height = sy;
        }
        _scroll.Controls.Add(_pnlSharedDriveDest);

        // Smarthost dest panel
        _pnlSmarthostDest = new Panel { Location = new Point(lx, y), Width = w, Visible = false };
        BuildSmarthostPanel();
        _scroll.Controls.Add(_pnlSmarthostDest);

        y += Math.Max(_pnlRelayDest.Height,
             Math.Max(_pnlMyDriveDest.Height,
             Math.Max(_pnlSharedDriveDest.Height, _pnlSmarthostDest.Height))) + 8;

        _lastOverrideY = y;

        Sep(_scroll, lx, y, w); y += 10;
        BoldLabel(_scroll, "Per-rule overrides  (leave unchecked to use global defaults):", lx, y); y += 26;

        _chkOverrideSaveWhat = Chk(_scroll, "Override save what:", lx, y);
        _cboSaveWhat = new ComboBox { Location = new Point(lx + 195, y - 2), Width = 180, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 9), Enabled = false };
        foreach (var v in Enum.GetNames<SaveWhat>()) _cboSaveWhat.Items.Add(v);
        _cboSaveWhat.SelectedIndex = 0;
        _chkOverrideSaveWhat.CheckedChanged += (_, _) => _cboSaveWhat.Enabled = _chkOverrideSaveWhat.Checked;
        _scroll.Controls.Add(_cboSaveWhat);
        y += 28;

        _chkOverrideSubfolder = Chk(_scroll, "Override per-email subfolder:", lx, y);
        _chkSubfolderValue = new CheckBox { Text = "Enabled", Location = new Point(lx + 235, y), AutoSize = true, Enabled = false };
        _chkOverrideSubfolder.CheckedChanged += (_, _) => _chkSubfolderValue.Enabled = _chkOverrideSubfolder.Checked;
        _scroll.Controls.Add(_chkSubfolderValue);
        y += 28;

        _chkOverrideFromSender = Chk(_scroll, "Override From: sender handling:", lx, y);
        _cboFromSender = new ComboBox { Location = new Point(lx + 243, y - 2), Width = 160, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 9), Enabled = false };
        foreach (var v in Enum.GetNames<FromSenderHandling>()) _cboFromSender.Items.Add(v);
        _cboFromSender.SelectedIndex = 0;
        _chkOverrideFromSender.CheckedChanged += (_, _) => _cboFromSender.Enabled = _chkOverrideFromSender.Checked;
        _scroll.Controls.Add(_cboFromSender);
        y += 28;

        _chkOverrideSaveEmbedded = Chk(_scroll, "Override save embedded images:", lx, y);
        _chkSaveEmbeddedValue = new CheckBox { Text = "Enabled", Location = new Point(lx + 235, y), AutoSize = true, Enabled = false };
        _chkOverrideSaveEmbedded.CheckedChanged += (_, _) => _chkSaveEmbeddedValue.Enabled = _chkOverrideSaveEmbedded.Checked;
        _scroll.Controls.Add(_chkSaveEmbeddedValue);
        y += 28;

        _chkOverrideFilename = Chk(_scroll, "Override filename template:", lx, y); y += 22;
        _txtFilenameTemplate = new TextBox { Location = new Point(lx, y), Width = 570, Font = new Font("Segoe UI", 9), Enabled = false, PlaceholderText = "e.g. %date%_%subject[0]%_%originalbasefilename%" };
        _chkOverrideFilename.CheckedChanged += (_, _) => _txtFilenameTemplate.Enabled = _chkOverrideFilename.Checked;
        _scroll.Controls.Add(_txtFilenameTemplate);
        y += 28;

        _chkOverrideDelimiter = Chk(_scroll, "Override subject delimiter:", lx, y);
        _txtSubjectDelimiter = new TextBox { Location = new Point(lx + 220, y - 2), Width = 80, Font = new Font("Segoe UI", 9), Enabled = false, PlaceholderText = "space" };
        _chkOverrideDelimiter.CheckedChanged += (_, _) => _txtSubjectDelimiter.Enabled = _chkOverrideDelimiter.Checked;
        _scroll.Controls.Add(_txtSubjectDelimiter);
        y += 28;

        _chkEnabled = new CheckBox { Text = "Rule enabled", Location = new Point(lx, y + 4), AutoSize = true, Checked = true };
        _scroll.Controls.Add(_chkEnabled);

        UpdateDestVisibility();
    }

    // ── Match section ─────────────────────────────────────────────────────────

    private void BuildMatchSection(int lx, ref int y, int w)
    {
        var mode = CurrentMode;

        if (mode == MatchMode.DomainSuffix)
        {
            Lbl(_scroll, "Suffix segment  (blank or * = wildcard — captures any subdomain as %suffix%):", lx, y).Tag = "match";
            y += 18;
            _txtSuffix     = Txt(_scroll, lx, y, 200); _txtSuffix.Tag = "match";
            Lbl(_scroll, "Base domain  (optional; blank = any domain):", lx + 220, y - 18).Tag = "match";
            _txtBaseDomain = Txt(_scroll, lx + 220, y, 280, "e.g. company.com"); _txtBaseDomain.Tag = "match";
            y += 28;
            var hint = new Label { Text = "Example: suffix=files, domain=company.com matches jane@files.company.com", Location = new Point(lx, y), Width = w, Height = 22, AutoSize = false, ForeColor = Color.DimGray, Font = new Font("Segoe UI", 8.5f), Tag = "match" };
            _scroll.Controls.Add(hint); y += 24;
        }
        else if (mode == MatchMode.ExactTo)
        {
            Lbl(_scroll, "To: address  (exact match, case-insensitive):", lx, y).Tag = "match"; y += 18;
            _txtPattern = Txt(_scroll, lx, y, 500, "invoices@relay.local"); _txtPattern.Tag = "match"; y += 28;
        }
        else
        {
            var fieldLabel = mode switch
            {
                MatchMode.RegexFrom    => "From: address pattern:",
                MatchMode.RegexSubject => "Subject pattern:",
                _                      => "To: address pattern:"
            };
            Lbl(_scroll, fieldLabel, lx, y).Tag = "match"; y += 18;
            _txtPattern = Txt(_scroll, lx, y, 480, @"e.g. (?<type>INVOICE|PO)-(?<num>\d+)"); _txtPattern.Tag = "match";
            var btnTest = new Button { Text = "Test…", Location = new Point(lx + 490, y - 1), Size = new Size(60, 24), FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = true, Tag = "match" };
            btnTest.Click += (_, _) => RunPatternTest();
            _scroll.Controls.Add(btnTest); y += 28;

            _chkCaseInsensitive = new CheckBox { Text = "Case-insensitive", Location = new Point(lx, y), AutoSize = true, Checked = true, Font = new Font("Segoe UI", 9), Tag = "match" };
            _scroll.Controls.Add(_chkCaseInsensitive); y += 24;

            _txtTestInput = new TextBox { Location = new Point(lx, y), Width = 480, Font = new Font("Segoe UI", 9), PlaceholderText = "Sample input to test — then click Test…", Tag = "match" };
            _scroll.Controls.Add(_txtTestInput); y += 26;

            _lblTestResult = new Label { Location = new Point(lx, y), Width = w, Height = 40, AutoSize = false, Font = new Font("Segoe UI", 9), ForeColor = Color.DimGray, Tag = "match", Text = "Enter a sample above and click Test… to see match result." };
            _scroll.Controls.Add(_lblTestResult); y += 44;

            var hintRx = new Label { Text = "Named groups (?<name>...) become %name% in path templates. Numbered groups become %match1%, %match2%, etc.", Location = new Point(lx, y), Width = w, Height = 28, AutoSize = false, ForeColor = Color.DimGray, Font = new Font("Segoe UI", 8.5f), Tag = "match" };
            _scroll.Controls.Add(hintRx); y += 30;
        }
    }

    private void RebuildMatchSection()
    {
        var toRemove = _scroll.Controls.Cast<Control>().Where(c => "match".Equals(c.Tag?.ToString())).ToList();
        int oldBottom = toRemove.Count > 0 ? toRemove.Max(c => c.Bottom) + 8 : 0;
        foreach (var c in toRemove) _scroll.Controls.Remove(c);
        _txtSuffix = null; _txtBaseDomain = null; _txtPattern = null;
        _chkCaseInsensitive = null; _txtTestInput = null; _lblTestResult = null;
        int startY = 8;
        BuildMatchSection(10, ref startY, 620);
        int newBottom = startY + 8;
        int delta = newBottom - oldBottom;
        if (delta != 0)
        {
            foreach (Control c in _scroll.Controls)
            {
                if ("match".Equals(c.Tag?.ToString())) continue;
                if (c.Location.Y >= oldBottom) c.Location = new Point(c.Location.X, c.Location.Y + delta);
            }
            _destPanelY   += delta;
            _lastOverrideY += delta;
            foreach (var pnl in new[] { _pnlRelayDest, _pnlMyDriveDest, _pnlSharedDriveDest, _pnlSmarthostDest })
                if (pnl != null) pnl.Location = new Point(pnl.Location.X, pnl.Location.Y + delta);
        }
        RebuildStripSuffixInRelayPanel();
        RebuildStripSuffixInSmarthostPanel();
    }

    // ── Relay panel ───────────────────────────────────────────────────────────

    private void BuildRelayPanel()
    {
        int ry = 0;
        Lbl(_pnlRelayDest, "Send via mailbox  (empty = passthrough):", 0, ry); ry += 18;
        _txtRelayVia = Txt(_pnlRelayDest, 0, ry, 400); ry += 30;
        Sep(_pnlRelayDest, 0, ry, 590); ry += 12;

        _chkStripSuffixRelay = null;
        if (CurrentMode == MatchMode.DomainSuffix)
        {
            _chkStripSuffixRelay = Chk(_pnlRelayDest, "Strip suffix segment from recipient address before delivery", 0, ry);
            _chkStripSuffixRelay.Tag = "striprow_relay"; ry += 24;
        }
        Lbl(_pnlRelayDest, "Override recipient address  (optional):", 0, ry); ry += 18;
        _txtDeliverToRelay = Txt(_pnlRelayDest, 0, ry, 420, "e.g. support@company.com — leave blank to use original"); ry += 28;
        _pnlRelayDest.Height = ry;
    }

    private void RebuildStripSuffixInRelayPanel()
    {
        if (_pnlRelayDest == null) return;
        var existing = _pnlRelayDest.Controls.Cast<Control>().FirstOrDefault(c => "striprow_relay".Equals(c.Tag?.ToString()));
        bool want = CurrentMode == MatchMode.DomainSuffix;
        if (existing != null && !want)
        {
            int dy = existing.Height + 4;
            foreach (Control c in _pnlRelayDest.Controls) if (c.Location.Y > existing.Location.Y) c.Location = new Point(c.Location.X, c.Location.Y - dy);
            _pnlRelayDest.Controls.Remove(existing); _chkStripSuffixRelay = null; _pnlRelayDest.Height -= dy;
        }
        else if (existing == null && want && _txtDeliverToRelay != null)
        {
            int insertY = _txtDeliverToRelay.Location.Y - 18 - 24;
            _chkStripSuffixRelay = Chk(_pnlRelayDest, "Strip suffix segment from recipient address before delivery", 0, insertY);
            _chkStripSuffixRelay.Tag = "striprow_relay";
            int dy = _chkStripSuffixRelay.Height + 4;
            foreach (Control c in _pnlRelayDest.Controls) if (c != _chkStripSuffixRelay && c.Location.Y >= insertY) c.Location = new Point(c.Location.X, c.Location.Y + dy);
            _pnlRelayDest.Height += dy;
        }
    }

    // ── Smarthost panel ───────────────────────────────────────────────────────

    private void BuildSmarthostPanel()
    {
        int y = 0;
        _chkSmarthostUseGlobal = new CheckBox { Text = "Use global smarthost settings (configured in Settings → Smarthost Failover)", Location = new Point(0, y), AutoSize = true, Font = new Font("Segoe UI", 9), Checked = true };
        _chkSmarthostUseGlobal.CheckedChanged += (_, _) => UpdateSmarthostFields();
        _pnlSmarthostDest.Controls.Add(_chkSmarthostUseGlobal); y += 26;

        _pnlSmarthostDest.Controls.Add(new Label { Text = "Mail matching this rule is always delivered via smarthost — not as a failover.", Location = new Point(0, y), AutoSize = false, Width = 590, Height = 22, ForeColor = Color.DimGray, Font = new Font("Segoe UI", 8.5f) }); y += 28;

        Lbl(_pnlSmarthostDest, "Host:", 0, y); y += 18;
        _txtSmarthostHost = Txt(_pnlSmarthostDest, 0, y, 380, "e.g. relay.company.com"); y += 28;
        Lbl(_pnlSmarthostDest, "Port:", 0, y); Lbl(_pnlSmarthostDest, "TLS:", 110, y); y += 18;
        _nudSmarthostPort = new NumericUpDown { Location = new Point(0, y), Width = 90, Minimum = 1, Maximum = 65535, Value = 587, Font = new Font("Segoe UI", 9) };
        _pnlSmarthostDest.Controls.Add(_nudSmarthostPort);
        _cboSmarthostTls = new ComboBox { Location = new Point(110, y), Width = 160, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 9) };
        _cboSmarthostTls.Items.AddRange(new object[] { "None", "STARTTLS (Recommended)", "SSL/TLS" }); _cboSmarthostTls.SelectedIndex = 1;
        _pnlSmarthostDest.Controls.Add(_cboSmarthostTls); y += 32;
        Lbl(_pnlSmarthostDest, "Username:", 0, y); y += 18;
        _txtSmarthostUser = Txt(_pnlSmarthostDest, 0, y, 340); y += 28;
        Lbl(_pnlSmarthostDest, "Password:", 0, y); y += 18;
        _txtSmarthostPass = new TextBox { Location = new Point(0, y), Width = 340, UseSystemPasswordChar = true, Font = new Font("Segoe UI", 9) };
        _pnlSmarthostDest.Controls.Add(_txtSmarthostPass); y += 28;

        Sep(_pnlSmarthostDest, 0, y, 590); y += 12;
        _chkStripSuffixSmarthost = null;
        if (CurrentMode == MatchMode.DomainSuffix)
        {
            _chkStripSuffixSmarthost = Chk(_pnlSmarthostDest, "Strip suffix segment from recipient address before delivery", 0, y);
            _chkStripSuffixSmarthost.Tag = "striprow_smarthost"; y += 24;
        }
        Lbl(_pnlSmarthostDest, "Override recipient address  (optional):", 0, y); y += 18;
        _txtDeliverToSmarthost = Txt(_pnlSmarthostDest, 0, y, 420, "e.g. support@company.com"); y += 30;
        _chkRewriteToHeader = new CheckBox { Text = "Also rewrite embedded To: header in message", Location = new Point(0, y), AutoSize = true, Font = new Font("Segoe UI", 9) };
        _pnlSmarthostDest.Controls.Add(_chkRewriteToHeader); y += 24;
        _pnlSmarthostDest.Controls.Add(new Label { Text = "Warning: rewriting the To: header may invalidate DKIM signatures.", Location = new Point(18, y), Width = 570, Height = 22, AutoSize = false, ForeColor = Color.DarkGoldenrod, Font = new Font("Segoe UI", 8.5f) }); y += 28;
        _pnlSmarthostDest.Height = y;
        UpdateSmarthostFields();
    }

    private void RebuildStripSuffixInSmarthostPanel()
    {
        if (_pnlSmarthostDest == null) return;
        var existing = _pnlSmarthostDest.Controls.Cast<Control>().FirstOrDefault(c => "striprow_smarthost".Equals(c.Tag?.ToString()));
        bool want = CurrentMode == MatchMode.DomainSuffix;
        if (existing != null && !want)
        {
            int dy = existing.Height + 4;
            foreach (Control c in _pnlSmarthostDest.Controls) if (c.Location.Y > existing.Location.Y) c.Location = new Point(c.Location.X, c.Location.Y - dy);
            _pnlSmarthostDest.Controls.Remove(existing); _chkStripSuffixSmarthost = null; _pnlSmarthostDest.Height -= dy;
        }
        else if (existing == null && want && _txtDeliverToSmarthost != null)
        {
            int insertY = _txtDeliverToSmarthost.Location.Y - 18 - 24;
            _chkStripSuffixSmarthost = Chk(_pnlSmarthostDest, "Strip suffix segment from recipient address before delivery", 0, insertY);
            _chkStripSuffixSmarthost.Tag = "striprow_smarthost";
            int dy = _chkStripSuffixSmarthost.Height + 4;
            foreach (Control c in _pnlSmarthostDest.Controls) if (c != _chkStripSuffixSmarthost && c.Location.Y >= insertY) c.Location = new Point(c.Location.X, c.Location.Y + dy);
            _pnlSmarthostDest.Height += dy;
        }
    }

    private void UpdateSmarthostFields()
    {
        bool useGlobal = _chkSmarthostUseGlobal?.Checked ?? true;
        if (_txtSmarthostHost != null) _txtSmarthostHost.Enabled = !useGlobal;
        if (_nudSmarthostPort != null) _nudSmarthostPort.Enabled = !useGlobal;
        if (_cboSmarthostTls  != null) _cboSmarthostTls.Enabled  = !useGlobal;
        if (_txtSmarthostUser != null) _txtSmarthostUser.Enabled = !useGlobal;
        if (_txtSmarthostPass != null) _txtSmarthostPass.Enabled = !useGlobal;
    }

    // ── Regex test ────────────────────────────────────────────────────────────

    private void RunPatternTest()
    {
        if (_txtPattern == null || _txtTestInput == null || _lblTestResult == null) return;
        var pattern = _txtPattern.Text.Trim();
        var input   = _txtTestInput.Text;
        if (string.IsNullOrEmpty(pattern)) { _lblTestResult.Text = "No pattern entered."; _lblTestResult.ForeColor = Color.DimGray; return; }
        try
        {
            var opts = RegexOptions.None;
            if (_chkCaseInsensitive?.Checked ?? true) opts |= RegexOptions.IgnoreCase;
            var m = Regex.Match(input, pattern, opts, TimeSpan.FromMilliseconds(500));
            if (!m.Success) { _lblTestResult.Text = "No match."; _lblTestResult.ForeColor = Color.Firebrick; return; }
            var sb = new System.Text.StringBuilder("Match");
            foreach (Group g in m.Groups) { if (int.TryParse(g.Name, out _)) continue; if (g.Success) sb.Append($"  %{g.Name}% = \"{g.Value}\""); }
            for (int i = 1; i < m.Groups.Count; i++) if (m.Groups[i].Success) sb.Append($"  %match{i}% = \"{m.Groups[i].Value}\"");
            _lblTestResult.Text = sb.ToString(); _lblTestResult.ForeColor = Color.DarkGreen;
        }
        catch (ArgumentException ex) { _lblTestResult.Text = $"Regex error: {ex.Message}"; _lblTestResult.ForeColor = Color.Firebrick; }
    }

    // ── Populate from existing rule ───────────────────────────────────────────

    private void PopulateFromEdit()
    {
        var r = _editRule;
        if (r == null) return;

        _cboMatchMode.SelectedIndex = r.Mode switch { MatchMode.ExactTo => 1, MatchMode.RegexTo => 2, MatchMode.RegexFrom => 3, MatchMode.RegexSubject => 4, _ => 0 };

        if (_txtSuffix != null) _txtSuffix.Text = r.Suffix;
        if (_txtBaseDomain != null) _txtBaseDomain.Text = r.BaseDomain;
        if (_txtPattern != null) _txtPattern.Text = r.Pattern;
        if (_chkCaseInsensitive != null) _chkCaseInsensitive.Checked = r.CaseInsensitive;

        bool isGDrive  = r.DestinationType == FileDestinationType.GoogleDrive;
        bool isShared  = isGDrive && !string.IsNullOrWhiteSpace(r.LibraryDriveId);

        _rdoTypeRelay.Checked       = r.DestinationType == FileDestinationType.EmailRelay;
        _rdoTypeMyDrive.Checked     = isGDrive && !isShared;
        _rdoTypeSharedDrive.Checked = isShared;
        _rdoTypeSmarthost.Checked   = r.DestinationType == FileDestinationType.SmarthostRelay;

        if (_txtRelayVia != null)       _txtRelayVia.Text       = r.RelayVia;
        if (_txtMyDriveUser != null)    _txtMyDriveUser.Text    = r.OneDriveUser;
        if (_txtMyDrivePath != null)    _txtMyDrivePath.Text    = !isShared ? r.FolderPath : "";
        if (_txtSharedDriveUser != null) _txtSharedDriveUser.Text = r.OneDriveUser;
        if (_txtSharedDriveId != null)  _txtSharedDriveId.Text  = r.LibraryDriveId;
        if (_txtSharedDrivePath != null) _txtSharedDrivePath.Text = isShared ? r.FolderPath : "";

        if (_chkSmarthostUseGlobal != null)  _chkSmarthostUseGlobal.Checked  = r.UseGlobalSmarthost;
        if (_txtSmarthostHost != null)        _txtSmarthostHost.Text          = r.SmarthostOverrideHost;
        if (_nudSmarthostPort != null)        _nudSmarthostPort.Value         = Math.Clamp(r.SmarthostOverridePort, 1, 65535);
        if (_cboSmarthostTls != null)         _cboSmarthostTls.SelectedIndex  = (int)r.SmarthostOverrideTls;
        if (_txtSmarthostUser != null)        _txtSmarthostUser.Text          = r.SmarthostOverrideUsername;
        if (_txtSmarthostPass != null)        _txtSmarthostPass.Text          = r.SmarthostOverridePassword;
        if (_chkStripSuffixRelay != null)     _chkStripSuffixRelay.Checked    = r.StripSuffixFromTo;
        if (_txtDeliverToRelay != null)       _txtDeliverToRelay.Text         = r.DeliverToOverride;
        if (_chkStripSuffixSmarthost != null) _chkStripSuffixSmarthost.Checked = r.StripSuffixFromTo;
        if (_txtDeliverToSmarthost != null)   _txtDeliverToSmarthost.Text     = r.DeliverToOverride;
        if (_chkRewriteToHeader != null)      _chkRewriteToHeader.Checked     = r.RewriteToHeader;

        if (r.UsePerEmailSubfolder.HasValue)  { _chkOverrideSubfolder.Checked = true; _chkSubfolderValue.Checked = r.UsePerEmailSubfolder.Value; }
        if (r.SaveWhat.HasValue)              { _chkOverrideSaveWhat.Checked  = true; _cboSaveWhat.SelectedItem = r.SaveWhat.Value.ToString(); }
        if (r.FromSenderHandling != FromSenderHandling.Ignore) { _chkOverrideFromSender.Checked = true; _cboFromSender.SelectedItem = r.FromSenderHandling.ToString(); }
        if (r.SaveEmbeddedImages.HasValue)    { _chkOverrideSaveEmbedded.Checked = true; _chkSaveEmbeddedValue.Checked = r.SaveEmbeddedImages.Value; }
        if (!string.IsNullOrWhiteSpace(r.FilenameTemplate)) { _chkOverrideFilename.Checked = true; _txtFilenameTemplate.Text = r.FilenameTemplate; }
        if (r.SubjectDelimiter is not null)   { _chkOverrideDelimiter.Checked = true; _txtSubjectDelimiter.Text = r.SubjectDelimiter == " " ? "" : r.SubjectDelimiter; }

        _chkEnabled.Checked = r.Enabled;
        UpdateDestVisibility();
    }

    // ── Destination visibility ────────────────────────────────────────────────

    private void UpdateDestVisibility()
    {
        bool isRelay    = _rdoTypeRelay?.Checked ?? true;
        bool isMyDrive  = _rdoTypeMyDrive?.Checked ?? false;
        bool isShared   = _rdoTypeSharedDrive?.Checked ?? false;
        bool isSh       = _rdoTypeSmarthost?.Checked ?? false;

        if (_pnlRelayDest       != null) _pnlRelayDest.Visible       = isRelay;
        if (_pnlMyDriveDest     != null) _pnlMyDriveDest.Visible     = isMyDrive;
        if (_pnlSharedDriveDest != null) _pnlSharedDriveDest.Visible = isShared;
        if (_pnlSmarthostDest   != null) _pnlSmarthostDest.Visible   = isSh;

        int activeHeight = isRelay   ? (_pnlRelayDest?.Height       ?? 50)
                         : isMyDrive ? (_pnlMyDriveDest?.Height     ?? 90)
                         : isShared  ? (_pnlSharedDriveDest?.Height ?? 140)
                                     : (_pnlSmarthostDest?.Height   ?? 200);

        RepositionOverrides(_destPanelY + activeHeight + 8);

        bool isDrive = isMyDrive || isShared;
        void SetPair(CheckBox? chk, Control? sub) { if (chk == null) return; chk.Enabled = isDrive; if (sub != null) sub.Enabled = isDrive && chk.Checked; }
        SetPair(_chkOverrideSaveWhat,    _cboSaveWhat);
        SetPair(_chkOverrideSubfolder,   _chkSubfolderValue);
        SetPair(_chkOverrideSaveEmbedded, _chkSaveEmbeddedValue);
        SetPair(_chkOverrideFilename,    _txtFilenameTemplate);
        SetPair(_chkOverrideDelimiter,   _txtSubjectDelimiter);
    }

    private void RepositionOverrides(int startY)
    {
        int delta = startY - _lastOverrideY;
        if (delta == 0) return;
        int prevY = _lastOverrideY;
        _lastOverrideY = startY;
        foreach (Control c in _scroll.Controls)
        {
            if (c == _pnlRelayDest || c == _pnlMyDriveDest || c == _pnlSharedDriveDest || c == _pnlSmarthostDest) continue;
            if ("match".Equals(c.Tag?.ToString())) continue;
            if (c.Location.Y >= prevY) c.Location = new Point(c.Location.X, c.Location.Y + delta);
        }
    }

    // ── Service flag enforcement ──────────────────────────────────────────────

    private void ApplyServiceFlags()
    {
        var cfg = _configManager.Config;
        bool drive = cfg.EnableGoogleDrive;
        _rdoTypeMyDrive.Enabled     = drive;
        _rdoTypeSharedDrive.Enabled = drive;
        // Email Relay and Smarthost are always available

        // If selected radio is now disabled, fall to first enabled option
        var radios = new[] { _rdoTypeRelay, _rdoTypeMyDrive, _rdoTypeSharedDrive, _rdoTypeSmarthost };
        if (radios.FirstOrDefault(r => r.Checked)?.Enabled == false)
        {
            var first = radios.FirstOrDefault(r => r.Enabled);
            if (first != null) first.Checked = true;
        }
    }

    // ── Save ──────────────────────────────────────────────────────────────────

    private void SaveRule()
    {
        var mode = CurrentMode;

        // Validate match
        if (mode == MatchMode.DomainSuffix)
        {
            var suf = _txtSuffix?.Text.Trim() ?? "";
            var dom = _txtBaseDomain?.Text.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(suf) && string.IsNullOrWhiteSpace(dom))
            {
                MessageBox.Show("Wildcard suffix rules require a Base Domain to avoid matching all traffic.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }
        else
        {
            var pat = _txtPattern?.Text.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(pat)) { MessageBox.Show(mode == MatchMode.ExactTo ? "To: address is required." : "Pattern is required.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (mode != MatchMode.ExactTo) { try { _ = new Regex(pat, RegexOptions.None, TimeSpan.FromMilliseconds(250)); } catch (ArgumentException ex) { MessageBox.Show($"Invalid regex pattern:\n{ex.Message}", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; } }
        }

        bool isShared = _rdoTypeSharedDrive?.Checked ?? false;
        if (isShared && string.IsNullOrWhiteSpace(_txtSharedDriveId?.Text))
        {
            MessageBox.Show("Enter the Shared Drive ID.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var destType = _rdoTypeMyDrive?.Checked == true || _rdoTypeSharedDrive?.Checked == true
            ? FileDestinationType.GoogleDrive
            : _rdoTypeSmarthost?.Checked == true
                ? FileDestinationType.SmarthostRelay
                : FileDestinationType.EmailRelay;

        string folderPath = isShared
            ? (_txtSharedDrivePath?.Text.Trim() ?? "")
            : destType == FileDestinationType.GoogleDrive
                ? (_txtMyDrivePath?.Text.Trim() ?? "")
                : "";

        bool stripSuffix = destType == FileDestinationType.EmailRelay
            ? (_chkStripSuffixRelay?.Checked ?? false)
            : destType == FileDestinationType.SmarthostRelay
                ? (_chkStripSuffixSmarthost?.Checked ?? false) : false;

        string deliverTo = destType == FileDestinationType.EmailRelay
            ? (_txtDeliverToRelay?.Text.Trim() ?? "")
            : destType == FileDestinationType.SmarthostRelay
                ? (_txtDeliverToSmarthost?.Text.Trim() ?? "") : "";

        Result = new RoutingRule
        {
            Id      = _editRule?.Id ?? Guid.NewGuid().ToString("N")[..8],
            Enabled = _chkEnabled.Checked,
            Mode    = mode,

            Suffix          = _txtSuffix?.Text.Trim() ?? "",
            BaseDomain      = _txtBaseDomain?.Text.Trim() ?? "",
            Pattern         = _txtPattern?.Text.Trim() ?? "",
            CaseInsensitive = _chkCaseInsensitive?.Checked ?? true,

            DestinationType = destType,
            RelayVia        = _txtRelayVia?.Text.Trim() ?? "",
            OneDriveUser    = isShared
                ? (_txtSharedDriveUser?.Text.Trim() ?? "")
                : (_txtMyDriveUser?.Text.Trim() ?? ""),
            LibraryDriveId  = isShared ? (_txtSharedDriveId?.Text.Trim() ?? "") : "",
            FolderPath      = folderPath,

            UsePerEmailSubfolder     = _chkOverrideSubfolder.Checked ? _chkSubfolderValue.Checked : null,
            SaveWhat                 = _chkOverrideSaveWhat.Checked ? Enum.Parse<SaveWhat>(_cboSaveWhat.SelectedItem?.ToString() ?? "AttachmentsOnly") : null,
            FromSenderHandling       = _chkOverrideFromSender.Checked ? Enum.Parse<FromSenderHandling>(_cboFromSender.SelectedItem?.ToString() ?? "Ignore") : FromSenderHandling.Ignore,
            SaveEmbeddedImages       = _chkOverrideSaveEmbedded.Checked ? _chkSaveEmbeddedValue.Checked : null,
            FilenameTemplate         = _chkOverrideFilename.Checked ? _txtFilenameTemplate.Text.Trim() : null,
            SubjectDelimiter         = _chkOverrideDelimiter.Checked ? (string.IsNullOrEmpty(_txtSubjectDelimiter.Text) ? " " : _txtSubjectDelimiter.Text) : null,

            UseGlobalSmarthost        = _chkSmarthostUseGlobal?.Checked ?? true,
            SmarthostOverrideHost     = _txtSmarthostHost?.Text.Trim() ?? "",
            SmarthostOverridePort     = (int)(_nudSmarthostPort?.Value ?? 587),
            SmarthostOverrideTls      = (SmarthostTls)(_cboSmarthostTls?.SelectedIndex ?? 1),
            SmarthostOverrideUsername = _txtSmarthostUser?.Text.Trim() ?? "",
            SmarthostOverridePassword = _txtSmarthostPass?.Text ?? "",

            StripSuffixFromTo = stripSuffix,
            DeliverToOverride = deliverTo,
            RewriteToHeader   = destType == FileDestinationType.SmarthostRelay && (_chkRewriteToHeader?.Checked ?? false),
        };

        DialogResult = DialogResult.OK;
        Close();
    }

    // ── Layout helpers ────────────────────────────────────────────────────────

    private Label Lbl(Control parent, string text, int x, int y, int width = 0)
    {
        var lbl = new Label { Text = text, Location = new Point(x, y), AutoSize = width == 0, Font = new Font("Segoe UI", 9) };
        if (width > 0) { lbl.Width = width; lbl.AutoSize = false; }
        parent.Controls.Add(lbl);
        return lbl;
    }

    private static void BoldLabel(Control parent, string text, int x, int y) =>
        parent.Controls.Add(new Label { Text = text, Location = new Point(x, y), AutoSize = true, Font = new Font("Segoe UI", 9, FontStyle.Bold) });

    private TextBox Txt(Control parent, int x, int y, int width, string placeholder = "")
    {
        var tb = new TextBox { Location = new Point(x, y), Width = width, Font = new Font("Segoe UI", 9), PlaceholderText = placeholder };
        parent.Controls.Add(tb);
        return tb;
    }

    private static RadioButton Rdo(Control parent, string text, int x, int y)
    {
        var r = new RadioButton { Text = text, Location = new Point(x, y), AutoSize = true };
        parent.Controls.Add(r);
        return r;
    }

    private static CheckBox Chk(Control parent, string text, int x, int y)
    {
        var c = new CheckBox { Text = text, Location = new Point(x, y), AutoSize = true, Font = new Font("Segoe UI", 9) };
        parent.Controls.Add(c);
        return c;
    }

    private static void Sep(Control parent, int x, int y, int width) =>
        parent.Controls.Add(new Panel { Location = new Point(x, y), Size = new Size(width, 1), BackColor = Color.FromArgb(210, 210, 220) });
}

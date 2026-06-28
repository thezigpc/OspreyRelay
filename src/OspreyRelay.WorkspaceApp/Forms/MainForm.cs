using System.Diagnostics;
using System.ServiceProcess;
using OspreyRelay.Core.Config;
using OspreyRelay.Core.Logging;
using OspreyRelay.Core.Smtp;
using OspreyRelay.WorkspaceApp.Relay;
using OspreyRelay.WorkspaceApp.Services;

namespace OspreyRelay.WorkspaceApp.Forms;

public class MainForm : Form
{
    // ── Core ────────────────────────────────────────────────────────────────
    private readonly ConfigManager _configManager = new("OspreyRelayWorkspace");
    private RelayLogger _logger = null!;
    private IRelayConnection _connection = null!;

    // ── Controls ────────────────────────────────────────────────────────────
    private Panel _pnlStatusBar = null!;
    private Label _lblRunState = null!;
    private Label _lblDetails = null!;
    private Button _btnStartStop = null!;
    private Button _btnConfigure = null!;
    private Button _btnSettings = null!;
    private Button _btnRouting = null!;
    private Button _btnFileRules = null!;
    private Button _btnUnrouted = null!;
    private Button _btnFtpBridge = null!;
    private Button _btnServiceInstall = null!;
    private Button _btnServiceStart = null!;
    private Button _btnOpenLog = null!;
    private Button _btnTestSend = null!;
    private Button _btnDebugMode = null!;
    private RichTextBox _rtbLog = null!;
    private NotifyIcon _trayIcon = null!;
    private System.Windows.Forms.Timer _statusTimer = null!;

    public MainForm()
    {
        _configManager.Load();

        bool svcInstalled = WindowsServiceManager.IsInstalled();

        _logger = new RelayLogger(svcInstalled ? null : _configManager.GetLogPath())
        {
            DebugMode = _configManager.Config.DebugMode
        };
        _logger.LogReceived += OnLogReceived;

        _connection = svcInstalled
            ? (IRelayConnection)new ServiceRelayConnection(_configManager.GetLogPath())
            : new InProcessRelayConnection(_configManager, _logger);
        _connection.LogReceived += OnLogReceived;
        _connection.Open();

        InitializeComponent();
        RefreshServiceButtons();
        UpdateStatusBar();

        if (!svcInstalled && _configManager.Config.IsWorkspaceConfigured)
            StartRelay();
    }

    // ── UI construction ──────────────────────────────────────────────────────
    private void InitializeComponent()
    {
        Text = "Osprey Relay for Workspace";
        Size = new Size(780, 560);
        MinimumSize = new Size(760, 440);
        StartPosition = FormStartPosition.CenterScreen;
        var exeIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        if (exeIcon != null) Icon = exeIcon;

        _pnlStatusBar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 64,
            BackColor = Color.FromArgb(20, 50, 30),
            Padding = new Padding(12, 0, 12, 0)
        };
        _lblRunState = new Label
        {
            Font = new Font("Segoe UI", 16, FontStyle.Bold),
            ForeColor = Color.FromArgb(210, 80, 80),
            Text = "STOPPED",
            AutoSize = true,
            Location = new Point(12, 10)
        };
        _lblDetails = new Label
        {
            Font = new Font("Segoe UI", 9),
            ForeColor = Color.FromArgb(180, 200, 180),
            Text = "Not configured — click Configure to upload your service account key",
            AutoSize = true,
            Location = new Point(14, 40)
        };
        _pnlStatusBar.Controls.AddRange(new Control[] { _lblRunState, _lblDetails });

        var pnlTools = new Panel
        {
            Dock = DockStyle.Top,
            Height = 82,
            BackColor = Color.FromArgb(245, 245, 248)
        };
        var row1 = new FlowLayoutPanel
        {
            Location = new Point(4, 4),
            Height = 36, Width = 760,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            WrapContents = false, AutoSize = false, Padding = new Padding(0)
        };
        var row2 = new FlowLayoutPanel
        {
            Location = new Point(4, 42),
            Height = 36, Width = 760,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            WrapContents = false, AutoSize = false, Padding = new Padding(0)
        };

        _btnStartStop      = ToolBtn("Start Relay");
        _btnConfigure      = ToolBtn("Configure…");
        _btnSettings       = ToolBtn("Settings…");
        _btnRouting        = ToolBtn("Email Routes…");
        _btnFileRules      = ToolBtn("Rules…");
        _btnUnrouted       = ToolBtn("Unrouted…");
        _btnFtpBridge      = ToolBtn("FTP Bridge…");
        _btnServiceInstall = ToolBtn("Install Service");
        _btnServiceStart   = ToolBtn("Start Service");
        _btnOpenLog        = ToolBtn("Open Log Folder");
        _btnTestSend       = ToolBtn("Test Send…");
        _btnDebugMode      = ToolBtn("Debug: OFF");

        _btnStartStop.Click      += (_, _) => ToggleRelay();
        _btnConfigure.Click      += (_, _) => OpenWizard();
        _btnSettings.Click       += (_, _) => OpenSettings();
        _btnRouting.Click        += (_, _) => OpenRoutingRules();
        _btnFileRules.Click      += (_, _) => OpenFileRules();
        _btnUnrouted.Click       += (_, _) => OpenUnroutedFolder();
        _btnFtpBridge.Click      += (_, _) => OpenFtpBridge();
        _btnServiceInstall.Click += (_, _) => ToggleServiceInstall();
        _btnServiceStart.Click   += (_, _) => ToggleServiceRunning();
        _btnOpenLog.Click        += (_, _) =>
            Process.Start("explorer.exe", _configManager.GetConfigDir());
        _btnTestSend.Click       += (_, _) => OpenTestSend();
        _btnDebugMode.Click      += (_, _) => ToggleDebugMode();

        UpdateDebugButton();

        row1.Controls.AddRange(new Control[]
        {
            _btnStartStop, _btnConfigure, _btnSettings, _btnRouting, _btnFileRules, _btnUnrouted
        });
        row2.Controls.AddRange(new Control[]
        {
            _btnFtpBridge, _btnServiceInstall, _btnServiceStart, _btnOpenLog, _btnTestSend, _btnDebugMode
        });
        pnlTools.Controls.Add(row1);
        pnlTools.Controls.Add(row2);

        _rtbLog = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            Font = new Font("Consolas", 9),
            BackColor = Color.FromArgb(24, 24, 28),
            ForeColor = Color.FromArgb(200, 200, 200),
            BorderStyle = BorderStyle.None,
            ScrollBars = RichTextBoxScrollBars.Vertical
        };
        var lblLogTitle = new Label
        {
            Text = "Activity Log",
            Dock = DockStyle.Top,
            Height = 22,
            Font = new Font("Segoe UI", 8, FontStyle.Bold),
            ForeColor = Color.Gray,
            Padding = new Padding(4, 4, 0, 0)
        };
        var pnlLog = new Panel { Dock = DockStyle.Fill };
        pnlLog.Controls.Add(_rtbLog);
        pnlLog.Controls.Add(lblLogTitle);

        var pnlConfigPath = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 24,
            BackColor = Color.FromArgb(238, 238, 242)
        };
        var configDir = _configManager.GetConfigDir();
        var lblConfigPath = new Label
        {
            AutoSize = true,
            Location = new Point(8, 4),
            Font = new Font("Segoe UI", 8),
            ForeColor = Color.DimGray,
            Text = $"Config: {configDir}"
        };
        var lnkOpenConfig = new LinkLabel
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 8),
            Text = "Open",
            Location = new Point(lblConfigPath.PreferredWidth + 14, 4)
        };
        lnkOpenConfig.LinkClicked += (_, _) => Process.Start("explorer.exe", configDir);
        var lblBackupTip = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 8),
            ForeColor = Color.Gray,
            Text = "— back up this folder to preserve your configuration",
            Location = new Point(lblConfigPath.PreferredWidth + 48, 4)
        };
        pnlConfigPath.Controls.AddRange(
            new Control[] { lblConfigPath, lnkOpenConfig, lblBackupTip });

        Controls.Add(pnlLog);
        Controls.Add(pnlTools);
        Controls.Add(_pnlStatusBar);
        Controls.Add(pnlConfigPath);

        _trayIcon = new NotifyIcon
        {
            Icon = exeIcon != null ? new Icon(exeIcon, 16, 16) : SystemIcons.Application,
            Text = "Osprey Relay for Workspace",
            Visible = true
        };
        var trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("Show", null,
            (_, _) => { Show(); WindowState = FormWindowState.Normal; Activate(); });
        trayMenu.Items.Add("Exit", null,
            (_, _) => { _trayIcon.Visible = false; Application.Exit(); });
        _trayIcon.ContextMenuStrip = trayMenu;
        _trayIcon.DoubleClick += (_, _) => { Show(); WindowState = FormWindowState.Normal; Activate(); };

        _statusTimer = new System.Windows.Forms.Timer { Interval = 5000 };
        _statusTimer.Tick += (_, _) => { RefreshServiceButtons(); UpdateStatusBar(); };
        _statusTimer.Start();

        FormClosing += (_, e) =>
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                _trayIcon.ShowBalloonTip(2000, "Osprey Relay for Workspace",
                    "Running in system tray", ToolTipIcon.Info);
            }
        };
    }

    private static Button ToolBtn(string text) => new Button
    {
        Text = text,
        Size = new Size(118, 30),
        Margin = new Padding(4, 0, 0, 0),
        FlatStyle = FlatStyle.Flat,
        FlatAppearance = { BorderColor = Color.FromArgb(200, 200, 210) },
        Font = new Font("Segoe UI", 8.5f),
        UseVisualStyleBackColor = true
    };

    // ── Relay control ─────────────────────────────────────────────────────────

    private void StartRelay()
    {
        if (_connection.IsRunning) return;

        if (_connection is InProcessRelayConnection && !_configManager.Config.IsWorkspaceConfigured)
        {
            _logger.Warning("Relay not started — workspace credentials not configured. Use Configure…");
            return;
        }

        try
        {
            _connection.StartAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.Error($"Could not start relay: {ex.Message}");
            MessageBox.Show(ex.Message, "Start Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            UpdateStatusBar();
        }
    }

    private void StopRelay()
    {
        try { _connection.StopAsync().GetAwaiter().GetResult(); } catch { }
        UpdateStatusBar();
    }

    private void ToggleRelay()
    {
        if (_connection.IsRunning) StopRelay();
        else StartRelay();
    }

    private void OpenWizard()
    {
        StopRelay();
        using var wizard = new WorkspaceSetupForm(_configManager, _logger);
        if (wizard.ShowDialog(this) == DialogResult.OK)
        {
            _configManager.Load();
            UpdateStatusBar();
            if (_configManager.Config.IsWorkspaceConfigured)
                StartRelay();
        }
    }

    private void OpenSettings()
    {
        using var form = new RelaySettingsForm(_configManager);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            _configManager.Load();
            UpdateStatusBar();
        }
    }

    private void OpenRoutingRules()
    {
        using var form = new SenderRoutesForm(_configManager);
        form.ShowDialog(this);
    }

    private void OpenFileRules()
    {
        using var form = new FileRoutingRulesForm(_configManager, _logger);
        if (form.ShowDialog(this) == DialogResult.OK && _connection.IsRunning)
        {
            StopRelay();
            _configManager.Load();
            StartRelay();
        }
    }

    private void OpenUnroutedFolder()
    {
        var dir = _configManager.GetUnroutedDir(_configManager.Config.UnroutedLocalPath);
        Directory.CreateDirectory(dir);
        Process.Start("explorer.exe", dir);
    }

    private void OpenFtpBridge()
    {
        using var form = new FtpBridgeForm(_configManager, _logger);
        if (form.ShowDialog(this) == DialogResult.OK && _connection.IsRunning)
        {
            StopRelay();
            _configManager.Load();
            StartRelay();
        }
    }

    private void OpenTestSend()
    {
        using var form = new TestSendForm(_configManager, _logger);
        form.ShowDialog(this);
    }

    private void ToggleDebugMode()
    {
        var cfg = _configManager.Config;
        cfg.DebugMode = !cfg.DebugMode;
        _logger.DebugMode = cfg.DebugMode;
        _configManager.Save(cfg);
        UpdateDebugButton();
        _logger.Info($"Debug mode {(cfg.DebugMode ? "enabled" : "disabled")}");
    }

    private void UpdateDebugButton()
    {
        var on = _configManager.Config.DebugMode;
        _btnDebugMode.Text = on ? "Debug: ON" : "Debug: OFF";
        _btnDebugMode.ForeColor = on ? Color.FromArgb(80, 200, 80) : SystemColors.ControlText;
    }

    // ── Service management ────────────────────────────────────────────────────

    private void ToggleServiceInstall()
    {
        try
        {
            bool wasAdmin = WindowsServiceManager.IsAdministrator();

            if (WindowsServiceManager.IsInstalled())
            {
                if (MessageBox.Show(
                    "Uninstall the Windows Service?\n\nThe relay will be stopped and the GUI will restart in standalone mode.",
                    "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return;

                if (_connection.IsRunning) StopRelay();
                WindowsServiceManager.Uninstall();

                if (wasAdmin)
                    Application.Restart();
                else
                    MessageBox.Show(
                        "Uninstall launched elevated. Please restart the management GUI when complete.",
                        "Service Uninstall", MessageBoxButtons.OK, MessageBoxIcon.Information);

                RefreshServiceButtons();
            }
            else
            {
                WindowsServiceManager.Install(Application.ExecutablePath);

                if (wasAdmin && WindowsServiceManager.IsInstalled())
                {
                    MessageBox.Show(
                        "Service installed. The GUI will restart in service management mode.",
                        "Service Installed", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    Application.Restart();
                }
                else
                {
                    MessageBox.Show(
                        "Install launched elevated. Please restart the management GUI when complete.",
                        "Service Install", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    RefreshServiceButtons();
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Service Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ToggleServiceRunning()
    {
        try
        {
            var status = WindowsServiceManager.GetStatus();
            if (status == ServiceControllerStatus.Running)
            {
                WindowsServiceManager.TryStop();
                _logger.Info("Service stopped.");
            }
            else
            {
                WindowsServiceManager.TryStart();
                _logger.Success("Service started.");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Service Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        RefreshServiceButtons();
    }

    // ── UI refresh ────────────────────────────────────────────────────────────

    private void UpdateStatusBar()
    {
        if (InvokeRequired) { Invoke(UpdateStatusBar); return; }

        var running = _connection.IsRunning;
        var svcMode = _connection is ServiceRelayConnection;
        var cfg     = _configManager.Config;

        _lblRunState.Text = running ? "RUNNING" : "STOPPED";
        _lblRunState.ForeColor = running
            ? Color.FromArgb(80, 210, 80)
            : Color.FromArgb(210, 80, 80);

        _btnStartStop.Text = running
            ? (svcMode ? "Stop Service" : "Stop Relay")
            : (svcMode ? "Start Service" : "Start Relay");

        _btnStartStop.Enabled = svcMode || cfg.IsWorkspaceConfigured;

        var ftpTag = cfg.FtpEnabled ? $"  ·  FTP:{cfg.FtpPort}" : "";
        _lblDetails.Text = running
            ? $"SMTP:{cfg.RelayPort}{ftpTag}  ·  Max {cfg.MaxMessageSizeMb} MB  ·  " +
              $"Impersonating: {cfg.ImpersonationEmail}" +
              (svcMode ? "  ·  [Windows Service]" : "")
            : cfg.IsWorkspaceConfigured
                ? $"Configured  ·  SMTP:{cfg.RelayPort}{ftpTag}  ·  Impersonating: {cfg.ImpersonationEmail}" +
                  (svcMode ? "  ·  [Windows Service]" : "")
                : "Not configured — click Configure to upload your service account key";
    }

    private void RefreshServiceButtons()
    {
        if (InvokeRequired) { Invoke(RefreshServiceButtons); return; }

        var svcMode   = _connection is ServiceRelayConnection;
        var installed = WindowsServiceManager.IsInstalled();

        _btnServiceInstall.Text = installed ? "Uninstall Service" : "Install Service";

        _btnServiceStart.Visible = !svcMode;
        _btnServiceStart.Enabled = installed && !svcMode;
        if (!svcMode)
        {
            var status = WindowsServiceManager.GetStatus();
            _btnServiceStart.Text = status == ServiceControllerStatus.Running
                ? "Stop Service" : "Start Service";
        }
    }

    private void OnLogReceived(object? sender, LogEntry e)
    {
        if (InvokeRequired) { BeginInvoke(() => OnLogReceived(sender, e)); return; }

        var color = e.Level switch
        {
            LogLevel.Debug   => Color.FromArgb(100, 140, 180),
            LogLevel.Success => Color.FromArgb(100, 210, 100),
            LogLevel.Warning => Color.FromArgb(230, 180, 60),
            LogLevel.Error   => Color.FromArgb(230, 80, 80),
            _                => Color.FromArgb(180, 180, 200)
        };

        _rtbLog.SelectionColor = Color.FromArgb(100, 100, 120);
        _rtbLog.AppendText($"{e.Timestamp:HH:mm:ss}  ");
        _rtbLog.SelectionColor = color;
        _rtbLog.AppendText($"{e.Message}{Environment.NewLine}");
        _rtbLog.ScrollToCaret();

        if (_rtbLog.Lines.Length > 2000)
        {
            _rtbLog.Select(0, _rtbLog.GetFirstCharIndexFromLine(1000));
            _rtbLog.SelectedText = "";
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _trayIcon.Dispose();
            _statusTimer.Dispose();
            _connection.Close();
            _connection.Dispose();
        }
        base.Dispose(disposing);
    }
}

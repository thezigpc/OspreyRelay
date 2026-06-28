using System.ServiceProcess;
using System.Text;
using OspreyRelay.Core.Logging;
using OspreyRelay.WorkspaceApp.Services;

namespace OspreyRelay.WorkspaceApp.Relay;

public sealed class ServiceRelayConnection : IRelayConnection
{
    private readonly string _logFilePath;
    private FileSystemWatcher? _watcher;
    private long _logPosition;
    private readonly object _readLock = new();

    public event EventHandler<LogEntry>? LogReceived;

    public bool IsRunning =>
        WindowsServiceManager.GetStatus() == ServiceControllerStatus.Running;

    public ServiceRelayConnection(string logFilePath)
    {
        _logFilePath = logFilePath;
    }

    private const int HistoryLines = 150;

    public void Open()
    {
        LoadHistory(HistoryLines);

        var dir  = Path.GetDirectoryName(_logFilePath)!;
        var file = Path.GetFileName(_logFilePath);

        _watcher = new FileSystemWatcher(dir, file)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true
        };
        _watcher.Changed += (_, _) => ReadNewLines();
    }

    public void Close()
    {
        if (_watcher == null) return;
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        _watcher = null;
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        WindowsServiceManager.TryStart();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        WindowsServiceManager.TryStop();
        return Task.CompletedTask;
    }

    public void Dispose() => Close();

    private void LoadHistory(int maxLines)
    {
        if (!File.Exists(_logFilePath)) { _logPosition = 0; return; }
        try
        {
            string content;
            using (var fs = new FileStream(_logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                _logPosition = fs.Length;
                fs.Seek(0, SeekOrigin.Begin);
                using var reader = new StreamReader(fs, Encoding.UTF8, leaveOpen: true);
                content = reader.ReadToEnd();
            }
            foreach (var line in content.Split('\n', StringSplitOptions.RemoveEmptyEntries).TakeLast(maxLines))
            {
                var entry = ParseLine(line.TrimEnd('\r'));
                if (entry != null) LogReceived?.Invoke(this, entry);
            }
        }
        catch { _logPosition = 0; }
    }

    private void ReadNewLines()
    {
        if (!File.Exists(_logFilePath)) return;
        lock (_readLock)
        {
            try
            {
                using var fs = new FileStream(_logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (fs.Length < _logPosition) _logPosition = 0;
                fs.Seek(_logPosition, SeekOrigin.Begin);
                using var reader = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: false);
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    var entry = ParseLine(line);
                    if (entry != null) LogReceived?.Invoke(this, entry);
                }
                _logPosition = fs.Position;
            }
            catch { }
        }
    }

    private static LogEntry? ParseLine(string line)
    {
        if (line.Length < 31) return null;
        if (!DateTime.TryParse(line[..19], out var ts)) return null;
        var levelStr = line[21..28].Trim();
        var message  = line[30..];
        var level = levelStr switch
        {
            "Debug"   => LogLevel.Debug,
            "Info"    => LogLevel.Info,
            "Success" => LogLevel.Success,
            "Warning" => LogLevel.Warning,
            "Error"   => LogLevel.Error,
            _         => LogLevel.Info
        };
        return new LogEntry(ts, level, message);
    }
}

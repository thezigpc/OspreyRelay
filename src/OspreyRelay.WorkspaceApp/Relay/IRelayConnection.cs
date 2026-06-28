using OspreyRelay.Core.Logging;

namespace OspreyRelay.WorkspaceApp.Relay;

public interface IRelayConnection : IDisposable
{
    bool IsRunning { get; }
    event EventHandler<LogEntry> LogReceived;
    void Open();
    void Close();
    Task StartAsync(CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);
}

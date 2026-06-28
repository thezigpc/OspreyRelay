using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using OspreyRelay.Core.Config;
using OspreyRelay.Core.Logging;
using OspreyRelay.WorkspaceApp.Service;

// Elevated reinstall/uninstall triggered by the UI via runas
if (args.Contains("--installservice"))
{
    OspreyRelay.WorkspaceApp.Services.WindowsServiceManager.Install(Environment.ProcessPath!);
    return;
}
if (args.Contains("--uninstallservice"))
{
    OspreyRelay.WorkspaceApp.Services.WindowsServiceManager.Uninstall();
    return;
}

bool serviceMode = WindowsServiceHelpers.IsWindowsService() || args.Contains("--service");

if (serviceMode)
{
    var host = Host.CreateDefaultBuilder(args)
        .UseWindowsService(opts => opts.ServiceName = "OspreyRelayWorkspace")
        .ConfigureServices(services =>
        {
            services.AddSingleton<ConfigManager>(_ => new ConfigManager("OspreyRelayWorkspace"));
            services.AddSingleton<RelayLogger>(provider =>
                new RelayLogger(provider.GetRequiredService<ConfigManager>().GetLogPath()));
            services.AddHostedService<RelayHostedService>();
        })
        .Build();

    await host.RunAsync();
    return;
}

// ── GUI mode — single-instance guard ─────────────────────────────────────────
using var mutex = new Mutex(true, @"Global\OspreyRelayWorkspaceGUI", out bool isFirstInstance);
if (!isFirstInstance)
{
    var hwnd = OspreyRelay.WorkspaceApp.NativeMethods.FindWindow(null, "Osprey Relay for Workspace");
    if (hwnd != IntPtr.Zero)
    {
        OspreyRelay.WorkspaceApp.NativeMethods.ShowWindow(hwnd, OspreyRelay.WorkspaceApp.NativeMethods.SW_RESTORE);
        OspreyRelay.WorkspaceApp.NativeMethods.SetForegroundWindow(hwnd);
    }
    return;
}

ApplicationConfiguration.Initialize();
Application.Run(new OspreyRelay.WorkspaceApp.Forms.MainForm());

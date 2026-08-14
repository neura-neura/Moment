using System.Windows;

namespace QuickCaptureBridge;

public partial class App : System.Windows.Application
{
    private BridgeController? _controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _controller = new BridgeController(this, e.Args.Contains("--background", StringComparer.OrdinalIgnoreCase));
        _controller.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Dispose();
        base.OnExit(e);
    }
}

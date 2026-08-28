using System.Threading;
using System.Windows;
using LockPC.App.Core;
using LockPC.App.Services;
using LockPC.App.Views;
using MessageBox = System.Windows.MessageBox;

namespace LockPC.App;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstance;
    private StateStore? _store;
    private ScheduleEngine? _engine;
    private OverlayManager? _overlays;
    private TrayService? _tray;
    private MainWindow? _mainWindow;
    private SleepWarningWindow? _warningWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(true, "LockPC.App.SingleInstance", out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show("AI退烧贴已经在运行。", "AI退烧贴", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        _store = new StateStore();
        _engine = new ScheduleEngine(_store);
        _overlays = new OverlayManager(_engine);
        _mainWindow = new MainWindow(_engine, _store);
        _tray = new TrayService(_mainWindow, _engine, RequestExit);

        _engine.SleepWarningRequested += OnSleepWarningRequested;
        _engine.StateChanged += (_, snapshot) =>
        {
            if (snapshot.Phase is LockPhase.Rest or LockPhase.SleepLock)
                _mainWindow.Hide();
        };

        _mainWindow.Show();
        _engine.Start();
    }

    private void OnSleepWarningRequested(object? sender, SleepWarningEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (_warningWindow?.IsVisible == true)
                return;

            _warningWindow = new SleepWarningWindow(e.ScheduledStartLocal, e.ScheduledEndLocal);
            _warningWindow.DelayRequested += (_, minutes) => _engine?.DelayCurrentSleepOccurrence(minutes);
            _warningWindow.Closed += (_, _) => _warningWindow = null;
            _warningWindow.Show();
        });
    }

    private void RequestExit()
    {
        if (_engine?.CurrentSnapshot.IsPlanActive == true)
        {
            MessageBox.Show("退烧计划仍在执行，结束前不能退出应用。", "退烧计划正在执行", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _engine?.Dispose();
        _overlays?.Dispose();
        _tray?.Dispose();
        _mainWindow?.AllowApplicationClose();
        _singleInstance?.ReleaseMutex();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _engine?.Dispose();
        _overlays?.Dispose();
        _tray?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}

using System.Threading;
using System.Windows;
using Microsoft.Win32;
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
    private CelebrationManager? _celebrations;
    private TrayService? _tray;
    private MainWindow? _mainWindow;
    private SleepWarningWindow? _warningWindow;
    private LockTransitionWindow? _transitionWindow;
    private LockPhase _lastObservedPhase = LockPhase.Idle;

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
        _celebrations = new CelebrationManager(_engine);
        _mainWindow = new MainWindow(_engine, _store);
        _tray = new TrayService(_mainWindow, _engine, RequestExit);

        _engine.SleepWarningRequested += OnSleepWarningRequested;
        _engine.LockTransitionRequested += OnLockTransitionRequested;
        _engine.StateChanged += OnEngineStateChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;

        _mainWindow.Show();
        // A persisted pending celebration means the app has started on the user's desktop.
        _engine.SetSessionLocked(false);
        _engine.Start();
    }

    private void OnSleepWarningRequested(object? sender, SleepWarningEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (_warningWindow?.IsVisible == true)
                return;

            _warningWindow = new SleepWarningWindow(e.ScheduledStartLocal, e.ScheduledEndLocal,
                e.CanDelay, e.OccurrenceDate);
            _warningWindow.DelayRequested += (_, request) => _engine?.DelayCurrentSleepOccurrence(
                request.OccurrenceDate, request.Minutes, request.Source);
            _warningWindow.Closed += (_, _) => _warningWindow = null;
            _warningWindow.Show();
        });
    }

    private void OnLockTransitionRequested(object? sender, LockTransitionEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            CloseTransitionWindow();
            if (e.Kind == LockTransitionKind.Sleep)
                CloseSleepWarningWindow();

            var window = new LockTransitionWindow(e);
            _transitionWindow = window;
            window.DelayRequested += (_, request) =>
            {
                if (_transitionWindow != window) return;
                _engine?.DelayCurrentSleepOccurrence(
                    request.OccurrenceDate, request.Minutes, request.Source);
                CloseTransitionWindow();
            };
            window.Closed += (_, _) =>
            {
                if (_transitionWindow == window)
                    _transitionWindow = null;
            };
            window.Show();
        });
    }

    private void OnEngineStateChanged(object? sender, RuntimeSnapshot snapshot)
    {
        Dispatcher.Invoke(() =>
        {
            if (snapshot.Phase is LockPhase.Rest or LockPhase.SleepLock)
                _mainWindow?.Hide();

            if (_transitionWindow?.Kind == LockTransitionKind.Rest &&
                snapshot.Phase is not (LockPhase.Focus or LockPhase.RestTransitionPreview))
                CloseTransitionWindow();
            else if (_transitionWindow?.Kind == LockTransitionKind.Sleep &&
                snapshot.Phase is LockPhase.SleepLock or LockPhase.SleepPreview)
                CloseTransitionWindow();

            if (snapshot.Phase == LockPhase.SleepLock && _lastObservedPhase != LockPhase.SleepLock)
            {
                var locked = WindowsLockService.TryLockWorkstation();
                _engine?.SetSessionLocked(locked);
            }
            _lastObservedPhase = snapshot.Phase;
        });
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is not (SessionSwitchReason.SessionLock or SessionSwitchReason.SessionUnlock)) return;
        Dispatcher.BeginInvoke(() => _engine?.SetSessionLocked(e.Reason == SessionSwitchReason.SessionLock));
    }

    private void CloseSleepWarningWindow()
    {
        if (_warningWindow is null) return;
        _warningWindow.Close();
        _warningWindow = null;
    }

    private void CloseTransitionWindow()
    {
        if (_transitionWindow is null) return;
        var window = _transitionWindow;
        _transitionWindow = null;
        window.AllowClose = true;
        window.Close();
    }

    private void RequestExit()
    {
        if (_engine?.CurrentSnapshot.IsPlanActive == true)
        {
            MessageBox.Show("退烧计划仍在执行，结束前不能退出应用。", "退烧计划正在执行", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        CloseSleepWarningWindow();
        CloseTransitionWindow();
        _engine?.Dispose();
        _overlays?.Dispose();
        _celebrations?.Dispose();
        _tray?.Dispose();
        _mainWindow?.AllowApplicationClose();
        _singleInstance?.ReleaseMutex();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        CloseSleepWarningWindow();
        CloseTransitionWindow();
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        if (_engine is not null)
        {
            _engine.SleepWarningRequested -= OnSleepWarningRequested;
            _engine.LockTransitionRequested -= OnLockTransitionRequested;
            _engine.StateChanged -= OnEngineStateChanged;
        }
        _engine?.Dispose();
        _overlays?.Dispose();
        _celebrations?.Dispose();
        _tray?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}

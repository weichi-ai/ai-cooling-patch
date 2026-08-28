using Microsoft.Win32;
using LockPC.App.Core;
using LockPC.App.Views;

namespace LockPC.App.Services;

public sealed class OverlayManager : IDisposable
{
    private readonly ScheduleEngine _engine;
    private readonly List<LockOverlayWindow> _windows = [];
    private readonly KeyboardBlocker _keyboardBlocker = new();
    private LockPhase _visiblePhase = LockPhase.Idle;
    private bool _reasonDialogOpen;

    public OverlayManager(ScheduleEngine engine)
    {
        _engine = engine;
        _engine.StateChanged += OnStateChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    private void OnStateChanged(object? sender, RuntimeSnapshot snapshot)
    {
        var shouldLock = snapshot.Phase is LockPhase.Rest or LockPhase.SleepLock or LockPhase.RestPreview or LockPhase.SleepPreview;
        if (!shouldLock)
        {
            CloseAll();
            return;
        }

        if (_windows.Count == 0 || _visiblePhase != snapshot.Phase)
        {
            CloseAll();
            _visiblePhase = snapshot.Phase;
            ShowOnAllScreens(snapshot);
        }

        foreach (var window in _windows)
            window.UpdateSnapshot(snapshot);
    }

    private void ShowOnAllScreens(RuntimeSnapshot snapshot)
    {
        var isSleep = snapshot.Phase is LockPhase.SleepLock or LockPhase.SleepPreview;
        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
        {
            var window = new LockOverlayWindow(screen, isSleep);
            window.EarlyRestRequested += OnEarlyRestRequested;
            _windows.Add(window);
            window.Show();
            window.UpdateSnapshot(snapshot);
        }

        _keyboardBlocker.Start();
        _windows.FirstOrDefault(window => window.IsPrimaryScreen)?.Activate();
    }

    private void OnEarlyRestRequested(object? sender, EventArgs e)
    {
        if (_reasonDialogOpen || sender is not LockOverlayWindow sourceWindow ||
            _engine.CurrentSnapshot.Phase != LockPhase.Rest)
            return;

        _reasonDialogOpen = true;
        foreach (var window in _windows)
            window.SuspendFocusRecovery = true;
        _keyboardBlocker.Stop();

        try
        {
            var dialog = new CancelPlanWindow(ReasonPromptPurpose.EndRestEarly)
            {
                Owner = sourceWindow,
                Topmost = true
            };
            if (dialog.ShowDialog() == true)
                _engine.EndRestEarly(dialog.CancellationReason);
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(exception.Message, "无法结束休息",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
        finally
        {
            _reasonDialogOpen = false;
            foreach (var window in _windows)
                window.SuspendFocusRecovery = false;

            if (_engine.CurrentSnapshot.Phase == LockPhase.Rest)
            {
                _keyboardBlocker.Start();
                _windows.FirstOrDefault(window => window.IsPrimaryScreen)?.Activate();
            }
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            if (_windows.Count == 0)
                return;

            var snapshot = _engine.CurrentSnapshot;
            CloseAll();
            if (snapshot.Phase is LockPhase.Rest or LockPhase.SleepLock or LockPhase.RestPreview or LockPhase.SleepPreview)
                ShowOnAllScreens(snapshot);
        });
    }

    private void CloseAll()
    {
        _keyboardBlocker.Stop();
        foreach (var window in _windows.ToArray())
        {
            window.AllowClose = true;
            window.EarlyRestRequested -= OnEarlyRestRequested;
            window.Close();
        }
        _windows.Clear();
        _visiblePhase = LockPhase.Idle;
    }

    public void Dispose()
    {
        _engine.StateChanged -= OnStateChanged;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        CloseAll();
        _keyboardBlocker.Dispose();
    }
}

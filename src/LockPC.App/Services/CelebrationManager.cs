using Microsoft.Win32;
using LockPC.App.Core;
using LockPC.App.Views;

namespace LockPC.App.Services;

public sealed class CelebrationManager : IDisposable
{
    private readonly ScheduleEngine _engine;
    private readonly List<CelebrationWindow> _windows = [];
    private readonly KeyboardBlocker _keyboardBlocker = new();
    private LockPhase _visiblePhase = LockPhase.Idle;

    public CelebrationManager(ScheduleEngine engine)
    {
        _engine = engine;
        _engine.StateChanged += OnStateChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    private void OnStateChanged(object? sender, RuntimeSnapshot snapshot)
    {
        if (!IsCelebration(snapshot.Phase))
        {
            CloseAll();
            return;
        }

        if (_windows.Count > 0 && _visiblePhase == snapshot.Phase) return;
        CloseAll();
        _visiblePhase = snapshot.Phase;
        var isSleep = snapshot.Phase is LockPhase.SleepCelebration or LockPhase.SleepCelebrationPreview;
        var isPreview = snapshot.Phase is LockPhase.RestCelebrationPreview or LockPhase.SleepCelebrationPreview;
        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
        {
            var window = new CelebrationWindow(screen, isSleep, isPreview);
            _windows.Add(window);
            window.Show();
        }
        _keyboardBlocker.Start();
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            if (_windows.Count == 0) return;
            var phase = _visiblePhase;
            CloseAll();
            OnStateChanged(this, _engine.CurrentSnapshot with { Phase = phase });
        });

    private static bool IsCelebration(LockPhase phase) => phase is LockPhase.RestCelebration or
        LockPhase.SleepCelebration or LockPhase.RestCelebrationPreview or LockPhase.SleepCelebrationPreview;

    private void CloseAll()
    {
        _keyboardBlocker.Stop();
        foreach (var window in _windows.ToArray())
        {
            window.AllowClose = true;
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

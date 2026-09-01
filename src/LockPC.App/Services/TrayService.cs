using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using LockPC.App.Core;

namespace LockPC.App.Services;

public sealed class TrayService : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly ScheduleEngine _engine;
    private readonly Action _exitAction;

    public TrayService(MainWindow mainWindow, ScheduleEngine engine, Action exitAction)
    {
        _engine = engine;
        _exitAction = exitAction;
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("打开 AI退烧贴", null, (_, _) => ShowMainWindow(mainWindow));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => _exitAction());

        _notifyIcon = new Forms.NotifyIcon
        {
            Text = BuildTooltip(_engine.CurrentSnapshot),
            Icon = Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? Drawing.SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => ShowMainWindow(mainWindow);
        _engine.StateChanged += OnStateChanged;
    }

    private void OnStateChanged(object? sender, RuntimeSnapshot snapshot)
    {
        _notifyIcon.Text = BuildTooltip(snapshot);
    }

    public static string BuildTooltip(RuntimeSnapshot snapshot) => snapshot.Phase switch
    {
        LockPhase.Idle => "AI退烧贴\n当前没有专注计划",
        LockPhase.Focus => $"专注中 · 第 {snapshot.CurrentRound}/{snapshot.TotalRounds} 轮\n{FormatRemaining(snapshot.Remaining)} 后进入离屏休息",
        LockPhase.Rest => $"离屏休息 · 第 {snapshot.CurrentRound}/{snapshot.TotalRounds} 轮\n剩余 {FormatRemaining(snapshot.Remaining)}",
        _ => $"AI退烧贴\n{snapshot.StatusText}"
    };

    private static string FormatRemaining(TimeSpan remaining) => remaining.TotalHours >= 1
        ? $"{(int)remaining.TotalHours}:{remaining.Minutes:00}:{remaining.Seconds:00}"
        : $"{remaining.Minutes:00}:{remaining.Seconds:00}";

    private void ShowMainWindow(MainWindow window)
    {
        if (_engine.CurrentSnapshot.Phase is LockPhase.Rest or LockPhase.SleepLock or LockPhase.RestPreview or LockPhase.SleepPreview)
            return;

        window.Show();
        window.WindowState = System.Windows.WindowState.Normal;
        window.Activate();
    }

    public void Dispose()
    {
        _engine.StateChanged -= OnStateChanged;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}

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
            Text = "AI退烧贴",
            Icon = Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? Drawing.SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => ShowMainWindow(mainWindow);
        _engine.StateChanged += OnStateChanged;
    }

    private void OnStateChanged(object? sender, RuntimeSnapshot snapshot)
    {
        _notifyIcon.Text = snapshot.Phase == LockPhase.Idle
            ? "AI退烧贴 · 当前没有退烧计划"
            : $"AI退烧贴 · {snapshot.StatusText}";
    }

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

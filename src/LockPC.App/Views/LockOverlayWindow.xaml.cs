using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using LockPC.App.Core;
using Forms = System.Windows.Forms;

namespace LockPC.App.Views;

public partial class LockOverlayWindow : Window
{
    private readonly bool _isSleep;
    private readonly System.Drawing.Rectangle _screenBounds;

    public bool AllowClose { get; set; }
    public bool SuspendFocusRecovery { get; set; }
    public bool IsPrimaryScreen { get; }
    public event EventHandler? EarlyRestRequested;

    public LockOverlayWindow(Forms.Screen screen, bool isSleep)
    {
        InitializeComponent();
        _isSleep = isSleep;
        _screenBounds = screen.Bounds;
        IsPrimaryScreen = screen.Primary;

        Left = screen.Bounds.Left;
        Top = screen.Bounds.Top;
        Width = screen.Bounds.Width;
        Height = screen.Bounds.Height;
        WindowStartupLocation = WindowStartupLocation.Manual;
        SourceInitialized += (_, _) => PositionWithPhysicalPixels();

        if (isSleep)
        {
            GradientStart.Color = System.Windows.Media.Color.FromRgb(40, 60, 91);
            GradientEnd.Color = System.Windows.Media.Color.FromRgb(10, 17, 31);
            Backdrop.Opacity = 1;
            KickerText.Text = "夜间退烧计划已生效";
            TitleText.Text = "今日 AI 亲密额度已用完。";
            DescriptionText.Text = "明天再聊，现在人类需要休眠。";
            FooterText.Text = "电脑可以息屏，但今晚不能提前撕贴。";
        }
        else
        {
            Backdrop.Opacity = 0.82;
        }
    }

    private void PositionWithPhysicalPixels()
    {
        var handle = new WindowInteropHelper(this).Handle;
        SetWindowPos(handle, IntPtr.Zero, _screenBounds.Left, _screenBounds.Top,
            _screenBounds.Width, _screenBounds.Height, 0x0010 | 0x0040);
    }

    public void UpdateSnapshot(RuntimeSnapshot snapshot)
    {
        Dispatcher.Invoke(() =>
        {
            var remaining = snapshot.Remaining;
            CountdownText.Text = remaining.TotalHours >= 1
                ? $"{(int)remaining.TotalHours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}"
                : $"{remaining.Minutes:00}:{remaining.Seconds:00}";

            if (!_isSleep && snapshot.TotalRounds > 0)
                KickerText.Text = $"第 {snapshot.CurrentRound} / {snapshot.TotalRounds} 轮 · 强制降温";
            EarlyEndButton.Visibility = snapshot.Phase == LockPhase.Rest && IsPrimaryScreen
                ? Visibility.Visible
                : Visibility.Collapsed;
        });
    }

    private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e) => e.Handled = true;

    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (SuspendFocusRecovery)
            return;
        Topmost = false;
        Topmost = true;
        Activate();
    }

    private void EarlyEndButton_Click(object sender, RoutedEventArgs e) => EarlyRestRequested?.Invoke(this, EventArgs.Empty);

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!AllowClose)
            e.Cancel = true;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y,
        int width, int height, uint flags);
}

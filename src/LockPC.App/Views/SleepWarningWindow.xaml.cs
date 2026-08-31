using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace LockPC.App.Views;

public partial class SleepWarningWindow : Window
{
    private readonly DateTime _scheduledStart;
    private readonly DispatcherTimer _timer;

    public event EventHandler<int>? DelayRequested;

    public SleepWarningWindow(DateTime scheduledStart, DateTime scheduledEnd, bool canDelay)
    {
        InitializeComponent();
        _scheduledStart = scheduledStart;
        MessageText.Text = $"睡眠保护将在 30 秒后开始，请立即保存文件和正在进行的工作。到点后电脑将锁定，{scheduledEnd:MM月dd日 HH:mm} 自动解除。";
        SetDelayAvailable(canDelay);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => UpdateCountdown();
        _timer.Start();
        UpdateCountdown();
    }

    private void UpdateCountdown()
    {
        var remaining = _scheduledStart - DateTime.Now;
        if (remaining < TimeSpan.Zero)
            remaining = TimeSpan.Zero;
        CountdownText.Text = $"{(int)remaining.TotalMinutes:00}:{remaining.Seconds:00}";
        if (remaining == TimeSpan.Zero)
            Close();
    }

    private void Delay_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string text } && int.TryParse(text, out var minutes))
        {
            SetDelayAvailable(false);
            DelayRequested?.Invoke(this, minutes);
            Close();
        }
    }

    private void SetDelayAvailable(bool available)
    {
        DelayButtonsPanel.IsEnabled = available;
        DelayHintText.Text = available
            ? "今晚还可以申请最后一次延迟，最长 30 分钟。"
            : "今晚已经延迟过一次，不能再次延迟。";
    }

    private void Acknowledge_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        base.OnClosed(e);
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using LockPC.App.Core;

namespace LockPC.App.Views;

public partial class SleepWarningWindow : Window
{
    private readonly DateTime _scheduledStart;
    private readonly DateOnly _occurrenceDate;
    private readonly DispatcherTimer _timer;

    public event EventHandler<SleepDelayRequestEventArgs>? DelayRequested;

    public SleepWarningWindow(DateTime scheduledStart, DateTime scheduledEnd, bool canDelay,
        DateOnly occurrenceDate)
    {
        InitializeComponent();
        _scheduledStart = scheduledStart;
        _occurrenceDate = occurrenceDate;
        MessageText.Text = "睡眠保护将在 30 秒后开始。请先保存正在处理的文件并退出运行中的闲置应用，届时电脑会自动锁定。";
        UnlockTimeText.Text = scheduledEnd.Date == DateTime.Today.AddDays(1)
            ? $"明早 {scheduledEnd:HH:mm}"
            : $"{scheduledEnd:MM月dd日 HH:mm}";
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
        CountdownProgress.Value = Math.Min(30, remaining.TotalSeconds);
        if (remaining == TimeSpan.Zero)
            Close();
    }

    private void Delay_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string text } && int.TryParse(text, out var minutes))
        {
            SetDelayAvailable(false);
            DelayRequested?.Invoke(this, new SleepDelayRequestEventArgs(
                _occurrenceDate, minutes, SleepDelaySource.WarningWindow));
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

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Acknowledge_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        base.OnClosed(e);
    }
}

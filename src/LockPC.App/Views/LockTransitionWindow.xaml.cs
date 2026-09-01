using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using LockPC.App.Core;

namespace LockPC.App.Views;

public partial class LockTransitionWindow : Window
{
    private readonly DateTimeOffset _locksAtUtc;
    private readonly DispatcherTimer _timer;
    private int _displayedSeconds = -1;
    private readonly DateOnly? _sleepOccurrenceDate;

    public LockTransitionKind Kind { get; }
    public bool AllowClose { get; set; }
    public event EventHandler<SleepDelayRequestEventArgs>? DelayRequested;

    public LockTransitionWindow(LockTransitionEventArgs transition)
    {
        InitializeComponent();
        Kind = transition.Kind;
        _locksAtUtc = transition.LocksAtUtc;
        _sleepOccurrenceDate = transition.SleepOccurrenceDate;

        Left = Math.Max(0, (SystemParameters.PrimaryScreenWidth - Width) / 2);
        Top = Math.Max(0, (SystemParameters.PrimaryScreenHeight - Height) / 2);

        if (Kind == LockTransitionKind.Sleep)
        {
            DialogSurface.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xF5, 0x14, 0x21, 0x38));
            ModeText.Text = "睡眠保护";
            TitleText.Text = "即将进入睡眠保护模式";
            DelayPanel.Visibility = transition.CanDelaySleep ? Visibility.Visible : Visibility.Collapsed;
            Height = transition.CanDelaySleep ? 430 : 360;
            Top = Math.Max(0, (SystemParameters.PrimaryScreenHeight - Height) / 2);
        }
        else
        {
            Height = 360;
            Top = Math.Max(0, (SystemParameters.PrimaryScreenHeight - Height) / 2);
        }

        _timer = new DispatcherTimer(DispatcherPriority.Send) { Interval = TimeSpan.FromMilliseconds(100) };
        _timer.Tick += (_, _) => UpdateCountdown();
        Loaded += (_, _) =>
        {
            StartRingAnimation();
            UpdateCountdown();
            _timer.Start();
        };
    }

    private void StartRingAnimation()
    {
        var remaining = _locksAtUtc - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero) return;
        RingRotate.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty,
            new DoubleAnimation(0, 360, remaining) { EasingFunction = new SineEase() });
    }

    private void UpdateCountdown()
    {
        var remaining = _locksAtUtc - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            CountdownText.Text = "00";
            CountdownProgress.Value = 0;
            if (remaining <= TimeSpan.FromSeconds(-2))
            {
                AllowClose = true;
                Close();
            }
            return;
        }

        var seconds = Math.Clamp((int)Math.Ceiling(remaining.TotalSeconds), 1, AppMetadata.LockTransitionSeconds);
        CountdownProgress.Value = Math.Clamp(remaining.TotalSeconds, 0, AppMetadata.LockTransitionSeconds);
        if (seconds == _displayedSeconds) return;

        _displayedSeconds = seconds;
        CountdownText.Text = seconds.ToString("00");
        NumberScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.86, 1, TimeSpan.FromMilliseconds(180)));
        NumberScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.86, 1, TimeSpan.FromMilliseconds(180)));
        CountdownText.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0.55, 1, TimeSpan.FromMilliseconds(180)));
    }

    private void Delay_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string text } && int.TryParse(text, out var minutes) &&
            _sleepOccurrenceDate is { } occurrenceDate)
        {
            DelayPanel.IsEnabled = false;
            DelayRequested?.Invoke(this, new SleepDelayRequestEventArgs(
                occurrenceDate, minutes, SleepDelaySource.TransitionWindow));
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!AllowClose)
        {
            e.Cancel = true;
            return;
        }
        _timer.Stop();
    }
}

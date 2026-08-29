using System.ComponentModel;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using LockPC.App.Core;
using Forms = System.Windows.Forms;

namespace LockPC.App.Views;

public partial class LockOverlayWindow : Window
{
    private readonly bool _isSleep;
    private readonly System.Drawing.Rectangle _screenBounds;
    private bool _peeling;

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
        Left = screen.Bounds.Left; Top = screen.Bounds.Top; Width = screen.Bounds.Width; Height = screen.Bounds.Height;
        WindowStartupLocation = WindowStartupLocation.Manual;
        SourceInitialized += (_, _) => PositionWithPhysicalPixels();
        if (isSleep)
        {
            GradientStart.Color = System.Windows.Media.Color.FromRgb(36, 53, 82);
            GradientEnd.Color = System.Windows.Media.Color.FromRgb(8, 16, 30);
            KickerText.Text = "睡眠保护 · 正在生效";
            TitleText.Text = "今日 AI 亲密额度已用完。";
            DescriptionText.Text = "明天再聊，现在人类需要休眠。";
            FooterText.Text = "电脑可以息屏，但今晚不能提前撕贴。";
            CoolingPanel.Visibility = Visibility.Collapsed;
            CoolingProgress.Visibility = Visibility.Collapsed;
        }
    }

    private void PositionWithPhysicalPixels()
    {
        var handle = new WindowInteropHelper(this).Handle;
        SetWindowPos(handle, IntPtr.Zero, _screenBounds.Left, _screenBounds.Top, _screenBounds.Width, _screenBounds.Height, 0x0010 | 0x0040);
    }

    public void UpdateSnapshot(RuntimeSnapshot snapshot)
    {
        Dispatcher.Invoke(() =>
        {
            var remaining = snapshot.Remaining;
            CountdownText.Text = remaining.TotalHours >= 1
                ? $"{(int)remaining.TotalHours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}"
                : $"{remaining.Minutes:00}:{remaining.Seconds:00}";
            if (!_isSleep)
            {
                KickerText.Text = snapshot.Phase == LockPhase.RestPeelPreview
                    ? "演示提前撕贴 · 退烧贴正在生效"
                    : snapshot.TotalRounds > 0 ? $"退烧贴正在生效 · 第 {snapshot.CurrentRound}/{snapshot.TotalRounds} 轮" : "退烧贴正在生效";
                var percent = (int)Math.Round(snapshot.PhaseProgress * 100);
                CoolingProgress.Value = percent;
                CoolingPercentText.Text = $"正在降温 {percent}%";
            }
            EarlyEndButton.Visibility = snapshot.Phase is LockPhase.Rest or LockPhase.RestPeelPreview && IsPrimaryScreen
                ? Visibility.Visible : Visibility.Collapsed;
        });
    }

    public async Task PlayPeelAnimationAsync(bool playSound)
    {
        if (_peeling) return;
        _peeling = true;
        EarlyEndButton.IsEnabled = false;
        if (playSound && IsPrimaryScreen) PlayPeelSound();
        var duration = TimeSpan.FromMilliseconds(900);
        PatchFold.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        PatchScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 1.04, duration));
        PatchScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, new DoubleAnimation(1, 1.04, duration));
        PatchRotate.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, new DoubleAnimation(0, -12, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
        PatchTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, new DoubleAnimation(0, 310, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
        PatchTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, new DoubleAnimation(0, -150, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
        var fade = new DoubleAnimation(1, 0, duration);
        var completed = new TaskCompletionSource();
        fade.Completed += (_, _) => completed.TrySetResult();
        CoolingPatch.BeginAnimation(OpacityProperty, fade);
        await completed.Task;
    }

    private static void PlayPeelSound()
    {
        try
        {
            const int rate = 22050;
            const int samples = 3400;
            var bytes = new byte[44 + samples * 2];
            using var stream = new MemoryStream(bytes);
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, true))
            {
                writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + samples * 2);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write((short)1);
                writer.Write((short)1); writer.Write(rate); writer.Write(rate * 2); writer.Write((short)2); writer.Write((short)16);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(samples * 2);
                var random = new Random(17);
                for (var i = 0; i < samples; i++)
                {
                    var envelope = 1d - (double)i / samples;
                    var texture = (random.NextDouble() * 2 - 1) * .55 + Math.Sin(i * .055) * .45;
                    writer.Write((short)(texture * envelope * 9000));
                }
            }
            stream.Position = 0;
            var player = new SoundPlayer(stream);
            player.Play();
        }
        catch { }
    }

    private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e) => e.Handled = true;
    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (SuspendFocusRecovery) return;
        Topmost = false; Topmost = true; Activate();
    }
    private void EarlyEndButton_Click(object sender, RoutedEventArgs e) => EarlyRestRequested?.Invoke(this, EventArgs.Empty);
    private void OnClosing(object? sender, CancelEventArgs e) { if (!AllowClose) e.Cancel = true; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int width, int height, uint flags);
}
using System.ComponentModel;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Forms = System.Windows.Forms;
using MediaColor = System.Windows.Media.Color;

namespace LockPC.App.Views;

public partial class CelebrationWindow : Window
{
    private static readonly MediaColor[] ConfettiColors =
    [
        MediaColor.FromRgb(98, 225, 209), MediaColor.FromRgb(255, 214, 102),
        MediaColor.FromRgb(255, 112, 67), MediaColor.FromRgb(145, 169, 222),
        MediaColor.FromRgb(255, 255, 255), MediaColor.FromRgb(255, 132, 183)
    ];

    private readonly System.Drawing.Rectangle _screenBounds;
    private readonly bool _playSound;
    private SoundPlayer? _soundPlayer;
    private MemoryStream? _soundStream;

    public bool AllowClose { get; set; }

    public CelebrationWindow(Forms.Screen screen, bool isSleep, bool isPreview)
    {
        InitializeComponent();
        _screenBounds = screen.Bounds;
        _playSound = screen.Primary;
        Left = screen.Bounds.Left;
        Top = screen.Bounds.Top;
        Width = screen.Bounds.Width;
        Height = screen.Bounds.Height;
        SourceInitialized += (_, _) => PositionWithPhysicalPixels();
        Loaded += (_, _) => StartCelebration();

        if (isSleep)
        {
            GlowColor.Color = MediaColor.FromRgb(62, 78, 142);
            IconText.Text = "☀";
            KickerText.Text = isPreview ? "睡眠保护完成 · 演示" : "睡眠保护完成";
            TitleText.Text = "早安，今天也要清醒地出发！";
            DescriptionText.Text = "电脑已恢复使用，先伸个懒腰再开始。";
        }
        else if (isPreview)
        {
            KickerText.Text = "退烧完成 · 演示";
        }
    }

    private void StartCelebration()
    {
        var random = new Random(HashCode.Combine(_screenBounds.Left, _screenBounds.Top, 111));
        var count = Math.Clamp((int)(ActualWidth * ActualHeight / 12000), 90, 180);
        for (var i = 0; i < count; i++)
        {
            var size = random.Next(7, 15);
            Shape piece = i % 4 == 0
                ? new Ellipse { Width = size, Height = size }
                : new System.Windows.Shapes.Rectangle { Width = size * .62, Height = size * 1.45, RadiusX = 2, RadiusY = 2 };
            piece.Fill = new SolidColorBrush(ConfettiColors[random.Next(ConfettiColors.Length)]);
            piece.RenderTransformOrigin = new System.Windows.Point(.5, .5);
            var transforms = new TransformGroup();
            var rotate = new RotateTransform(random.Next(0, 360));
            transforms.Children.Add(rotate);
            piece.RenderTransform = transforms;
            Canvas.SetLeft(piece, random.NextDouble() * Math.Max(1, ActualWidth));
            Canvas.SetTop(piece, -random.Next(20, 240));
            ConfettiCanvas.Children.Add(piece);

            var delay = TimeSpan.FromMilliseconds(random.Next(0, 900));
            var duration = TimeSpan.FromMilliseconds(random.Next(2100, 3900));
            piece.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(-80, ActualHeight + 100, duration)
            {
                BeginTime = delay,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            });
            piece.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(Canvas.GetLeft(piece),
                Canvas.GetLeft(piece) + random.Next(-160, 161), duration) { BeginTime = delay });
            rotate.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(rotate.Angle, rotate.Angle + random.Next(360, 1080), duration) { BeginTime = delay });
        }

        if (_playSound) PlayCelebrationSound();
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)));
    }

    private void PlayCelebrationSound()
    {
        try
        {
            const int rate = 22050;
            const double seconds = 1.35;
            var samples = (int)(rate * seconds);
            var bytes = new byte[44 + samples * 2];
            _soundStream = new MemoryStream(bytes);
            using (var writer = new BinaryWriter(_soundStream, System.Text.Encoding.ASCII, true))
            {
                writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + samples * 2);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write((short)1);
                writer.Write((short)1); writer.Write(rate); writer.Write(rate * 2); writer.Write((short)2); writer.Write((short)16);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(samples * 2);
                var notes = new[] { 523.25, 659.25, 783.99, 1046.50 };
                for (var i = 0; i < samples; i++)
                {
                    var t = (double)i / rate;
                    var noteIndex = Math.Min(notes.Length - 1, (int)(t / .24));
                    var local = t - noteIndex * .24;
                    var envelope = Math.Exp(-3.4 * Math.Max(0, local));
                    var value = Math.Sin(2 * Math.PI * notes[noteIndex] * t) * envelope * .38;
                    writer.Write((short)(value * short.MaxValue));
                }
            }
            _soundStream.Position = 0;
            _soundPlayer = new SoundPlayer(_soundStream);
            _soundPlayer.Play();
        }
        catch { }
    }

    private void PositionWithPhysicalPixels()
    {
        var handle = new WindowInteropHelper(this).Handle;
        SetWindowPos(handle, IntPtr.Zero, _screenBounds.Left, _screenBounds.Top,
            _screenBounds.Width, _screenBounds.Height, 0x0010 | 0x0040);
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!AllowClose) { e.Cancel = true; return; }
        _soundPlayer?.Stop();
        _soundPlayer?.Dispose();
        _soundStream?.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int x, int y, int width, int height, uint flags);
}

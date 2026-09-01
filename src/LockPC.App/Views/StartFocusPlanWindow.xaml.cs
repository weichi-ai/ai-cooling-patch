using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace LockPC.App.Views;

public partial class StartFocusPlanWindow : Window
{
    public StartFocusPlanWindow(int focusMinutes, int restMinutes, int rounds)
    {
        InitializeComponent();
        PlanTitleText.Text = $"{rounds} 轮专注计划";
        FocusMinutesText.Text = $"{focusMinutes} 分钟";
        RestMinutesText.Text = $"{restMinutes} 分钟";
        RoundsText.Text = $"{rounds} 轮";
    }

    private void Window_SourceInitialized(object? sender, EventArgs e) => FitToWorkingArea();

    private void FitToWorkingArea()
    {
        var screen = Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle);
        var dpi = VisualTreeHelper.GetDpi(this);
        var workArea = screen.WorkingArea;
        var workLeft = workArea.Left / dpi.DpiScaleX;
        var workTop = workArea.Top / dpi.DpiScaleY;
        var workWidth = workArea.Width / dpi.DpiScaleX;
        var workHeight = workArea.Height / dpi.DpiScaleY;
        var availableWidth = Math.Max(420, workWidth - 32);
        var availableHeight = Math.Max(360, workHeight - 32);
        MinWidth = Math.Min(MinWidth, availableWidth);
        MinHeight = Math.Min(MinHeight, availableHeight);
        Width = Math.Min(Width, availableWidth);
        Height = Math.Min(Height, availableHeight);
        Left = workLeft + Math.Max(16, (workWidth - Width) / 2);
        Top = workTop + Math.Max(16, (workHeight - Height) / 2);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}

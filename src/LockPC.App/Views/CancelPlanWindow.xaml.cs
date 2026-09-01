using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace LockPC.App.Views;

public enum ReasonPromptPurpose
{
    CancelPlan,
    EndRestEarly
}

public partial class CancelPlanWindow : Window
{
    public string CancellationReason { get; private set; } = string.Empty;

    public CancelPlanWindow(ReasonPromptPurpose purpose = ReasonPromptPurpose.CancelPlan)
    {
        InitializeComponent();
        if (purpose == ReasonPromptPurpose.EndRestEarly)
        {
            Title = "提前撕掉退烧贴";
            HeadingText.Text = "确定要提前撕掉退烧贴？";
            DescriptionText.Text = "提前结束休息需要写下理由，记录会进入你的上头记录。";
            PlaceholderText.Text = "为什么现在必须回来？至少 5 个字";
            KeepButton.Content = "继续降温";
            ConfirmButton.Content = "记录原因并撕贴";
        }
        ReasonTextBox.Focus();
    }

    private void ReasonTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var normalized = ReasonTextBox.Text.Trim();
        var length = normalized.EnumerateRunes().Count();
        PlaceholderText.Visibility = ReasonTextBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        CountText.Text = $"{length} / 至少 5 个字";
        CountText.Foreground = length >= 5
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(46, 115, 92))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(155, 100, 41));
        ConfirmButton.IsEnabled = length >= 5;
    }

    private void KeepPlan_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ConfirmCancel_Click(object sender, RoutedEventArgs e)
    {
        var length = ReasonTextBox.Text.Trim().EnumerateRunes().Count();
        if (length < 5)
        {
            CountText.Text = $"还需输入 {5 - length} 个字";
            CountText.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(196, 67, 38));
            ReasonTextBox.Focus();
            return;
        }

        CancellationReason = ReasonTextBox.Text.Trim();
        DialogResult = true;
        Close();
    }
}

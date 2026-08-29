using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LockPC.App.Core;
using LockPC.App.Services;
using LockPC.App.Views;
using MessageBox = System.Windows.MessageBox;

namespace LockPC.App;

public partial class MainWindow : Window
{
    private readonly ScheduleEngine _engine;
    private readonly StateStore _store;
    private readonly AnalyticsService _analytics;
    private readonly UpdateService _updateService = new();
    private bool _allowClose;
    private bool _updateCheckInProgress;
    private bool _hasCheckedForUpdates;
    private string? _latestReleaseUrl;

    public MainWindow(ScheduleEngine engine, StateStore store)
    {
        InitializeComponent();
        _engine = engine; _store = store; _analytics = new AnalyticsService(store);
        _engine.StateChanged += Engine_StateChanged;
        LoadSettings(engine.Settings);
        CurrentVersionText.Text = $"当前版本 v{AppMetadata.CurrentVersionText}";
        ReleaseNotesList.ItemsSource = AppMetadata.ReleaseNotes;
        RefreshAnalytics();
        UpdateSnapshot(engine.CurrentSnapshot);
    }

    private void LoadSettings(AppSettings settings)
    {
        SelectByTag(FocusMinutesCombo, settings.FocusMinutes); SelectByTag(RestMinutesCombo, settings.RestMinutes); SelectByTag(RoundsCombo, settings.FocusRounds);
        SleepEnabledCheck.IsChecked = settings.SleepProtectionEnabled;
        SleepStartText.Text = settings.SleepStart.ToString(@"hh\:mm"); SleepEndText.Text = settings.SleepEnd.ToString(@"hh\:mm");
        MondayCheck.IsChecked = settings.SleepDays.Contains(DayOfWeek.Monday); TuesdayCheck.IsChecked = settings.SleepDays.Contains(DayOfWeek.Tuesday);
        WednesdayCheck.IsChecked = settings.SleepDays.Contains(DayOfWeek.Wednesday); ThursdayCheck.IsChecked = settings.SleepDays.Contains(DayOfWeek.Thursday);
        FridayCheck.IsChecked = settings.SleepDays.Contains(DayOfWeek.Friday); SaturdayCheck.IsChecked = settings.SleepDays.Contains(DayOfWeek.Saturday); SundayCheck.IsChecked = settings.SleepDays.Contains(DayOfWeek.Sunday);
        StartWithWindowsCheck.IsChecked = StartupManager.IsEnabled; PeelSoundCheck.IsChecked = settings.PeelSoundEnabled;
        RefreshSummaries(settings);
    }

    private static void SelectByTag(System.Windows.Controls.ComboBox comboBox, int value)
    {
        comboBox.SelectedItem = comboBox.Items.OfType<System.Windows.Controls.ComboBoxItem>().FirstOrDefault(item => item.Tag?.ToString() == value.ToString());
        if (comboBox.SelectedIndex < 0) comboBox.SelectedIndex = 0;
    }

    private void Engine_StateChanged(object? sender, RuntimeSnapshot snapshot) => Dispatcher.Invoke(() =>
    {
        UpdateSnapshot(snapshot); RefreshAnalytics();
    });

    private void UpdateSnapshot(RuntimeSnapshot snapshot)
    {
        HeaderStatusText.Text = snapshot.StatusText;
        CurrentPlanKicker.Text = snapshot.Phase switch
        {
            LockPhase.Focus => "专注模式 · 正在生效",
            LockPhase.Rest => "退烧贴正在生效",
            LockPhase.SleepLock => "睡眠保护 · 正在生效",
            _ => "当前状态"
        };
        CurrentPlanTitle.Text = snapshot.Phase switch
        {
            LockPhase.Focus => $"第 {snapshot.CurrentRound}/{snapshot.TotalRounds} 轮专注中",
            LockPhase.Rest => $"第 {snapshot.CurrentRound}/{snapshot.TotalRounds} 轮正在退烧",
            LockPhase.SleepLock => "今晚先睡，明天再聊",
            LockPhase.RestPreview => "休息模式演示中",
            LockPhase.RestPeelPreview => "提前撕贴演示中",
            LockPhase.SleepPreview => "睡眠保护演示中",
            _ => "当前没有专注计划"
        };
        CurrentPlanSubtitle.Text = snapshot.Phase switch
        {
            LockPhase.Focus => "保持专注；需要结束整组计划时，请填写理由。",
            LockPhase.Rest => $"已退烧 {Math.Round(snapshot.PhaseProgress * 100):0}% · 可填写理由提前撕贴",
            LockPhase.SleepLock => "保护期间不可暂停、提前退出或修改计划。",
            LockPhase.RestPreview or LockPhase.RestPeelPreview or LockPhase.SleepPreview => "这是界面演示，不计入数据分析。",
            _ => "今天准备和 AI 专注多久？"
        };
        CurrentCountdownText.Text = snapshot.Phase == LockPhase.Idle ? "--:--" : FormatRemaining(snapshot.Remaining);
        CancelPlanButton.Visibility = snapshot.Phase == LockPhase.Focus ? Visibility.Visible : Visibility.Collapsed;
        StatusDot.Fill = snapshot.Phase == LockPhase.Idle ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(120, 145, 143)) : (System.Windows.Media.Brush)FindResource("PrimaryBrush");
    }

    private static string FormatRemaining(TimeSpan remaining) => remaining.TotalHours >= 1
        ? $"{(int)remaining.TotalHours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}" : $"{remaining.Minutes:00}:{remaining.Seconds:00}";

    private void Navigate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button button && int.TryParse(button.Tag?.ToString(), out var index)) MainTabs.SelectedIndex = index;
    }

    private void StartPomodoro_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var focus = SelectedTag(FocusMinutesCombo); var rest = SelectedTag(RestMinutesCombo); var rounds = SelectedTag(RoundsCombo);
            var answer = MessageBox.Show($"即将开始 {rounds} 轮计划：每轮专注 {focus} 分钟，随后离屏休息 {rest} 分钟。\n\n专注中可以结束整组计划；休息中可填写理由提前撕贴。", "开始专注计划", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) return;
            var settings = BuildSettingsFromControls(); settings.FocusMinutes = focus; settings.RestMinutes = rest; settings.FocusRounds = rounds;
            _engine.UpdateSettings(settings); _engine.StartPomodoro(focus, rest, rounds); MainTabs.SelectedIndex = 0;
        }
        catch (Exception exception) { MessageBox.Show(exception.Message, "无法开始计划", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void CancelPlan_Click(object sender, RoutedEventArgs e)
    {
        if (_engine.CurrentSnapshot.Phase != LockPhase.Focus) return;
        var dialog = new CancelPlanWindow { Owner = this };
        if (dialog.ShowDialog() != true) return;
        try { _engine.CancelPomodoro(dialog.CancellationReason); RefreshAnalytics(); MessageBox.Show("本次专注计划已结束，原因已保存在本机。", "计划已结束", MessageBoxButton.OK, MessageBoxImage.Information); }
        catch (Exception exception) { MessageBox.Show(exception.Message, "无法结束计划", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void SaveSleepSettings_Click(object sender, RoutedEventArgs e)
    {
        try { var settings = BuildSettingsFromControls(); _engine.UpdateSettings(settings); RefreshSummaries(settings); MessageBox.Show("睡眠保护计划已保存。", "AI退烧贴", MessageBoxButton.OK, MessageBoxImage.Information); }
        catch (Exception exception) { MessageBox.Show(exception.Message, "无法保存计划", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void PreviewRest_Click(object sender, RoutedEventArgs e) { try { _engine.StartRestPreview(TimeSpan.FromSeconds(10)); } catch (Exception ex) { ShowPreviewError(ex); } }
    private void PreviewPeel_Click(object sender, RoutedEventArgs e) { try { _engine.StartRestPeelPreview(TimeSpan.FromSeconds(45)); } catch (Exception ex) { ShowPreviewError(ex); } }
    private void PreviewSleep_Click(object sender, RoutedEventArgs e) { try { _engine.StartSleepPreview(TimeSpan.FromSeconds(10)); } catch (Exception ex) { ShowPreviewError(ex); } }
    private static void ShowPreviewError(Exception exception) => MessageBox.Show(exception.Message, "无法演示", MessageBoxButton.OK, MessageBoxImage.Warning);

    private void SaveAppSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            StartupManager.SetEnabled(StartWithWindowsCheck.IsChecked == true);
            _engine.Settings.StartWithWindows = StartWithWindowsCheck.IsChecked == true;
            _engine.Settings.PeelSoundEnabled = PeelSoundCheck.IsChecked == true;
            _store.SaveSettings(_engine.Settings);
            MessageBox.Show("应用设置已保存。", "AI退烧贴", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception) { MessageBox.Show(exception.Message, "无法保存应用设置", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private AppSettings BuildSettingsFromControls()
    {
        if (!TimeSpan.TryParse(SleepStartText.Text.Trim(), out var start) || !TimeSpan.TryParse(SleepEndText.Text.Trim(), out var end))
            throw new ArgumentException("时间格式应为 HH:mm，例如 23:30。");
        var days = new HashSet<DayOfWeek>();
        AddDay(days, MondayCheck, DayOfWeek.Monday); AddDay(days, TuesdayCheck, DayOfWeek.Tuesday); AddDay(days, WednesdayCheck, DayOfWeek.Wednesday);
        AddDay(days, ThursdayCheck, DayOfWeek.Thursday); AddDay(days, FridayCheck, DayOfWeek.Friday); AddDay(days, SaturdayCheck, DayOfWeek.Saturday); AddDay(days, SundayCheck, DayOfWeek.Sunday);
        return new AppSettings
        {
            FocusMinutes = SelectedTag(FocusMinutesCombo), RestMinutes = SelectedTag(RestMinutesCombo), FocusRounds = SelectedTag(RoundsCombo),
            SleepProtectionEnabled = SleepEnabledCheck.IsChecked == true, SleepStart = start, SleepEnd = end, SleepDays = days,
            SleepWarningSeconds = 30, AllowDisplayPowerOff = _engine.Settings.AllowDisplayPowerOff,
            StartWithWindows = StartWithWindowsCheck.IsChecked == true, PeelSoundEnabled = PeelSoundCheck.IsChecked == true
        };
    }

    private static void AddDay(HashSet<DayOfWeek> days, System.Windows.Controls.CheckBox box, DayOfWeek day) { if (box.IsChecked == true) days.Add(day); }
    private static int SelectedTag(System.Windows.Controls.ComboBox box) => box.SelectedItem is System.Windows.Controls.ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out var value) ? value : throw new InvalidOperationException("请选择有效的时间或轮数。");
    private void RefreshSummaries(AppSettings settings)
    {
        HomeFocusSummary.Text = $"专注 {settings.FocusMinutes} 分钟 · 休息 {settings.RestMinutes} 分钟 · {settings.FocusRounds} 轮";
        HomeSleepSummary.Text = settings.SleepProtectionEnabled ? $"今晚 {settings.SleepStart:hh\\:mm} · {settings.SleepEnd:hh\\:mm} 自动解除" : "当前未启用";
    }

    private void RefreshAnalytics()
    {
        var data = _analytics.BuildLastSevenDays();
        MetricFocusText.Text = $"{data.FocusMinutes} 分钟"; MetricRoundsText.Text = $"{data.FocusRounds} 轮完成";
        MetricRestRateText.Text = $"{data.FullRestRate}%"; MetricRestCountText.Text = $"{data.FullRestCount} 次完整休息";
        MetricInterruptionsText.Text = $"{data.Interruptions} 次"; MetricSleepText.Text = $"{data.SleepNights} 晚"; MetricDelayText.Text = $"延迟 {data.SleepDelays} 次";
        DailyFocusList.ItemsSource = data.DailyFocus; InterruptionBucketList.ItemsSource = data.InterruptionBuckets; ActivityList.ItemsSource = data.ActivityRows;
        EmptyActivityText.Visibility = data.ActivityRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e) => await CheckForUpdatesAsync(false);
    private async void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || e.Source != MainTabs) return;
        if (MainTabs.SelectedIndex == 3) RefreshAnalytics();
        if (MainTabs.SelectedIndex == 4) await CheckForUpdatesAsync(false);
    }
    private async void CheckUpdate_Click(object sender, RoutedEventArgs e) => await CheckForUpdatesAsync(true);
    private async Task CheckForUpdatesAsync(bool force)
    {
        if (_updateCheckInProgress || (_hasCheckedForUpdates && !force)) return;
        _updateCheckInProgress = true; CheckUpdateButton.IsEnabled = false; UpdateStatusText.Text = "正在检查更新…"; UpdateDetailText.Text = "正在与 GitHub Releases 的最新公开版本比较。"; DownloadUpdateButton.Visibility = Visibility.Collapsed;
        try { var result = await _updateService.CheckLatestAsync(); _hasCheckedForUpdates = true; ApplyUpdateResult(result); }
        finally { _updateCheckInProgress = false; CheckUpdateButton.IsEnabled = true; }
    }

    private void ApplyUpdateResult(UpdateCheckResult result)
    {
        _latestReleaseUrl = null; UpdateStatusText.Foreground = (System.Windows.Media.Brush)FindResource("PrimaryBrush");
        switch (result.Status)
        {
            case UpdateCheckStatus.UpdateAvailable:
                _latestReleaseUrl = result.ReleaseUrl; UpdateStatusText.Foreground = (System.Windows.Media.Brush)FindResource("HotBrush"); UpdateStatusText.Text = $"发现新版本 v{result.LatestVersion}";
                UpdateDetailText.Text = result.PublishedAt is null ? $"{result.ReleaseName} 已发布。" : $"{result.ReleaseName} · {result.PublishedAt.Value.LocalDateTime:yyyy-MM-dd} 发布。";
                DownloadUpdateButton.Content = $"下载 v{result.LatestVersion}"; DownloadUpdateButton.Visibility = Visibility.Visible; break;
            case UpdateCheckStatus.Latest: UpdateStatusText.Text = "已是最新版本"; UpdateDetailText.Text = $"当前版本 v{result.CurrentVersion} 与最新公开 Release 一致。"; break;
            case UpdateCheckStatus.NotConfigured: UpdateStatusText.Text = "更新检查尚未启用"; UpdateDetailText.Text = result.Message ?? "尚未配置 GitHub 仓库地址。"; break;
            default: UpdateStatusText.Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush"); UpdateStatusText.Text = "暂时无法检查更新"; UpdateDetailText.Text = result.Message ?? "请稍后重试。"; break;
        }
    }

    private void DownloadUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (!Uri.TryCreate(_latestReleaseUrl, UriKind.Absolute, out var uri) || !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)) { MessageBox.Show("最新版下载地址无效。", "无法打开下载页"); return; }
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }
    private void OpenGithub_Click(object sender, RoutedEventArgs e) => Process.Start(new ProcessStartInfo(AppMetadata.EffectiveGitHubRepositoryUrl) { UseShellExecute = true });
    private void Window_Closing(object sender, CancelEventArgs e) { if (_allowClose) return; e.Cancel = true; Hide(); }
    public void AllowApplicationClose() { _allowClose = true; Close(); }
}

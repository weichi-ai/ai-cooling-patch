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
    private readonly UpdateService _updateService = new();
    private bool _allowClose;
    private bool _updateCheckInProgress;
    private bool _hasCheckedForUpdates;
    private string? _latestReleaseUrl;

    public MainWindow(ScheduleEngine engine, StateStore store)
    {
        InitializeComponent();
        _engine = engine;
        _store = store;
        _engine.StateChanged += Engine_StateChanged;
        LoadSettings(engine.Settings);
        RefreshCancellationRecords();
        CurrentVersionText.Text = $"当前版本 v{AppMetadata.CurrentVersionText}";
        ReleaseNotesList.ItemsSource = AppMetadata.ReleaseNotes;
        UpdateSnapshot(engine.CurrentSnapshot);
    }

    private void LoadSettings(AppSettings settings)
    {
        SelectByTag(FocusMinutesCombo, settings.FocusMinutes);
        SelectByTag(RestMinutesCombo, settings.RestMinutes);
        SelectByTag(RoundsCombo, settings.FocusRounds);
        SleepEnabledCheck.IsChecked = settings.SleepProtectionEnabled;
        SleepStartText.Text = settings.SleepStart.ToString(@"hh\:mm");
        SleepEndText.Text = settings.SleepEnd.ToString(@"hh\:mm");
        MondayCheck.IsChecked = settings.SleepDays.Contains(DayOfWeek.Monday);
        TuesdayCheck.IsChecked = settings.SleepDays.Contains(DayOfWeek.Tuesday);
        WednesdayCheck.IsChecked = settings.SleepDays.Contains(DayOfWeek.Wednesday);
        ThursdayCheck.IsChecked = settings.SleepDays.Contains(DayOfWeek.Thursday);
        FridayCheck.IsChecked = settings.SleepDays.Contains(DayOfWeek.Friday);
        SaturdayCheck.IsChecked = settings.SleepDays.Contains(DayOfWeek.Saturday);
        SundayCheck.IsChecked = settings.SleepDays.Contains(DayOfWeek.Sunday);
        StartWithWindowsCheck.IsChecked = StartupManager.IsEnabled;
        RefreshSummaries(settings);
    }

    private static void SelectByTag(System.Windows.Controls.ComboBox comboBox, int value)
    {
        foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
        {
            if (item.Tag?.ToString() == value.ToString())
            {
                comboBox.SelectedItem = item;
                return;
            }
        }
        comboBox.SelectedIndex = 0;
    }

    private void Engine_StateChanged(object? sender, RuntimeSnapshot snapshot) => Dispatcher.Invoke(() => UpdateSnapshot(snapshot));

    private void UpdateSnapshot(RuntimeSnapshot snapshot)
    {
        HeaderStatusText.Text = snapshot.StatusText;
        CurrentPlanTitle.Text = snapshot.StatusText;
        CurrentPlanSubtitle.Text = snapshot.Phase switch
        {
            LockPhase.Focus => "正在和 AI 认真搞事业。保持专注，别被 AI 带跑。",
            LockPhase.Rest => "有点上头，强制降温中。",
            LockPhase.SleepLock => "今日 AI 亲密额度已用完，明天再聊。",
            LockPhase.RestPreview or LockPhase.SleepPreview => "退烧贴正在进行 10 秒试贴。",
            _ => "今天准备和 AI 专注多久？"
        };
        CurrentCountdownText.Text = snapshot.Phase == LockPhase.Idle ? "--:--" : FormatRemaining(snapshot.Remaining);
        CancelPlanButton.Visibility = snapshot.Phase == LockPhase.Focus ? Visibility.Visible : Visibility.Collapsed;
        StatusDot.Fill = snapshot.Phase == LockPhase.Idle
            ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(150, 163, 158))
            : (System.Windows.Media.Brush)FindResource("PrimaryBrush");
    }

    private static string FormatRemaining(TimeSpan remaining) => remaining.TotalHours >= 1
        ? $"{(int)remaining.TotalHours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}"
        : $"{remaining.Minutes:00}:{remaining.Seconds:00}";

    private void StartPomodoro_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var focus = SelectedTag(FocusMinutesCombo);
            var rest = SelectedTag(RestMinutesCombo);
            var rounds = SelectedTag(RoundsCombo);
            var answer = MessageBox.Show(
                $"即将开始 {rounds} 轮退烧计划：每轮专注 {focus} 分钟，随后离屏休息 {rest} 分钟。\n\n专注期间可以正常使用电脑；休息期间退烧贴将覆盖屏幕，提醒你起身离开。",
                "开始退烧计划", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes)
                return;

            var settings = BuildSettingsFromControls();
            settings.FocusMinutes = focus;
            settings.RestMinutes = rest;
            settings.FocusRounds = rounds;
            _engine.UpdateSettings(settings);
            _engine.StartPomodoro(focus, rest, rounds);
            MainTabs.SelectedIndex = 0;
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "无法开始计划", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SaveSleepSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = BuildSettingsFromControls();
            _engine.UpdateSettings(settings);
            RefreshSummaries(settings);
            MessageBox.Show("夜间退烧计划已保存。", "AI退烧贴", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "无法保存计划", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CancelPlan_Click(object sender, RoutedEventArgs e)
    {
        if (_engine.CurrentSnapshot.Phase != LockPhase.Focus)
            return;

        var dialog = new CancelPlanWindow { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            _engine.CancelPomodoro(dialog.CancellationReason);
            RefreshCancellationRecords();
            MessageBox.Show("本次退烧计划已结束，原因已保存到上头记录。", "计划已结束", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "无法取消计划", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RefreshCancellationRecords()
    {
        var records = _store.LoadPlanEvents()
            .OrderByDescending(record => record.EventAt)
            .Select(record => new
            {
                Time = record.EventAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm"),
                Type = record.EventType == PlanEventType.PlanCancelled ? "提前结束计划" : "提前撕掉退烧贴",
                record.Reason,
                Detail = $"第 {record.CurrentRound}/{record.TotalRounds} 轮 · 剩余 {TimeSpan.FromSeconds(record.RemainingSeconds):mm\\:ss}"
            })
            .ToList();
        CancellationList.ItemsSource = records;
        CancellationList.Visibility = records.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        EmptyRecordsPanel.Visibility = records.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private AppSettings BuildSettingsFromControls()
    {
        if (!TimeSpan.TryParse(SleepStartText.Text.Trim(), out var sleepStart) ||
            !TimeSpan.TryParse(SleepEndText.Text.Trim(), out var sleepEnd))
            throw new ArgumentException("时间格式应为 HH:mm，例如 23:30。");

        var days = new HashSet<DayOfWeek>();
        AddDay(days, MondayCheck, DayOfWeek.Monday);
        AddDay(days, TuesdayCheck, DayOfWeek.Tuesday);
        AddDay(days, WednesdayCheck, DayOfWeek.Wednesday);
        AddDay(days, ThursdayCheck, DayOfWeek.Thursday);
        AddDay(days, FridayCheck, DayOfWeek.Friday);
        AddDay(days, SaturdayCheck, DayOfWeek.Saturday);
        AddDay(days, SundayCheck, DayOfWeek.Sunday);

        return new AppSettings
        {
            FocusMinutes = SelectedTag(FocusMinutesCombo),
            RestMinutes = SelectedTag(RestMinutesCombo),
            FocusRounds = SelectedTag(RoundsCombo),
            SleepProtectionEnabled = SleepEnabledCheck.IsChecked == true,
            SleepStart = sleepStart,
            SleepEnd = sleepEnd,
            SleepDays = days,
            SleepWarningSeconds = 30,
            AllowDisplayPowerOff = _engine.Settings.AllowDisplayPowerOff,
            StartWithWindows = _engine.Settings.StartWithWindows
        };
    }

    private static void AddDay(HashSet<DayOfWeek> days, System.Windows.Controls.CheckBox checkBox, DayOfWeek day)
    {
        if (checkBox.IsChecked == true)
            days.Add(day);
    }

    private static int SelectedTag(System.Windows.Controls.ComboBox comboBox)
    {
        if (comboBox.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out var value))
            return value;
        throw new InvalidOperationException("请选择有效的时间或轮数。");
    }

    private void RefreshSummaries(AppSettings settings)
    {
        HomeFocusSummary.Text = $"专注 {settings.FocusMinutes} 分钟 · 强制休息 {settings.RestMinutes} 分钟 · {settings.FocusRounds} 轮";
        HomeSleepSummary.Text = settings.SleepProtectionEnabled
            ? $"{settings.SleepStart:hh\\:mm} 开始 · {settings.SleepEnd:hh\\:mm} 解除"
            : "当前未启用";
    }

    private void PreviewRest_Click(object sender, RoutedEventArgs e)
    {
        try { _engine.StartRestPreview(TimeSpan.FromSeconds(10)); }
        catch (Exception exception) { MessageBox.Show(exception.Message, "无法预览", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void SaveStartupSetting_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            StartupManager.SetEnabled(StartWithWindowsCheck.IsChecked == true);
            _engine.Settings.StartWithWindows = StartWithWindowsCheck.IsChecked == true;
            _store.SaveSettings(_engine.Settings);
            MessageBox.Show("启动设置已保存。", "AI退烧贴", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "无法保存启动设置", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void PreviewSleep_Click(object sender, RoutedEventArgs e)
    {
        try { _engine.StartSleepPreview(TimeSpan.FromSeconds(10)); }
        catch (Exception exception) { MessageBox.Show(exception.Message, "无法预览", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void OpenFocusTab_Click(object sender, RoutedEventArgs e) => MainTabs.SelectedIndex = 1;
    private void OpenSleepTab_Click(object sender, RoutedEventArgs e) => MainTabs.SelectedIndex = 2;

    private async void Window_Loaded(object sender, RoutedEventArgs e) => await CheckForUpdatesAsync(false);

    private async void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source == MainTabs && AboutTab?.IsSelected == true)
            await CheckForUpdatesAsync(false);
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e) => await CheckForUpdatesAsync(true);

    private async Task CheckForUpdatesAsync(bool force)
    {
        if (_updateCheckInProgress || (_hasCheckedForUpdates && !force))
            return;

        _updateCheckInProgress = true;
        CheckUpdateButton.IsEnabled = false;
        UpdateStatusText.Text = "正在检查更新…";
        UpdateDetailText.Text = "正在与 GitHub Releases 的最新公开版本比较。";
        DownloadUpdateButton.Visibility = Visibility.Collapsed;

        try
        {
            var result = await _updateService.CheckLatestAsync();
            _hasCheckedForUpdates = true;
            ApplyUpdateResult(result);
        }
        finally
        {
            _updateCheckInProgress = false;
            CheckUpdateButton.IsEnabled = true;
        }
    }

    private void ApplyUpdateResult(UpdateCheckResult result)
    {
        _latestReleaseUrl = null;
        AboutTab.Header = "关于";
        UpdateStatusText.Foreground = (System.Windows.Media.Brush)FindResource("PrimaryBrush");

        switch (result.Status)
        {
            case UpdateCheckStatus.UpdateAvailable:
                _latestReleaseUrl = result.ReleaseUrl;
                AboutTab.Header = "关于 · 有更新";
                UpdateStatusText.Foreground = (System.Windows.Media.Brush)FindResource("HotBrush");
                UpdateStatusText.Text = $"发现新版本 v{result.LatestVersion}";
                UpdateDetailText.Text = result.PublishedAt is null
                    ? $"{result.ReleaseName} 已发布，可以前往 GitHub 下载最新版。"
                    : $"{result.ReleaseName} · {result.PublishedAt.Value.LocalDateTime:yyyy-MM-dd} 发布。";
                DownloadUpdateButton.Content = $"下载 v{result.LatestVersion}";
                DownloadUpdateButton.Visibility = Visibility.Visible;
                break;
            case UpdateCheckStatus.Latest:
                UpdateStatusText.Text = "已是最新版本";
                UpdateDetailText.Text = $"当前版本 v{result.CurrentVersion} 与 GitHub 最新公开 Release 一致。";
                break;
            case UpdateCheckStatus.NotConfigured:
                UpdateStatusText.Text = "更新检查尚未启用";
                UpdateDetailText.Text = result.Message ?? "尚未配置 GitHub 仓库地址。";
                break;
            default:
                UpdateStatusText.Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush");
                UpdateStatusText.Text = "暂时无法检查更新";
                UpdateDetailText.Text = result.Message ?? "请稍后重新检查。";
                break;
        }
    }

    private void DownloadUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (!Uri.TryCreate(_latestReleaseUrl, UriKind.Absolute, out var uri) ||
            !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show("最新版下载地址无效，请稍后重新检查。", "无法打开下载页", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        if (_allowClose)
            return;
        e.Cancel = true;
        Hide();
    }

    public void AllowApplicationClose()
    {
        _allowClose = true;
        Close();
    }
}

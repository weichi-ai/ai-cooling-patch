namespace LockPC.App.Core;

public sealed class AppSettings
{
    public int FocusMinutes { get; set; } = 45;
    public int RestMinutes { get; set; } = 10;
    public int FocusRounds { get; set; } = 4;
    public bool SleepProtectionEnabled { get; set; } = true;
    public TimeSpan SleepStart { get; set; } = new(23, 30, 0);
    public TimeSpan SleepEnd { get; set; } = new(6, 0, 0);
    public HashSet<DayOfWeek> SleepDays { get; set; } = Enum.GetValues<DayOfWeek>().ToHashSet();
    public int SleepWarningSeconds { get; set; } = 30;
    public bool AllowDisplayPowerOff { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public bool PeelSoundEnabled { get; set; } = true;
}
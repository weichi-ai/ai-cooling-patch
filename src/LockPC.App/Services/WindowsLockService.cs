using System.Runtime.InteropServices;

namespace LockPC.App.Services;

public static class WindowsLockService
{
    public static bool TryLockWorkstation()
    {
        try { return LockWorkStation(); }
        catch { return false; }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LockWorkStation();
}

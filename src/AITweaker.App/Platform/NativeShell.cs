using System.Runtime.InteropServices;

namespace AITweaker.App.Platform;

internal static partial class Native
{
    // ------------------------------------------------------------------ wallpaper & theme broadcasts

    private const uint SPI_GETDESKWALLPAPER = 0x0073;
    private const uint SPI_SETDESKWALLPAPER = 0x0014;
    private const uint SPIF_UPDATEINIFILE = 0x01;
    private const uint SPIF_SENDCHANGE = 0x02;
    private static readonly IntPtr HWND_BROADCAST = new(0xffff);
    private const uint WM_SETTINGCHANGE = 0x001A;
    private const uint SMTO_ABORTIFHUNG = 0x0002;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint param, string? vparam, uint winIni);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint param, [Out] char[] vparam, uint winIni);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, UIntPtr wParam, string lParam, uint flags, uint timeout, out UIntPtr result);

    public static string GetWallpaper()
    {
        var buf = new char[520];
        return SystemParametersInfo(SPI_GETDESKWALLPAPER, (uint)buf.Length, buf, 0) ? new string(buf).TrimEnd('\0') : "";
    }

    public static bool SetWallpaper(string path) => SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, path, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);

    /// <summary>Tells Explorer, DWM and apps that colors / theme / policies changed, so the new look shows without signing out.</summary>
    public static void BroadcastSettingChange(params string[] areas)
    {
        foreach (var area in areas)
            SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, UIntPtr.Zero, area, SMTO_ABORTIFHUNG, 1500, out _);
    }
}

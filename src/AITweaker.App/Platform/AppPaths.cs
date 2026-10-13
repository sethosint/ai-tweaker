namespace AITweaker.App.Platform;

public static class AppPaths
{
    /// <summary>Machine-wide data: backups, logs (C:\ProgramData\AITweaker).</summary>
    public static string Data { get; } = Ensure(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "AITweaker"));

    public static string Backups { get; } = Ensure(Path.Combine(Data, "backups"));
    public static string Logs { get; } = Ensure(Path.Combine(Data, "logs"));
    /// <summary>Helper tools the app downloads and verifies (PresentMon).</summary>
    public static string Tools { get; } = Ensure(Path.Combine(Data, "tools"));
    /// <summary>Measurement history and raw frame captures.</summary>
    public static string Bench { get; } = Ensure(Path.Combine(Data, "bench"));

    /// <summary>Per-user data: settings, WebView2 profile, downloads.</summary>
    public static string User { get; } = Ensure(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AITweaker"));

    public static string WebView { get; } = Ensure(Path.Combine(User, "WebView2"));
    public static string Downloads { get; } = Ensure(Path.Combine(User, "downloads"));

    public static string Desktop => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    private static string Ensure(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }
}

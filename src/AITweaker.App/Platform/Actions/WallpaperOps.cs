using System.Reflection;
using AITweaker.Core.Image;
using AITweaker.Core.Model;
using Microsoft.Win32;

namespace AITweaker.App.Platform.Actions;

/// <summary>AI Tweaker's own wallpapers and lock screen, extracted from the exe into %ProgramData%\AITweaker\Look.</summary>
public static class WallpaperOps
{
    private const string Desktop = @"HKCU\Control Panel\Desktop";
    private const string Csp = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\PersonalizationCSP";

    private static readonly string Windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    private static readonly string SystemRoot = Path.GetPathRoot(Windir) ?? @"C:\";

    /// <summary>Catalog paths are written for drive C:; maps them onto the drive Windows actually runs from.</summary>
    public static string Resolve(string path) =>
        path.StartsWith(@"C:\", StringComparison.OrdinalIgnoreCase) ? Path.Combine(SystemRoot, path[3..]) : path;

    /// <summary>Writes the wallpapers and aitweaker.theme under <paramref name="root"/>: the live system drive or a mounted image.</summary>
    public static void ExtractAssets(string root)
    {
        string Map(string p) => Path.Combine(root, p[3..]);
        var asm = Assembly.GetExecutingAssembly();
        foreach (var v in LookAssets.Variants)
        {
            var target = Map(LookAssets.WallpaperFile(v));
            using var src = asm.GetManifestResourceStream($"AITweaker.Wallpapers.aitweaker-{v}.jpg")
                ?? throw new InvalidOperationException($"wallpaper '{v}' is missing from the build");
            if (File.Exists(target) && new FileInfo(target).Length == src.Length) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var dst = File.Create(target);
            src.CopyTo(dst);
        }
        var theme = Map(LookAssets.ThemePath);
        File.WriteAllText(theme, LookAssets.ThemeFile());
    }

    /// <summary>Lists "AI Tweaker" in Settings → Personalization → Themes (best effort: the folder may be TrustedInstaller-only).</summary>
    private static void RegisterTheme()
    {
        try
        {
            File.WriteAllText(Path.Combine(Windir, "Resources", "Themes", "aitweaker.theme"), LookAssets.ThemeFile());
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            AppLog.Info("look", "theme not added to the Themes list: " + ex.Message);
        }
    }

    public static Task ApplyAsync(WallpaperAction a, ExecContext ctx)
    {
        ExtractAssets(SystemRoot);
        RegisterTheme();
        var file = Resolve(a.File);
        if (a.Target == "lock")
        {
            RegistryOps.SetValue(Csp, "LockScreenImagePath", file, RegistryValueKind.String, ctx);
            RegistryOps.SetValue(Csp, "LockScreenImageUrl", file, RegistryValueKind.String, ctx);
            RegistryOps.SetValue(Csp, "LockScreenImageStatus", 1, RegistryValueKind.DWord, ctx);
            return Task.CompletedTask;
        }

        ctx.Record(ChangeRecord.Of("wallpaper", ("path", Native.GetWallpaper())));
        RegistryOps.SetValue(Desktop, "WallpaperStyle", "10", RegistryValueKind.String, ctx); // fill
        RegistryOps.SetValue(Desktop, "TileWallpaper", "0", RegistryValueKind.String, ctx);
        if (!Native.SetWallpaper(file)) throw new InvalidOperationException("Windows refused to set the wallpaper");
        ctx.Log("wallpaper -> " + file);
        return Task.CompletedTask;
    }

    public static Task RevertAsync(ChangeRecord r)
    {
        var old = r["path"];
        Native.SetWallpaper(string.IsNullOrEmpty(old) || !File.Exists(old) ? Path.Combine(Windir, @"Web\Wallpaper\Windows\img0.jpg") : old);
        return Task.CompletedTask;
    }

    public static bool? Check(WallpaperAction a)
    {
        var file = Resolve(a.File);
        var current = a.Target == "lock" ? RegistryHelper.Read(Csp, "LockScreenImagePath").Data : Native.GetWallpaper();
        return string.Equals(current, file, StringComparison.OrdinalIgnoreCase);
    }
}

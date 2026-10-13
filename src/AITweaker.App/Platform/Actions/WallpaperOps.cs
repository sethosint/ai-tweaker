using System.Reflection;
using AITweaker.Core.Image;
using AITweaker.Core.Model;
using Microsoft.Win32;

namespace AITweaker.App.Platform.Actions;

/// <summary>AI Tweaker's own wallpapers and lock screen, extracted from the exe into %ProgramData%\AITweaker\Look.</summary>
public static class WallpaperOps
{
    private const string Desktop = @"HKCU\Control Panel\Desktop";
    private const string Colors = @"HKCU\Control Panel\Colors";
    private const string Wallpapers = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Wallpapers";
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
        if (a.Target == "lock")
        {
            var lockFile = Resolve(a.File);
            RegistryOps.SetValue(Csp, "LockScreenImagePath", lockFile, RegistryValueKind.String, ctx);
            RegistryOps.SetValue(Csp, "LockScreenImageUrl", lockFile, RegistryValueKind.String, ctx);
            RegistryOps.SetValue(Csp, "LockScreenImageStatus", 1, RegistryValueKind.DWord, ctx);
            return Task.CompletedTask;
        }

        // Recorded first, so on rollback it runs last — after the colour and style values are restored.
        ctx.Record(ChangeRecord.Of("wallpaper", ("path", Native.GetWallpaper())));
        RegistryOps.SetValue(Desktop, "WallpaperStyle", "10", RegistryValueKind.String, ctx); // fill
        RegistryOps.SetValue(Desktop, "TileWallpaper", "0", RegistryValueKind.String, ctx);
        RegistryOps.SetValue(Wallpapers, "BackgroundType", a.SolidBlack ? 1 : 0, RegistryValueKind.DWord, ctx); // 1 = solid colour, 0 = picture (no Spotlight)
        if (a.SolidBlack)
        {
            RegistryOps.SetValue(Colors, "Background", LookAssets.BlackRgb, RegistryValueKind.String, ctx);
            Native.SetDesktopColor(0, 0, 0);
            if (!Native.SetWallpaper("")) throw new InvalidOperationException("Windows refused to remove the wallpaper");
            ctx.Log("desktop -> solid black");
            return Task.CompletedTask;
        }
        var file = Resolve(a.File);
        if (!Native.SetWallpaper(file)) throw new InvalidOperationException("Windows refused to set the wallpaper");
        ctx.Log("wallpaper -> " + file);
        return Task.CompletedTask;
    }

    public static Task RevertAsync(ChangeRecord r)
    {
        // The registry values (colour, style, background type) are already restored at this point.
        if (RegistryHelper.Read(Colors, "Background").Data?.Split(' ') is [var rr, var gg, var bb]
            && byte.TryParse(rr, out var cr) && byte.TryParse(gg, out var cg) && byte.TryParse(bb, out var cb))
            Native.SetDesktopColor(cr, cg, cb);
        var old = r["path"] ?? "";
        // An empty path means the desktop was a solid colour before.
        Native.SetWallpaper(old.Length == 0 ? "" : File.Exists(old) ? old : Path.Combine(Windir, @"Web\Wallpaper\Windows\img0.jpg"));
        return Task.CompletedTask;
    }

    public static bool? Check(WallpaperAction a)
    {
        if (a.SolidBlack)
            return Native.GetWallpaper().Length == 0 && RegistryHelper.Read(Colors, "Background").Data?.Trim() == LookAssets.BlackRgb;
        var file = Resolve(a.File);
        var current = a.Target == "lock" ? RegistryHelper.Read(Csp, "LockScreenImagePath").Data : Native.GetWallpaper();
        return string.Equals(current, file, StringComparison.OrdinalIgnoreCase);
    }
}

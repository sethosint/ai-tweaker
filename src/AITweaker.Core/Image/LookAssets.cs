using System.Globalization;
using System.Text;

namespace AITweaker.Core.Image;

/// <summary>
/// AI Tweaker's own Windows look: wallpapers drawn for this project (tools/wallpapers), the signature amber accent
/// and a .theme file so the look also shows up in Settings → Personalization → Themes.
/// </summary>
public static class LookAssets
{
    /// <summary>Under ProgramData: always writable for administrators (also inside a mounted image), readable by every account and LogonUI.</summary>
    public const string Folder = @"C:\ProgramData\AITweaker\Look";
    public const string ThemePath = Folder + @"\aitweaker.theme";
    /// <summary>Same file as <see cref="ThemePath"/>, written the way registry REG_EXPAND_SZ values expect it.</summary>
    public const string ThemePathExpand = @"%ProgramData%\AITweaker\Look\aitweaker.theme";
    public static readonly string[] Variants = ["dark", "light", "lock"];

    public static string WallpaperFile(string variant) => $@"{Folder}\aitweaker-{(Variants.Contains(variant) ? variant : "dark")}.jpg";

    /// <summary>Signature accent "signal amber" and the shades Windows uses for controls, Start and the taskbar.</summary>
    public static readonly (byte R, byte G, byte B)[] Palette =
    [
        (0xFB, 0xDD, 0xA6), // light 3
        (0xF7, 0xC4, 0x6E), // light 2
        (0xF2, 0xA9, 0x3B), // light 1 — the app's night accent, used by Windows dark mode
        (0xE0, 0x7A, 0x12), // accent
        (0xB8, 0x5F, 0x0A), // dark 1
        (0x8A, 0x45, 0x07), // dark 2
        (0x5C, 0x2E, 0x05), // dark 3
        (0x3B, 0x8F, 0xD9), // complementary
    ];

    public static (byte R, byte G, byte B) Accent => Palette[3];

    /// <summary>REG_BINARY AccentPalette: 8 × RGBA.</summary>
    public static byte[] AccentPalette() => Palette.SelectMany(c => new[] { c.R, c.G, c.B, (byte)0 }).ToArray();

    /// <summary>0xAABBGGRR as Explorer\Accent and DWM AccentColor store it.</summary>
    public static uint Abgr((byte R, byte G, byte B) c) => 0xFF000000u | (uint)c.B << 16 | (uint)c.G << 8 | c.R;

    /// <summary>0xAARRGGBB with DWM's default colorization alpha.</summary>
    public static uint Argb((byte R, byte G, byte B) c, byte alpha = 0xC4) => (uint)alpha << 24 | (uint)c.R << 16 | (uint)c.G << 8 | c.B;

    public static string ThemeFile()
    {
        var a = Accent;
        var sb = new StringBuilder();
        sb.AppendLine("; AI Tweaker — own Windows look (dark, signal amber accent, AI Tweaker wallpaper)");
        sb.AppendLine("[Theme]");
        sb.AppendLine("DisplayName=AI Tweaker");
        sb.AppendLine();
        sb.AppendLine(@"[Control Panel\Desktop]");
        sb.AppendLine($"Wallpaper={WallpaperFile("dark")}");
        sb.AppendLine("WallpaperStyle=10");
        sb.AppendLine("TileWallpaper=0");
        sb.AppendLine("Pattern=");
        sb.AppendLine();
        sb.AppendLine("[VisualStyles]");
        sb.AppendLine(@"Path=%SystemRoot%\resources\themes\Aero\Aero.msstyles");
        sb.AppendLine("ColorStyle=NormalColor");
        sb.AppendLine("Size=NormalSize");
        sb.AppendLine("AutoColorization=0");
        sb.AppendLine("ColorizationColor=0X" + Argb(a).ToString("X8", CultureInfo.InvariantCulture));
        sb.AppendLine("SystemMode=Dark");
        sb.AppendLine("AppMode=Dark");
        sb.AppendLine();
        sb.AppendLine("[MasterThemeSelector]");
        sb.AppendLine("MTSM=RJSPBS");
        return sb.ToString();
    }
}

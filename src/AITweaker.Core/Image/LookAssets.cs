using System.Globalization;
using System.Text;

namespace AITweaker.Core.Image;

/// <summary>
/// AI Tweaker's own Windows look: black and monochrome — black taskbar and Start, solid black desktop, dark mode,
/// with minimal dark wallpapers made for this project (tools/wallpapers, no text or logos) as an option and a .theme
/// file so the look also shows up in Settings → Personalization → Themes.
/// </summary>
public static class LookAssets
{
    /// <summary>Under ProgramData: always writable for administrators (also inside a mounted image), readable by every account and LogonUI.</summary>
    public const string Folder = @"C:\ProgramData\AITweaker\Look";
    public const string ThemePath = Folder + @"\aitweaker.theme";
    /// <summary>Same file as <see cref="ThemePath"/>, written the way registry REG_EXPAND_SZ values expect it.</summary>
    public const string ThemePathExpand = @"%ProgramData%\AITweaker\Look\aitweaker.theme";

    /// <summary>Wallpaper image files shipped inside the exe. "black" is not a file: it is a solid colour desktop.</summary>
    public static readonly string[] Variants = ["graphite", "obsidian", "lock"];
    public const string Black = "black";

    /// <summary>"Background" colour value (HKCU\Control Panel\Colors) for the solid black desktop.</summary>
    public const string BlackRgb = "0 0 0";

    public static string WallpaperFile(string variant) => $@"{Folder}\aitweaker-{(Variants.Contains(variant) ? variant : "graphite")}.jpg";

    /// <summary>
    /// Monochrome accent palette. The dark shades are black, so with "accent colour on Start and taskbar" both turn black;
    /// the light shades stay grey so toggles, links and selection remain visible in dark mode.
    /// </summary>
    public static readonly (byte R, byte G, byte B)[] Palette =
    [
        (0xE0, 0xE0, 0xE0), // light 3
        (0xBD, 0xBD, 0xBD), // light 2 — toggles and links in dark mode
        (0x9A, 0x9A, 0x9A), // light 1
        (0x1A, 0x1A, 0x1A), // accent
        (0x00, 0x00, 0x00), // dark 1
        (0x00, 0x00, 0x00), // dark 2 — Start and taskbar
        (0x00, 0x00, 0x00), // dark 3
        (0x6E, 0x6E, 0x6E), // complementary
    ];

    public static (byte R, byte G, byte B) Accent => Palette[3];
    public static (byte R, byte G, byte B) StartColor => Palette[5];

    /// <summary>REG_BINARY AccentPalette: 8 × RGBA.</summary>
    public static byte[] AccentPalette() => Palette.SelectMany(c => new[] { c.R, c.G, c.B, (byte)0 }).ToArray();

    /// <summary>0xAABBGGRR as Explorer\Accent and DWM AccentColor store it.</summary>
    public static uint Abgr((byte R, byte G, byte B) c) => 0xFF000000u | (uint)c.B << 16 | (uint)c.G << 8 | c.R;

    /// <summary>0xAARRGGBB with DWM's default colorization alpha.</summary>
    public static uint Argb((byte R, byte G, byte B) c, byte alpha = 0xC4) => (uint)alpha << 24 | (uint)c.R << 16 | (uint)c.G << 8 | c.B;

    public static string ThemeFile()
    {
        var sb = new StringBuilder();
        sb.AppendLine("; AI Tweaker — own Windows look (dark mode, black taskbar and Start, solid black desktop)");
        sb.AppendLine("[Theme]");
        sb.AppendLine("DisplayName=AI Tweaker");
        sb.AppendLine();
        sb.AppendLine(@"[Control Panel\Desktop]");
        sb.AppendLine("Wallpaper=");
        sb.AppendLine("WallpaperStyle=10");
        sb.AppendLine("TileWallpaper=0");
        sb.AppendLine("Pattern=");
        sb.AppendLine();
        sb.AppendLine(@"[Control Panel\Colors]");
        sb.AppendLine("Background=" + BlackRgb);
        sb.AppendLine();
        sb.AppendLine("[VisualStyles]");
        sb.AppendLine(@"Path=%SystemRoot%\resources\themes\Aero\Aero.msstyles");
        sb.AppendLine("ColorStyle=NormalColor");
        sb.AppendLine("Size=NormalSize");
        sb.AppendLine("AutoColorization=0");
        sb.AppendLine("ColorizationColor=0X" + Argb(Accent).ToString("X8", CultureInfo.InvariantCulture));
        sb.AppendLine("SystemMode=Dark");
        sb.AppendLine("AppMode=Dark");
        sb.AppendLine();
        sb.AppendLine("[MasterThemeSelector]");
        sb.AppendLine("MTSM=RJSPBS");
        return sb.ToString();
    }
}

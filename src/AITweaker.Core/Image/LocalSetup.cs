using System.Globalization;
using System.Text.RegularExpressions;

namespace AITweaker.Core.Image;

/// <summary>
/// Windows Setup started from the running system (no USB stick), with the three documented
/// <c>/auto</c> modes: <c>upgrade</c> keeps apps and files (a repair install), <c>dataonly</c> keeps personal files,
/// <c>clean</c> keeps nothing.
/// </summary>
public static partial class LocalSetup
{
    public static readonly string[] Modes = ["clean", "dataonly", "upgrade"];

    /// <summary>
    /// Setup runs by itself and restarts the PC: licence accepted, compatibility warnings that can be dismissed are
    /// dismissed, no Dynamic Update download (the image is already current) and no setup telemetry.
    /// </summary>
    public static string Arguments(string mode)
    {
        if (!Modes.Contains(mode)) throw new ArgumentException("Unknown setup mode: " + mode, nameof(mode));
        return $"/auto {mode} /eula accept /dynamicupdate disable /compat ignorewarning /telemetry disable";
    }

    /// <summary>The build number from <c>dism /Get-WimInfo /Index:1</c> output ("Version : 10.0.26100" → 26100).</summary>
    public static int? ImageBuild(string dismOutput)
    {
        var m = VersionRx().Match(dismOutput);
        return m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : null;
    }

    [GeneratedRegex(@"Version\s*:\s*10\.0\.(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex VersionRx();

    /// <summary>
    /// Setup refuses to run an older Windows from inside a newer one, so that has to be caught first.
    /// Returns null when the image can be used.
    /// </summary>
    public static string? Problem(int currentBuild, int? imageBuild)
    {
        if (imageBuild is null) return "unknown-image";
        if (imageBuild < 22000) return "not-windows11";
        if (imageBuild < currentBuild) return "older-image";
        return null;
    }
}

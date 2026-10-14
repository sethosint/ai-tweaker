using System.Diagnostics;
using AITweaker.Core.Image;
using AITweaker.Core.Profile;

namespace AITweaker.App.Platform;

public sealed record ReinstallStarted(string Setup, string Arguments, int ImageBuild);

/// <summary>
/// Reinstalls Windows from the running system, without a USB stick: takes the AI Tweaker image or an official ISO
/// (downloaded automatically when none is chosen), checks that it isn't older than the installed Windows, mounts it
/// and starts Windows Setup, which prepares everything and restarts the PC by itself.
/// </summary>
public static class LocalReinstall
{
    public static async Task<ReinstallStarted> StartAsync(SystemProfile profile, string? source, string mode, string language, Action<string, double> progress, CancellationToken ct = default)
    {
        var args = LocalSetup.Arguments(mode);
        // Setup keeps files and apps only when the image is in the installed Windows' language
        if (string.IsNullOrWhiteSpace(source) && mode != "clean" && language.StartsWith("English", StringComparison.OrdinalIgnoreCase)
            && !profile.Os.Language.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Files and apps can only be kept with an image in the system language ({profile.Os.Language}). Choose a clean install or your own ISO in that language.");
        // Setup keeps files and apps only within the same edition: our image holds one (Pro or Home)
        if (mode != "clean" && source is not null && source.Equals(ImageBuilder.Existing(), StringComparison.OrdinalIgnoreCase)
            && ImageBuilder.BuiltEdition() is { } built && !profile.Os.Edition.StartsWith(built == "Core" ? "Core" : "Professional", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"The AI Tweaker image holds Windows 11 {(built == "Core" ? "Home" : "Pro")}, but this PC runs {profile.Os.Edition}: files and apps can only be kept within the same edition. Rebuild the image with \"Same as this PC\" or choose a clean install.");
        if (string.IsNullOrWhiteSpace(source)) source = await UsbCreator.DownloadIsoAsync(language, progress, ct);

        progress("mount", 0);
        var root = source.EndsWith(".iso", StringComparison.OrdinalIgnoreCase) ? await MountAsync(source, ct) : source;
        var setup = Path.Combine(root, "setup.exe");
        if (!File.Exists(setup)) throw new FileNotFoundException("setup.exe not found in the image", setup);

        progress("check", 0);
        var wim = new[] { "install.wim", "install.esd" }.Select(f => Path.Combine(root, "sources", f)).FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException("sources\\install.wim not found in the image");
        var info = await ProcessRunner.RunAsync("dism.exe", $"/English /Get-WimInfo /WimFile:\"{wim}\" /Index:1", TimeSpan.FromMinutes(2), ct: ct);
        var build = LocalSetup.ImageBuild(info.Output);
        switch (LocalSetup.Problem(profile.Os.Build, build))
        {
            case "older-image": throw new InvalidOperationException($"The image (build {build}) is older than the installed Windows (build {profile.Os.Build}); Setup can't go back from inside Windows. Download the latest image or install from a USB stick.");
            case "not-windows11": throw new InvalidOperationException($"The image is not Windows 11 (build {build}).");
            case "unknown-image": throw new InvalidOperationException("Can't read the Windows version of the image: " + info.Output.Trim());
        }

        // Microsoft's documented switch for PCs without TPM 2.0 or with an older CPU (TPM 1.2 is still needed)
        if (!profile.Security.Tpm)
            RegistryHelper.Write(@"HKLM\SYSTEM\Setup\MoSetup", "AllowUpgradesWithUnsupportedTPMOrCPU", 1, Microsoft.Win32.RegistryValueKind.DWord);

        progress("setup", 0);
        AppLog.Warn("reinstall", $"starting Windows Setup ({mode}) from {root}: {args}");
        Process.Start(new ProcessStartInfo(setup, args) { UseShellExecute = true, WorkingDirectory = root });
        return new ReinstallStarted(setup, args, build!.Value);
    }

    /// <summary>Mounts the ISO (or finds it already mounted) and returns its drive root.</summary>
    private static async Task<string> MountAsync(string iso, CancellationToken ct)
    {
        var script = $"""
            $ErrorActionPreference = 'Stop'
            $img = Get-DiskImage -ImagePath {ProcessRunner.PsQuote(iso)}
            if (-not $img.Attached) {"{"} $img = Mount-DiskImage -ImagePath {ProcessRunner.PsQuote(iso)} -StorageType ISO -PassThru {"}"}
            ($img | Get-Volume).DriveLetter
            """;
        var r = await ProcessRunner.PowerShellAsync(script, TimeSpan.FromMinutes(2), ct: ct);
        var letter = r.Output.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length == 1 && char.IsAsciiLetter(l[0]));
        if (letter is null) throw new InvalidOperationException("Could not mount the ISO: " + (r.Error + r.Output).Trim());
        return letter + @":\";
    }
}

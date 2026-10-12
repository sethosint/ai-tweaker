using System.IO.Compression;
using System.Reflection;
using AITweaker.Core.Catalog;
using AITweaker.Core.Image;

namespace AITweaker.App.Platform;

/// <summary>
/// Saves the AI Tweaker playbook for AME Wizard (<c>AI Tweaker.apbx</c>) to the desktop. With 7-Zip installed it is
/// packed the standard way (zip, password <see cref="AmePlaybook.Password"/>); otherwise as a plain zip.
/// </summary>
public static class AmeExport
{
    public sealed record Result(string Path, bool Protected, int Options, int Files);

    public static async Task<Result> ExportAsync(TweakCatalog catalog, string lang, CancellationToken ct = default)
    {
        var playbook = AmePlaybook.Build(catalog, lang);
        var stage = Path.Combine(AppPaths.Downloads, "ame-playbook");
        if (Directory.Exists(stage)) Directory.Delete(stage, recursive: true);

        foreach (var (path, text) in playbook.Files)
            File.WriteAllText(Created(Path.Combine(stage, path)), text, AmePlaybook.EncodingFor(path));
        var asm = Assembly.GetExecutingAssembly();
        foreach (var name in playbook.Wallpapers)
            Write(Path.Combine(stage, "Executables", "Look", name), s => Copy(asm, "AITweaker.Wallpapers." + name, s));
        Write(Path.Combine(stage, "playbook.png"), s => Copy(asm, "AITweaker.Playbook.png", s));

        var target = Path.Combine(AppPaths.Desktop, "AI Tweaker.apbx");
        if (File.Exists(target)) File.Delete(target);
        bool locked = false;
        if (SevenZip() is { } sevenZip)
        {
            // "<stage>\*" stores the contents at the archive root, where AME looks for playbook.conf
            var r = await ProcessRunner.RunAsync(sevenZip, $"a -tzip -mx1 -y -p{AmePlaybook.Password} \"{target}\" \"{Path.Combine(stage, "*")}\"", TimeSpan.FromMinutes(2), ct: ct);
            locked = r.Success && File.Exists(target);
            if (!locked) AppLog.Warn("ame", $"7-Zip failed ({r.ExitCode}): {r.Error.Trim()}; saving a plain zip");
        }
        if (!locked)
        {
            if (File.Exists(target)) File.Delete(target);
            ZipFile.CreateFromDirectory(stage, target, CompressionLevel.Optimal, includeBaseDirectory: false);
        }
        AppLog.Ok("ame", $"playbook saved: {target} ({playbook.Files.Count + playbook.Wallpapers.Count + 1} files, {playbook.Options.Count} options)");
        return new Result(target, locked, playbook.Options.Count, playbook.Files.Count + playbook.Wallpapers.Count + 1);
    }

    private static string? SevenZip() => new[]
    {
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
    }.Select(d => Path.Combine(d, "7-Zip", "7z.exe")).FirstOrDefault(File.Exists);

    private static string Created(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }

    private static void Write(string path, Action<Stream> write)
    {
        using var s = File.Create(Created(path));
        write(s);
    }

    private static void Copy(Assembly asm, string resource, Stream to)
    {
        using var src = asm.GetManifestResourceStream(resource) ?? throw new InvalidOperationException($"'{resource}' is missing from the build");
        src.CopyTo(to);
    }
}

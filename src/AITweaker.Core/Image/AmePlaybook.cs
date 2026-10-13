using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using AITweaker.Core.Catalog;
using AITweaker.Core.Conditions;
using AITweaker.Core.Model;

namespace AITweaker.Core.Image;

/// <summary>
/// AI Tweaker as a playbook for AME Wizard: the AI Tweaker look, the Open-Shell Start menu and the
/// "turn off what you don't use" switches, generated from the same catalog the app applies.
/// An .apbx is these files in a 7z archive with the password <see cref="Password"/>.
/// </summary>
/// <remarks>
/// Semantics follow AME's open-source engine (Ameliorated-LLC/trusted-uninstaller-cli): HKCU values go to every
/// user hive plus the default profile once it is loaded as <c>AME_UserHive_Default</c>; REG_DWORD data is unsigned
/// decimal, REG_BINARY hex, REG_MULTI_SZ joined with a literal <c>\0</c>; <c>!service</c> without an operation
/// deletes the service, so every service action names one; PowerShell commands are passed inside
/// <c>-C "…"</c>, so scripts live in Executables and commands never contain double quotes.
/// </remarks>
public sealed class AmePlaybook
{
    public const string Password = "malte";
    public const string Version = "1.1.0";
    public static readonly Guid UniqueId = new("17b2d243-a6da-43bd-914a-6d3908f8785e");
    public const string OpenShellOption = "start-openshell";
    private const string KeepStartOption = "start-windows";
    private const string DefaultHive = @"HKU\AME_UserHive_Default";

    /// <summary>Facts true on any machine the playbook runs on (Windows 11).</summary>
    private static readonly Dictionary<string, object?> Facts = new() { ["os.build"] = 22631, ["os.win11"] = true };

    /// <summary>Text files by path inside the playbook (backslashes, as AME expects).</summary>
    public SortedDictionary<string, string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Wallpaper images (file names in <see cref="LookAssets.Folder"/>) to put into Executables\Look.</summary>
    public SortedSet<string> Wallpapers { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<SkippedTweak> Skipped { get; } = [];
    /// <summary>Option names in the order the pages show them.</summary>
    public List<string> Options { get; } = [];

    private readonly string _lang;
    private readonly List<string> _scripts = [];

    private AmePlaybook(string lang) => _lang = lang == "en" ? "en" : "ru";

    private string T(string ru, string en) => _lang == "en" ? en : ru;
    private string T(LocalizedText? text) => text is null ? "" : _lang == "en" ? text.En : text.Ru;

    /// <summary>Encoding to write a playbook file with: scripts need a BOM for Windows PowerShell 5.1.</summary>
    public static Encoding EncodingFor(string path) =>
        path.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase) ? new UTF8Encoding(true) : new UTF8Encoding(false);

    public static AmePlaybook Build(TweakCatalog catalog, string lang = "ru")
    {
        var p = new AmePlaybook(lang);
        var look = catalog.Tweaks.Where(t => t.Category == "look" && t.Level >= 1).ToList();
        var openShell = look.FirstOrDefault(t => t.Id == "look.openshell");
        look.Remove(openShell!);

        var root = new List<string>
        {
            // AI Tweaker keeps no backups for changes made here, so Windows System Restore is the way back
            Status(p.T("Точка восстановления", "Restore point")),
            Ps(@"& .\Look\Restore-Point.ps1", exeDir: true, weight: 10),
            // HKCU actions reach new accounts only through the loaded default profile
            Ps($@"reg load {DefaultHive} $env:SystemDrive\Users\Default\NTUSER.DAT", ignoreErrors: true),
            Status(p.T("Внешний вид AI Tweaker", "The AI Tweaker look")),
            Task("look.yml"),
        };
        p.Files[@"Configuration\look.yml"] = p.TaskFile(p.T("Внешний вид", "Look"),
            p.T("Тёмная тема, чёрные панель задач и «Пуск», чёрный рабочий стол, спокойный экран блокировки",
                "Dark mode, black taskbar and Start, black desktop, a quiet lock screen"),
            look.SelectMany(p.Tweak).Concat(
            [
                // new accounts start with the AI Tweaker theme, as in the AI Tweaker image
                Reg(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes", "InstallTheme", RegistryValueKind2.Expand, LookAssets.ThemePathExpand),
                Ps(@"& .\Look\Install-Look.ps1", exeDir: true, weight: 5),
            ]));

        if (openShell is not null)
        {
            p.Options.Add(OpenShellOption);
            root.Add(Task("openshell.yml", OpenShellOption));
            p.Files[@"Configuration\openshell.yml"] = p.TaskFile("Open-Shell",
                p.T("Устанавливает только меню «Пуск» Open-Shell и настраивает его в стиле Windows 7",
                    "Installs only the Open-Shell Start menu and sets it up in the Windows 7 style"),
                new[]
                {
                    Status(p.T("Установка Open-Shell", "Installing Open-Shell")),
                    Ps(@"& .\OpenShell\Install-OpenShell.ps1", exeDir: true, runas: "currentUserElevated", weight: 20),
                }.Concat(p.Tweak(openShell)));
        }

        foreach (var f in catalog.Features)
        {
            var tweaks = f.Tweaks.Select(catalog.Find).OfType<TweakDefinition>().ToList();
            var actions = tweaks.SelectMany(p.Tweak).ToList();
            if (actions.Count == 0) continue;
            var option = "off-" + f.Id;
            p.Options.Add(option);
            root.Add(Task($@"features\{f.Id}.yml", option));
            p.Files[$@"Configuration\features\{f.Id}.yml"] = p.TaskFile(p.T(f.Name), string.Join("; ", tweaks.Select(t => p.T(t.Name))), actions);
        }

        root.Add(Status(p.T("Обновление рабочего стола", "Refreshing the desktop")));
        root.Add(Ps(@"& .\Look\Refresh-Desktop.ps1", exeDir: true, runas: "currentUser"));
        root.Add(Ps($"reg unload {DefaultHive}", ignoreErrors: true));
        p.Files[@"Configuration\custom.yml"] = p.TaskFile("AI Tweaker", p.T("Запускает выбранные части плейбука", "Runs the chosen parts of the playbook"), root);

        p.Files[@"Executables\Look\aitweaker.theme"] = LookAssets.ThemeFile();
        p.Files[@"Executables\Look\Install-Look.ps1"] = InstallLookScript;
        p.Files[@"Executables\Look\Restore-Point.ps1"] = RestorePointScript;
        p.Files[@"Executables\Look\Refresh-Desktop.ps1"] = p.RefreshScript(look);
        if (openShell is not null) p.Files[@"Executables\OpenShell\Install-OpenShell.ps1"] = InstallOpenShellScript;
        for (int i = 0; i < p._scripts.Count; i++) p.Files[$@"Executables\Tweaks\{i + 1:00}.ps1"] = p._scripts[i];
        p.Files["playbook.conf"] = p.Conf(catalog);
        return p;
    }

    // ------------------------------------------------------------------ catalog → AME actions

    private IEnumerable<string> Tweak(TweakDefinition t)
    {
        var lines = new List<string> { Status(T(t.Name)) };
        foreach (var a in t.Actions)
        {
            if (a.When is { } when && !(ConditionEvaluator.Identifiers(when).All(Facts.ContainsKey) && ConditionEvaluator.Evaluate(when, Facts)))
            {
                Skipped.Add(new SkippedTweak(t.Id, "condition: " + when));
                continue;
            }
            switch (a)
            {
                case RegistryAction r when r.Path.Contains('*'):
                    Skipped.Add(new SkippedTweak(t.Id, "per-device: " + r.Path));
                    break;
                case RegistryAction r:
                    lines.Add(r.Delete
                        ? Reg(r.Path, r.Name, r.Kind, null)
                        : Reg(r.Path, r.Name, r.Kind, Data(r)));
                    break;
                case WallpaperAction w:
                    foreach (var op in OfflineImagePlan.Wallpaper(w, t.Id))
                        lines.Add(Reg(LivePath(op.Hive, op.SubKey), op.Name, op.Kind, op.Data));
                    if (!w.SolidBlack) Wallpapers.Add(FileName(w.File));
                    break;
                case ServiceAction s:
                    if (s.Stop && s.Startup is ServiceStartup.Disabled or ServiceStartup.Manual)
                        lines.Add($"  - !service: {{name: {Q(s.Name)}, operation: stop, ignoreErrors: true}}");
                    lines.Add($"  - !service: {{name: {Q(s.Name)}, operation: change, startup: {Start(s.Startup)}, ignoreErrors: true}}");
                    if (s.Startup == ServiceStartup.Delayed)
                        lines.Add(Reg($@"HKLM\SYSTEM\CurrentControlSet\Services\{s.Name}", "DelayedAutostart", RegistryValueKind2.Dword, "1"));
                    break;
                case BcdAction b:
                    lines.Add($"  - !run: {{exe: 'bcdedit.exe', args: {Q(b.Value is null ? $"/deletevalue {b.Element}" : $"/set {b.Element} {b.Value}")}, ignoreErrors: true}}");
                    break;
                case PowerShellAction ps:
                    var script = ps.Script.TrimEnd() + "\n";
                    if (!_scripts.Contains(script)) _scripts.Add(script);
                    lines.Add(Ps($@"& .\Tweaks\{_scripts.IndexOf(script) + 1:00}.ps1", exeDir: true, runas: RunAs(t, ps.Script)));
                    break;
                default:
                    Skipped.Add(new SkippedTweak(t.Id, "live-only: " + a.GetType().Name));
                    break;
            }
        }
        return lines.Count > 1 ? lines : [];
    }

    /// <summary>Look scripts act on the signed-in user's session; per-user installs (winget) need the user's token elevated.</summary>
    private static string? RunAs(TweakDefinition t, string script) =>
        t.Category == "look" ? "currentUser"
        : script.Contains("winget", StringComparison.OrdinalIgnoreCase) || script.Contains("HKCU:", StringComparison.OrdinalIgnoreCase)
            || script.Contains("LOCALAPPDATA", StringComparison.OrdinalIgnoreCase) ? "currentUserElevated"
        : null; // TrustedInstaller

    /// <summary>File name of a Windows path, wherever the playbook is generated.</summary>
    private static string FileName(string windowsPath) => windowsPath[(windowsPath.LastIndexOf('\\') + 1)..];

    private static int Start(ServiceStartup s) => s switch
    {
        ServiceStartup.Disabled => 4,
        ServiceStartup.Manual => 3,
        _ => 2,
    };

    private static string LivePath(OfflineHive hive, string sub) => hive switch
    {
        OfflineHive.Software => @"HKLM\SOFTWARE\" + sub,
        OfflineHive.System => @"HKLM\SYSTEM\CurrentControlSet\" + (sub.StartsWith(@"ControlSet001\", StringComparison.OrdinalIgnoreCase) ? sub[14..] : sub),
        _ => @"HKCU\" + sub,
    };

    /// <summary>Registry data the way AME parses it.</summary>
    private static string Data(RegistryAction r)
    {
        var v = r.Value!.Value;
        return r.Kind switch
        {
            RegistryValueKind2.Dword => unchecked((uint)v.GetInt64()).ToString(CultureInfo.InvariantCulture),
            RegistryValueKind2.Qword => unchecked((ulong)v.GetInt64()).ToString(CultureInfo.InvariantCulture),
            RegistryValueKind2.Binary => Convert.ToHexString(v.ValueKind == JsonValueKind.Array
                ? v.EnumerateArray().Select(e => (byte)e.GetInt32()).ToArray()
                : Convert.FromBase64String(v.GetString() ?? "")),
            RegistryValueKind2.Multi => v.ValueKind == JsonValueKind.Array ? string.Join(@"\0", v.EnumerateArray().Select(e => e.GetString())) : v.GetString() ?? "",
            _ => v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ToString(),
        };
    }

    private static string Type(RegistryValueKind2 kind) => kind switch
    {
        RegistryValueKind2.Dword => "REG_DWORD",
        RegistryValueKind2.Qword => "REG_QWORD",
        RegistryValueKind2.Expand => "REG_EXPAND_SZ",
        RegistryValueKind2.Multi => "REG_MULTI_SZ",
        RegistryValueKind2.Binary => "REG_BINARY",
        _ => "REG_SZ",
    };

    // ------------------------------------------------------------------ YAML

    /// <summary>Single-quoted YAML scalar: backslashes stay literal, quotes double.</summary>
    private static string Q(string s) => "'" + s.Replace("'", "''") + "'";

    private static string Reg(string path, string name, RegistryValueKind2 kind, string? data) => data is null
        ? $"  - !registryValue: {{path: {Q(path)}, value: {Q(name)}, operation: delete}}"
        : $"  - !registryValue: {{path: {Q(path)}, value: {Q(name)}, type: {Type(kind)}, data: {Q(data)}}}";

    private static string Status(string text) => $"  - !writeStatus: {{status: {Q(text)}}}";

    private static string Task(string path, string? option = null) =>
        option is null ? $"  - !task: {{path: {Q(path)}}}" : $"  - !task: {{path: {Q(path)}, option: {Q(option)}}}";

    private static string Ps(string command, bool exeDir = false, string? runas = null, int weight = 1, bool ignoreErrors = true)
    {
        if (command.Contains('"')) throw new ArgumentException("AME passes the command inside double quotes", nameof(command));
        var sb = new StringBuilder($"  - !powerShell: {{command: {Q(command)}, wait: true");
        if (exeDir) sb.Append(", exeDir: true");
        if (runas is not null) sb.Append(", runas: ").Append(runas);
        if (weight != 1) sb.Append(", weight: ").Append(weight);
        if (ignoreErrors) sb.Append(", ignoreErrors: true");
        return sb.Append('}').ToString();
    }

    private string TaskFile(string title, string description, IEnumerable<string> actions)
    {
        var sb = new StringBuilder();
        sb.Append("---\n");
        sb.Append("title: ").Append(Q(title)).Append('\n');
        sb.Append("description: ").Append(Q(description)).Append('\n');
        sb.Append("actions:\n");
        foreach (var a in actions) sb.Append(a).Append('\n');
        return sb.ToString();
    }

    // ------------------------------------------------------------------ playbook.conf

    private string Conf(TweakCatalog catalog)
    {
        var pages = new XElement("FeaturePages");
        if (Options.Contains(OpenShellOption))
            pages.Add(new XElement("RadioPage",
                new XAttribute("IsRequired", "true"),
                new XAttribute("DefaultOption", OpenShellOption),
                new XAttribute("Description", T("Меню «Пуск»", "Start menu")),
                new XElement("Options",
                    Radio(OpenShellOption, T("Классическое меню Open-Shell в стиле Windows 7 (бесплатно)", "Classic Open-Shell menu, Windows 7 style (free)")),
                    Radio(KeepStartOption, T("Оставить меню Windows 11", "Keep the Windows 11 menu"))),
                new XElement("BottomLine", new XAttribute("Text", T("Об Open-Shell", "About Open-Shell")), new XAttribute("Link", "https://github.com/Open-Shell/Open-Shell-Menu"))));

        // AME shows at most 4 checkboxes per page
        var features = catalog.Features.Where(f => Options.Contains("off-" + f.Id)).ToList();
        for (int i = 0; i < features.Count; i += 4)
            pages.Add(new XElement("CheckboxPage",
                new XAttribute("IsRequired", "true"),
                new XAttribute("Description", T("Отметьте то, чем не пользуетесь, — это отключится", "Tick what you don't use; it will be turned off")),
                new XElement("Options", features.Skip(i).Take(4).Select(f => new XElement("CheckboxOption",
                    new XAttribute("IsChecked", f.KeepByDefault ? "false" : "true"),
                    new XElement("Text", T(f.Name)),
                    new XElement("Name", "off-" + f.Id))))));

        var doc = new XElement("Playbook",
            new XElement("Name", "AI Tweaker"),
            new XElement("Username", "AI Tweaker"),
            new XElement("Title", "AI Tweaker " + Version),
            new XElement("ShortDescription", T("Внешний вид AI Tweaker и отключение ненужного", "The AI Tweaker look, minus what you don't use")),
            new XElement("Description", T(
                "Тёмная тема, чёрные панель задач и «Пуск», чёрный рабочий стол, классическое меню Open-Shell. "
                + "На следующих страницах можно отключить то, чем вы не пользуетесь. Остальную настройку под ваше железо сделает приложение AI Tweaker.",
                "Dark mode, black taskbar and Start, black desktop, the classic Open-Shell menu. "
                + "The next pages let you turn off what you don't use. The AI Tweaker app tunes the rest for your hardware.")),
            new XElement("Details", T("Тот же каталог, что применяет приложение AI Tweaker.", "The same catalog the AI Tweaker app applies.")),
            new XElement("ProgressText", T("Применяем внешний вид и выбранные настройки. Рабочий стол перезапустится в конце.",
                "Applying the look and the options you chose. The desktop restarts at the end.")),
            new XElement("Version", Version),
            new XElement("UniqueId", UniqueId.ToString()),
            new XElement("Overhaul", "false"),
            new XElement("UseKernelDriver", "false"),
            new XElement("EstimatedMinutes", "3"),
            new XElement("Git", "https://github.com/sethosint/ai-tweaker"),
            pages);
        return "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" + doc.ToString().Replace("\r\n", "\n") + "\n";
    }

    private static XElement Radio(string name, string text) => new("RadioOption", new XElement("Text", text), new XElement("Name", name));

    // ------------------------------------------------------------------ scripts

    private static readonly string RestorePointScript = """
        # Same restore point as the AI Tweaker app makes before applying tweaks.
        try { Enable-ComputerRestore -Drive "$env:SystemDrive\" } catch { }
        New-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore' -Name SystemRestorePointCreationFrequency -Value 0 -PropertyType DWord -Force | Out-Null
        Checkpoint-Computer -Description 'AI Tweaker playbook' -RestorePointType MODIFY_SETTINGS

        """.Replace("\r\n", "\n");

    private static readonly string InstallLookScript = $$"""
        # AI Tweaker look: theme and wallpapers go where the registry points at them.
        $ErrorActionPreference = 'Continue'
        $dest = Join-Path $env:ProgramData 'AITweaker\Look'
        New-Item -ItemType Directory -Force -Path $dest | Out-Null
        Copy-Item -Path (Join-Path $PSScriptRoot '*') -Include '*.jpg', '*.theme' -Destination $dest -Force
        Copy-Item -Path (Join-Path $dest 'aitweaker.theme') -Destination (Join-Path $env:windir 'Resources\Themes') -Force -ErrorAction SilentlyContinue

        # Windows on another drive: point the lock screen at the real folder
        $baked = '{{LookAssets.Folder}}'
        if ($dest -ne $baked) {
            $csp = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\PersonalizationCSP'
            foreach ($name in 'LockScreenImagePath', 'LockScreenImageUrl') {
                $value = (Get-ItemProperty -Path $csp -Name $name -ErrorAction SilentlyContinue).$name
                if ($value) { Set-ItemProperty -Path $csp -Name $name -Value $value.Replace($baked, $dest) }
            }
        }

        """.Replace("\r\n", "\n");

    private string RefreshScript(List<TweakDefinition> look)
    {
        var w = look.SelectMany(t => t.Actions).OfType<WallpaperAction>().FirstOrDefault(a => a.Target == "desktop");
        var wallpaper = w is null ? null : w.SolidBlack ? "" : w.File;
        var sb = new StringBuilder();
        sb.Append("# Shows the new look right away for the signed-in user.\n");
        sb.Append("Add-Type -Namespace AITweaker -Name Native -MemberDefinition @'\n");
        sb.Append("[DllImport(\"user32.dll\", CharSet = CharSet.Unicode)] public static extern bool SystemParametersInfo(uint action, uint param, string value, uint flags);\n");
        sb.Append("[DllImport(\"user32.dll\")] public static extern bool SetSysColors(int count, int[] elements, int[] colors);\n");
        sb.Append("'@\n");
        if (wallpaper is not null)
        {
            if (wallpaper.Length == 0) sb.Append("[AITweaker.Native]::SetSysColors(1, @(1), @(0)) | Out-Null # COLOR_DESKTOP: black\n");
            else sb.Append("$wallpaper = Join-Path $env:ProgramData ").Append(Q(@"AITweaker\Look\" + FileName(wallpaper))).Append('\n');
            sb.Append("[AITweaker.Native]::SystemParametersInfo(0x14, 0, ").Append(wallpaper.Length == 0 ? "''" : "$wallpaper").Append(", 3) | Out-Null # SPI_SETDESKWALLPAPER\n");
        }
        sb.Append("Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue\n");
        sb.Append("Start-Sleep -Seconds 2\n");
        sb.Append("if (-not (Get-Process explorer -ErrorAction SilentlyContinue)) { Start-Process explorer.exe }\n");
        sb.Append("$menu = Join-Path $env:ProgramFiles 'Open-Shell\\StartMenu.exe'\n");
        sb.Append("if (Test-Path $menu) { Start-Sleep -Seconds 2; & $menu -reloadsettings }\n");
        return sb.ToString();
    }

    private static readonly string InstallOpenShellScript = """
        # Only the Open-Shell Start menu (no Explorer toolbar, no updater): winget first, then the official GitHub release.
        $menu = Join-Path $env:ProgramFiles 'Open-Shell\StartMenu.exe'
        if (Test-Path $menu) { exit 0 }
        $switches = '/qn ADDLOCAL=StartMenu'

        $winget = Get-Command winget -ErrorAction SilentlyContinue
        if ($winget) {
            & $winget.Source install --id Open-Shell.Open-Shell-Menu --exact --silent --accept-package-agreements --accept-source-agreements --disable-interactivity --override $switches
            if (Test-Path $menu) { exit 0 }
        }

        try {
            [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
            $release = Invoke-RestMethod -UseBasicParsing -Uri 'https://api.github.com/repos/Open-Shell/Open-Shell-Menu/releases/latest' -Headers @{ 'User-Agent' = 'AI-Tweaker' }
            $asset = $release.assets | Where-Object { $_.name -like 'OpenShellSetup*.exe' } | Select-Object -First 1
            $setup = Join-Path $env:TEMP $asset.name
            Invoke-WebRequest -UseBasicParsing -Uri $asset.browser_download_url -OutFile $setup
            Start-Process -FilePath $setup -ArgumentList $switches -Wait
        } catch {
            Write-Output "Open-Shell download failed: $_"
        }
        if (Test-Path $menu) { exit 0 } else { exit 1 }

        """.Replace("\r\n", "\n");
}

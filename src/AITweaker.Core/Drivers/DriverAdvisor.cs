using System.Text.RegularExpressions;
using AITweaker.Core.Bios;
using AITweaker.Core.Model;
using AITweaker.Core.Profile;

namespace AITweaker.Core.Drivers;

public sealed record DriverLink(string Title, string Url);

/// <summary>One component's driver: what is installed, whether it needs attention, and where the right driver comes from.</summary>
/// <param name="Status">ok | old | inbox (Windows' generic driver) | missing | error | check</param>
/// <param name="Priority">high | medium | low</param>
public sealed record DriverItem(
    string Category, string Name, string Vendor, string? Version, DateTime? Date, string? Provider,
    string Status, string Priority, LocalizedText? Note, IReadOnlyList<DriverLink> Links);

/// <summary>Picks, per component, the official driver source: the chip maker where its generic driver is the right one, the PC maker where it isn't.</summary>
public static class DriverAdvisor
{
    private const string OptionalUpdates = "ms-settings:windowsupdate-optionalupdates";
    private static readonly DriverLink IntelDsa = new("Intel Driver & Support Assistant", "https://www.intel.com/content/www/us/en/support/detect.html");
    private static readonly DriverLink WindowsOptional = new("Windows Update", OptionalUpdates);

    public static List<DriverItem> Build(IReadOnlyList<FirmwareVendor> vendors, SystemProfile p, DateTime? now = null)
    {
        var today = now ?? DateTime.Now;
        var maker = BiosAdvisor.Vendor(vendors, p);
        var oem = OemLinks(maker, p);
        var list = new List<DriverItem>();

        // One place for everything else: the PC maker's page for this exact model (laptops) or board (desktops).
        if (oem.Count > 0 && maker.Support is not null)
            list.Add(new DriverItem("oem", Model(p, maker), maker.Name, null, null, null, "check", "medium",
                p.Machine.IsLaptop
                    ? new("Звук, тачпад, Wi-Fi, клавиши и управление питанием на ноутбуке лучше ставить с сайта производителя: там драйверы под вашу модель." + (maker.Updater is null ? "" : $" Или через {maker.Updater}."),
                          "On a laptop, audio, touchpad, Wi-Fi, hotkeys and power management are best installed from the maker's site: those drivers are built for your model." + (maker.Updater is null ? "" : $" Or through {maker.Updater}."))
                    : new("Звук, LAN и утилиты платы — с сайта производителя платы, раздел «Поддержка» для вашей модели.", "Audio, LAN and board utilities come from the board maker's support page for your model."),
                oem));

        foreach (var g in p.Gpus) list.Add(Gpu(g, Find(p, g.PnpId), p, today, oem));
        list.Add(Chipset(p, oem));
        foreach (var n in p.Network) list.Add(Network(n, Find(p, n.PnpId), p, today, oem));
        foreach (var d in p.Drivers.Where(IsBluetoothRadio)) list.Add(Simple("bluetooth", d, today, oem, years: 2, inboxPriority: "low"));
        foreach (var d in p.Drivers.Where(IsAudioCodec)) list.Add(Audio(d, today, oem));

        foreach (var dev in p.ProblemDevices)
            list.Add(new DriverItem("problem", dev.Name, VendorOf(dev.PnpId, dev.Name, ""), null, null, null,
                dev.ErrorCode == 28 ? "missing" : "error", "high",
                new($"Код ошибки {dev.ErrorCode}. Сначала — «Дополнительные обновления» в Windows Update, потом сайт производителя.", $"Error code {dev.ErrorCode}. Try Windows Update optional updates first, then the maker's site."),
                [WindowsOptional, .. oem]));

        return list
            .OrderBy(i => i.Category == "problem" ? 0 : i.Category == "oem" ? 1 : 2)
            .ThenBy(i => i.Priority switch { "high" => 0, "medium" => 1, _ => 2 })
            .ToList();
    }

    // ------------------------------------------------------------------ components

    private static DriverItem Gpu(GpuInfo g, DeviceDriver? d, SystemProfile p, DateTime today, List<DriverLink> oem)
    {
        var provider = d?.Provider;
        bool generic = g.Name.Contains("Basic Display", StringComparison.OrdinalIgnoreCase) || IsMicrosoft(provider);
        var links = g.Vendor switch
        {
            "NVIDIA" => new List<DriverLink> { new("NVIDIA App", "https://www.nvidia.com/en-us/software/nvidia-app/"), new("GeForce drivers", "https://www.nvidia.com/en-us/drivers/") },
            "AMD" => [new("AMD Software: Adrenalin", "https://www.amd.com/en/support/download/drivers.html")],
            "Intel" => [IntelDsa],
            _ => oem,
        };
        if (generic)
            return new DriverItem("gpu", g.Name, g.Vendor, g.DriverVersion, g.DriverDate, provider, "missing", "high",
                new("Стоит базовый драйвер Windows: нет ускорения, высокой частоты и G-SYNC/FreeSync. Поставьте драйвер видеокарты.", "Windows' basic driver is in use: no acceleration, high refresh or G-SYNC/FreeSync. Install the GPU driver."), links);
        double age = g.DriverDate is { } date ? (today - date).TotalDays : 0;
        // The integrated GPU only drives the screen on hybrid laptops; on a desktop with a discrete card it barely matters.
        bool secondary = !g.IsDiscrete && p.Gpus.Any(x => x.IsDiscrete);
        if (age > (secondary ? 365 : 180))
            return new DriverItem("gpu", g.Name, g.Vendor, g.DriverVersion, g.DriverDate, provider, "old", secondary ? "low" : "medium",
                new($"Драйверу {Math.Round(age / 30)} мес. Свежие драйверы чинят фризы и дают FPS в новых играх.", $"The driver is {Math.Round(age / 30)} months old. New drivers fix stutter and add FPS in new games."), links);
        return new DriverItem("gpu", g.Name, g.Vendor, g.DriverVersion, g.DriverDate, provider, "ok", "low", null, links);
    }

    private static DriverItem Chipset(SystemProfile p, List<DriverLink> oem)
    {
        bool laptop = p.Machine.IsLaptop;
        if (p.Cpu.Vendor == "AMD")
        {
            var amd = p.Drivers.Where(d => d.Class == "SYSTEM" && IsAmd(d.Provider)).OrderByDescending(d => d.Date).FirstOrDefault();
            var links = laptop ? oem.Prepend(new DriverLink("AMD", "https://www.amd.com/en/support/download/drivers.html")).ToList()
                               : [new("AMD Chipset Drivers", "https://www.amd.com/en/support/download/drivers.html")];
            if (amd is not null)
                return new DriverItem("chipset", p.Cpu.Name, "AMD", amd.Version, amd.Date, amd.Provider, "ok", "low", null, links);
            bool x3d = p.Cpu.IsX3D && p.Cpu.Cores > 8;
            return new DriverItem("chipset", p.Cpu.Name, "AMD", null, null, null, "missing", x3d ? "high" : "medium",
                x3d ? new("Без драйвера чипсета AMD не работает 3D V-Cache Performance Optimizer: игры попадают не на тот CCD, FPS проседает.", "Without the AMD chipset driver the 3D V-Cache Performance Optimizer doesn't run: games land on the wrong CCD and FPS drops.")
                    : new("Драйвер чипсета AMD управляет питанием и парковкой ядер Ryzen. Сейчас стоят только стандартные драйверы Windows.", "The AMD chipset driver handles Ryzen power management and core parking. Only Windows' generic drivers are installed now."),
                links);
        }
        if (p.Cpu.Vendor == "Intel")
        {
            var mei = p.Drivers.FirstOrDefault(d => d.Class == "SYSTEM" && d.Name.Contains("Management Engine", StringComparison.OrdinalIgnoreCase) && !IsMicrosoft(d.Provider));
            List<DriverLink> links = laptop ? [.. oem, IntelDsa] : [new("Intel Chipset INF Utility", "https://www.intel.com/content/www/us/en/download/19347/chipset-inf-utility.html"), IntelDsa];
            return mei is not null
                ? new DriverItem("chipset", p.Cpu.Name, "Intel", mei.Version, mei.Date, mei.Provider, "ok", "low", null, links)
                : new DriverItem("chipset", p.Cpu.Name, "Intel", null, null, null, "check", "low",
                    new("Не нашли Intel Management Engine — поставьте драйвер чипсета и ME с сайта производителя платы или через Intel DSA.", "Intel Management Engine wasn't found — install the chipset and ME drivers from the board maker or through Intel DSA."), links);
        }
        return new DriverItem("chipset", p.Cpu.Name, p.Cpu.Vendor, null, null, null, "check", "low", null, oem);
    }

    private static DriverItem Network(NetAdapterInfo n, DeviceDriver? d, SystemProfile p, DateTime today, List<DriverLink> oem)
    {
        var vendor = VendorOf(n.PnpId, n.Description, d?.Manufacturer ?? "");
        var category = n.Kind == "wifi" ? "wifi" : "ethernet";
        var links = vendor switch
        {
            "Intel" or "Killer" => new List<DriverLink> { IntelDsa },
            _ => [.. oem, WindowsOptional],
        };
        LocalizedText? killer = vendor != "Killer" ? null : n.Kind == "wifi"
            ? new("Killer Wi-Fi — это чип Intel: ставьте обычный драйвер Intel Wi-Fi, без Killer Control Center (он лезет в приоритеты трафика и сам добавляет задержку).", "Killer Wi-Fi is an Intel chip: install the plain Intel Wi-Fi driver, without Killer Control Center (it meddles with traffic priority and adds latency itself).")
            : new("Ставьте только драйвер, без Killer Control Center: он лезет в приоритеты трафика и сам добавляет задержку. Intel DSA находит Killer-драйверы.", "Install the driver only, without Killer Control Center: it meddles with traffic priority and adds latency itself. Intel DSA finds Killer drivers.");
        if (d is not null && IsMicrosoft(d.Provider))
            return new DriverItem(category, n.Description, vendor, d.Version, d.Date, d.Provider, "inbox", "medium",
                killer ?? new("Стоит стандартный драйвер Windows. У производителя свежее, с полными настройками энергосбережения и буферов.", "Windows' generic driver is in use. The maker's is newer and exposes the full power-saving and buffer settings."), links);
        double age = d?.Date is { } date ? (today - date).TotalDays : 0;
        if (age > 730)
            return new DriverItem(category, n.Description, vendor, d!.Version, d.Date, d.Provider, "old", "medium",
                killer ?? new($"Драйверу {Math.Round(age / 365)} г. Свежие исправляют обрывы, пинг-спайки и сон адаптера.", $"The driver is {Math.Round(age / 365)} years old. New ones fix drops, ping spikes and adapter sleep issues."), links);
        return new DriverItem(category, n.Description, vendor, d?.Version, d?.Date, d?.Provider, killer is null ? "ok" : "check", killer is null ? "low" : "medium", killer, links);
    }

    private static DriverItem Audio(DeviceDriver d, DateTime today, List<DriverLink> oem)
    {
        var vendor = VendorOf(d.HardwareId.Length > 0 ? d.HardwareId : d.DeviceId, d.Name, d.Manufacturer);
        if (IsMicrosoft(d.Provider))
            return new DriverItem("audio", d.Name, vendor, d.Version, d.Date, d.Provider, "inbox", "low",
                new("Для игр стандартного драйвера хватает. Драйвер производителя нужен ради его панели (Realtek Audio Console, Nahimic) и разъёмов на передней панели.", "Windows' driver is fine for games. The maker's is only needed for its control panel (Realtek Audio Console, Nahimic) and front-panel jack detection."),
                [.. oem, WindowsOptional]);
        return Simple("audio", d, today, oem, years: 3, inboxPriority: "low");
    }

    private static DriverItem Simple(string category, DeviceDriver d, DateTime today, List<DriverLink> oem, int years, string inboxPriority)
    {
        var vendor = VendorOf(d.HardwareId.Length > 0 ? d.HardwareId : d.DeviceId, d.Name, d.Manufacturer);
        List<DriverLink> links = vendor is "Intel" or "Killer" ? [IntelDsa] : [.. oem, WindowsOptional];
        if (IsMicrosoft(d.Provider))
            return new DriverItem(category, d.Name, vendor, d.Version, d.Date, d.Provider, "inbox", inboxPriority, null, links);
        double age = d.Date is { } date ? (today - date).TotalDays : 0;
        if (age > 365 * years)
            return new DriverItem(category, d.Name, vendor, d.Version, d.Date, d.Provider, "old", "low", null, links);
        return new DriverItem(category, d.Name, vendor, d.Version, d.Date, d.Provider, "ok", "low", null, links);
    }

    // ------------------------------------------------------------------ helpers

    private static List<DriverLink> OemLinks(FirmwareVendor maker, SystemProfile p) =>
        maker.Support is { } url ? [new DriverLink(maker.Name, url)] : maker.Key == "microsoft" ? [new DriverLink("Windows Update", OptionalUpdates)] : [];

    private static string Model(SystemProfile p, FirmwareVendor maker) =>
        (p.Machine.IsLaptop || maker.Laptop is null ? $"{p.Machine.Manufacturer} {p.Machine.Model}" : $"{p.Board.Manufacturer} {p.Board.Product}").Trim();

    private static DeviceDriver? Find(SystemProfile p, string pnpId) =>
        pnpId.Length == 0 ? null : p.Drivers.FirstOrDefault(d => d.DeviceId.Equals(pnpId, StringComparison.OrdinalIgnoreCase))
            ?? p.Drivers.FirstOrDefault(d => pnpId.StartsWith(d.DeviceId, StringComparison.OrdinalIgnoreCase) || d.DeviceId.StartsWith(pnpId, StringComparison.OrdinalIgnoreCase));

    /// <summary>The Bluetooth radio itself (a USB or PCI device), not the profiles Windows enumerates under it.</summary>
    private static bool IsBluetoothRadio(DeviceDriver d) =>
        d.Class == "BLUETOOTH" && (d.DeviceId.StartsWith("USB\\VID_", StringComparison.OrdinalIgnoreCase) || d.DeviceId.StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase));

    /// <summary>The onboard audio codec; HDMI audio of the GPU is covered by the GPU driver.</summary>
    private static bool IsAudioCodec(DeviceDriver d) =>
        d.Class == "MEDIA" && d.DeviceId.StartsWith("HDAUDIO\\FUNC_01", StringComparison.OrdinalIgnoreCase)
        && VendorOf(d.DeviceId, d.Name, d.Manufacturer) is not ("NVIDIA" or "AMD" or "Intel");

    private static bool IsMicrosoft(string? provider) => provider is not null && provider.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase);
    private static bool IsAmd(string? provider) => provider is not null && (provider.Contains("Advanced Micro Devices", StringComparison.OrdinalIgnoreCase) || provider.StartsWith("AMD", StringComparison.OrdinalIgnoreCase));

    /// <summary>The chip maker from the PCI/USB vendor id, then from the driver's manufacturer string.</summary>
    public static string VendorOf(string id, string name, string manufacturer)
    {
        if (name.Contains("Killer", StringComparison.OrdinalIgnoreCase)) return "Killer";
        var m = Regex.Match(id, @"(?:VEN|VID)_([0-9A-F]{4})", RegexOptions.IgnoreCase);
        var byId = (m.Success ? m.Groups[1].Value.ToUpperInvariant() : "") switch
        {
            "10DE" => "NVIDIA",
            "1002" or "1022" => "AMD",
            "8086" or "8087" => "Intel",
            "10EC" or "0BDA" => "Realtek",
            "14C3" or "0E8D" => "MediaTek",
            "17CB" or "168C" or "1969" or "0CF3" => "Qualcomm",
            "14E4" or "0A5C" => "Broadcom",
            "1D6A" => "Marvell",
            _ => null,
        };
        if (byId is not null) return byId;
        var mf = manufacturer.ToLowerInvariant();
        foreach (var (key, vendor) in new[] { ("intel", "Intel"), ("realtek", "Realtek"), ("mediatek", "MediaTek"), ("qualcomm", "Qualcomm"), ("broadcom", "Broadcom"), ("nvidia", "NVIDIA"), ("advanced micro", "AMD"), ("amd", "AMD") })
            if (mf.Contains(key)) return vendor;
        return mf.StartsWith('(') || mf.StartsWith("microsoft") ? "" : manufacturer;
    }
}

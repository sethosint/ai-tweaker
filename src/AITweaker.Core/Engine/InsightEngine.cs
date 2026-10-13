using AITweaker.Core.Model;
using AITweaker.Core.Profile;

namespace AITweaker.Core.Engine;

public enum InsightSeverity
{
    Info,
    Warning,
    Critical,
}

/// <summary>A diagnostic "ticket" shown in the dashboard ticket flow.</summary>
public sealed record Insight(
    string Id,
    InsightSeverity Severity,
    LocalizedText Title,
    LocalizedText Detail,
    string? Action = null,
    string? Metric = null);

/// <summary>Hardware/software diagnostics that tweaks alone cannot fix (BIOS, drivers, hardware choices).</summary>
public static class InsightEngine
{
    public static List<Insight> Analyze(SystemProfile p, DateTime? now = null)
    {
        var today = now ?? DateTime.Now;
        var list = new List<Insight>();

        if (p.Machine.IsVirtualMachine)
        {
            list.Add(new("vm", InsightSeverity.Warning,
                new("Запуск в виртуальной машине", "Running inside a virtual machine"),
                new("Аппаратные твики (прерывания, MSI, BCD, сетевая карта) будут пропущены.", "Hardware tweaks (interrupts, MSI, BCD, NIC) will be skipped.")));
        }

        if (p.Os.Build > 0 && p.Os.Build < 22000)
        {
            list.Add(new("os.win10", InsightSeverity.Warning,
                new("Установлена Windows 10", "Windows 10 detected"),
                new("Программа рассчитана на Windows 11 (24H2+). Рекомендуется чистая установка Windows 11 через вкладку IMAGE.", "AI Tweaker targets Windows 11 (24H2+). A clean Windows 11 install from the IMAGE tab is recommended."),
                "image"));
        }

        foreach (var dev in p.ProblemDevices.Take(5))
        {
            list.Add(new("dev." + dev.PnpId, dev.ErrorCode == 28 ? InsightSeverity.Critical : InsightSeverity.Warning,
                new($"Нет драйвера: {dev.Name}", $"Missing driver: {dev.Name}"),
                new($"Устройство работает с ошибкой (код {dev.ErrorCode}). Установите драйвер во вкладке DRIVERS.", $"Device reports error code {dev.ErrorCode}. Install a driver in the DRIVERS tab."),
                "drivers"));
        }

        if (p.Memory.XmpLikelyOff)
        {
            list.Add(new("ram.xmp", InsightSeverity.Warning,
                new($"Память работает на {p.Memory.ConfiguredSpeed} МГц", $"RAM runs at {p.Memory.ConfiguredSpeed} MHz"),
                new("Похоже, профиль XMP/EXPO выключен в BIOS — вы теряете 5–15% FPS в CPU-зависимых играх.", "XMP/EXPO looks disabled in BIOS — that costs 5–15% FPS in CPU-bound games."),
                "bios", $"{p.Memory.ConfiguredSpeed} MHz"));
        }

        if (p.Memory.Modules.Count == 1 && !p.Machine.IsLaptop)
        {
            list.Add(new("ram.single", InsightSeverity.Warning,
                new("Одна планка памяти (одноканальный режим)", "Single memory stick (single channel)"),
                new("Одноканальная память режет пропускную способность вдвое. Поставьте вторую такую же планку.", "Single-channel memory halves bandwidth. Add a matching second stick.")));
        }

        if (p.Memory.TotalGb > 0 && p.Memory.TotalGb < 15)
        {
            list.Add(new("ram.low", InsightSeverity.Info,
                new($"{p.Memory.TotalGb:0} ГБ ОЗУ", $"{p.Memory.TotalGb:0} GB of RAM"),
                new("Для современных игр рекомендуется 16–32 ГБ. Службы предзагрузки (SysMain) оставлены включёнными.", "16–32 GB is recommended for modern games. Prefetch services (SysMain) are kept enabled.")));
        }

        if (IsVminAffected(p.Cpu))
        {
            bool patched = p.Cpu.MicrocodeRevision is >= 0x12B;
            if (!patched)
            {
                list.Add(new("cpu.intel.vmin", InsightSeverity.Critical,
                    new("Intel 13/14 gen: обновите BIOS", "Intel 13th/14th gen: update your BIOS"),
                    new(p.Cpu.MicrocodeRevision is { } mc
                            ? $"Микрокод 0x{mc:X} старше 0x12B. Без обновления BIOS процессор подвержен деградации (Vmin Shift)."
                            : "Не удалось прочитать версию микрокода. Убедитесь, что BIOS содержит микрокод 0x12B или новее (защита от деградации Vmin Shift).",
                        p.Cpu.MicrocodeRevision is { } mc2
                            ? $"Microcode 0x{mc2:X} is older than 0x12B. Without a BIOS update the CPU is exposed to Vmin Shift degradation."
                            : "Could not read the microcode revision. Make sure the BIOS ships microcode 0x12B or newer (Vmin Shift fix)."),
                    "bios"));
            }
        }

        if (p.Cpu.IsX3D && p.Cpu.Cores > 8)
        {
            list.Add(new("cpu.x3d.dual", InsightSeverity.Info,
                new("Двухчиплетный X3D: Game Bar сохранён", "Dual-CCD X3D: Game Bar kept"),
                new("Драйвер AMD 3D V-Cache использует Xbox Game Bar, чтобы отправлять игры на кэш-ядра. Game Bar не будет удалён, режим «Игра» включён.", "The AMD 3D V-Cache driver relies on Xbox Game Bar to park games on the cache cores. Game Bar stays installed and Game Mode on.")));
        }

        if (p.Cpu.IsHybrid)
        {
            list.Add(new("cpu.hybrid", InsightSeverity.Info,
                new($"Гибридный CPU: {p.Cpu.PCores}P + {p.Cpu.ECores}E", $"Hybrid CPU: {p.Cpu.PCores}P + {p.Cpu.ECores}E"),
                new("Прерывания GPU, сети и USB будут закреплены за P-ядрами. E-ядра отключать не нужно — Thread Director распределяет фоновые задачи на них.", "GPU, network and USB interrupts will be pinned to P-cores. Keep E-cores on — Thread Director moves background work there.")));
        }

        var sys = p.SystemDisk;
        if (sys is not null && sys.MediaType == "HDD")
        {
            list.Add(new("disk.hdd", InsightSeverity.Critical,
                new("Windows установлена на HDD", "Windows is installed on an HDD"),
                new("Жёсткий диск — главный источник фризов и долгих загрузок. Перенесите систему на SSD (лучше NVMe).", "A hard drive is the main source of stutter and long loads. Move Windows to an SSD (NVMe preferred).")));
        }

        if (p.SystemDriveFreePercent < 15)
        {
            list.Add(new("disk.full", InsightSeverity.Warning,
                new($"Диск C: заполнен ({100 - p.SystemDriveFreePercent:0}%)", $"Drive C: is {100 - p.SystemDriveFreePercent:0}% full"),
                new("Когда на SSD мало места, падает скорость записи и растут фризы. Освободите хотя бы 15–20%.", "Nearly full SSDs slow down writes and cause stutter. Keep at least 15–20% free.")));
        }

        var gpu = p.PrimaryGpu;
        if (gpu?.DriverDate is { } dd && gpu.IsDiscrete && (today - dd).TotalDays > 180)
        {
            list.Add(new("gpu.driver.old", InsightSeverity.Warning,
                new($"Драйвер видеокарты от {dd:dd.MM.yyyy}", $"GPU driver from {dd:yyyy-MM-dd}"),
                new($"Драйверу {gpu.Name} больше полугода. Свежие драйверы дают прирост FPS в новых играх.", $"The {gpu.Name} driver is over six months old. New drivers bring FPS gains in recent games."),
                "drivers", gpu.DriverVersion));
        }

        foreach (var d in p.Displays.Where(d => d.MaxRefreshHz > d.RefreshHz))
        {
            list.Add(new("display.hz." + d.Device, InsightSeverity.Critical,
                new($"Монитор работает на {d.RefreshHz} Гц вместо {d.MaxRefreshHz} Гц", $"Monitor runs at {d.RefreshHz} Hz instead of {d.MaxRefreshHz} Hz"),
                new($"{d.Name}: включите максимальную частоту обновления — это самый заметный прирост плавности.", $"{d.Name}: switch to the maximum refresh rate — the most noticeable smoothness upgrade."),
                "tweak:display.maxhz", $"{d.RefreshHz}→{d.MaxRefreshHz} Hz"));
        }

        if (!p.Security.SecureBoot || !p.Security.Tpm)
        {
            list.Add(new("sec.secureboot", InsightSeverity.Info,
                new(!p.Security.SecureBoot ? "Secure Boot выключен" : "TPM не найден", !p.Security.SecureBoot ? "Secure Boot is off" : "TPM not found"),
                new("Valorant (Vanguard), FACEIT, Battlefield 6 и ряд других античитов требуют Secure Boot и TPM 2.0. Включается в BIOS.", "Valorant (Vanguard), FACEIT, Battlefield 6 and other anti-cheats require Secure Boot and TPM 2.0. Enable them in BIOS."),
                "bios"));
        }

        if (p.Network.Any(n => n.Kind == "wifi" && n.Connected) && !p.Network.Any(n => n.Kind == "ethernet" && n.Connected))
        {
            list.Add(new("net.wifi", InsightSeverity.Info,
                new("Подключение по Wi-Fi", "Connected over Wi-Fi"),
                new("Wi-Fi добавляет джиттер. Для соревновательных игр лучше кабель; Wi-Fi адаптер будет переведён в режим максимальной производительности.", "Wi-Fi adds jitter. Use a cable for competitive games; the Wi-Fi adapter will be set to maximum performance.")));
        }

        if (p.Machine.IsLaptop)
        {
            list.Add(new("laptop", InsightSeverity.Info,
                new("Ноутбук: профиль адаптирован", "Laptop: profile adapted"),
                new("Энергосхема меняется только для работы от сети, на батарее Windows экономит заряд. Играйте с подключённым зарядным устройством.", "Power tweaks apply to AC only; on battery Windows still saves charge. Game with the charger plugged in.")));
        }

        return list.OrderByDescending(i => i.Severity).ToList();
    }

    /// <summary>Raptor Lake desktop parts (65 W+) hit by the Vmin Shift instability, fixed by microcode 0x12B.</summary>
    internal static bool IsVminAffected(CpuInfo cpu)
    {
        if (cpu.Vendor != "Intel" || cpu.IntelGeneration is not (13 or 14)) return false;
        var n = cpu.Name;
        if (n.Contains("HX", StringComparison.OrdinalIgnoreCase)) return false; // mobile parts are not affected
        return n.Contains("i9", StringComparison.OrdinalIgnoreCase)
            || n.Contains("i7", StringComparison.OrdinalIgnoreCase)
            || n.Contains("13600", StringComparison.Ordinal) || n.Contains("13700", StringComparison.Ordinal)
            || n.Contains("14600", StringComparison.Ordinal);
    }
}

using AITweaker.Core.Profile;

namespace AITweaker.App.Platform;

public sealed record DriverLink(string Title, string Url);

public sealed record DriverItem(string Category, string Name, string Vendor, string? Version, DateTime? Date, string Status, IReadOnlyList<DriverLink> Links);

/// <summary>Shows the drivers that matter for FPS and latency and links to the official download pages.</summary>
public static class DriverCenter
{
    public static List<DriverItem> Build(SystemProfile p)
    {
        var list = new List<DriverItem>();

        foreach (var g in p.Gpus)
        {
            var links = g.Vendor switch
            {
                "NVIDIA" => new List<DriverLink>
                {
                    new("NVIDIA App", "https://www.nvidia.com/en-us/software/nvidia-app/"),
                    new("GeForce drivers", "https://www.nvidia.com/en-us/drivers/"),
                },
                "AMD" => [new("AMD Adrenalin", "https://www.amd.com/en/support/download/drivers.html")],
                "Intel" => [new("Intel Driver & Support Assistant", "https://www.intel.com/content/www/us/en/support/detect.html")],
                _ => [],
            };
            var age = g.DriverDate is { } d ? (DateTime.Now - d).TotalDays : 0;
            list.Add(new DriverItem("gpu", g.Name, g.Vendor, g.DriverVersion, g.DriverDate, age > 180 ? "old" : "ok", links));
        }

        var chipset = p.Cpu.Vendor switch
        {
            "AMD" => new List<DriverLink> { new("AMD Chipset Drivers", "https://www.amd.com/en/support/download/drivers.html") },
            "Intel" => [new("Intel Chipset INF Utility", "https://www.intel.com/content/www/us/en/download/19347/chipset-inf-utility.html")],
            _ => [],
        };
        list.Add(new DriverItem("chipset", p.Cpu.Name, p.Cpu.Vendor, null, null, "check", chipset));

        foreach (var n in p.Network)
            list.Add(new DriverItem("network", n.Description, n.Kind, null, null, n.Connected ? "ok" : "check", BoardLinks(p)));

        foreach (var dev in p.ProblemDevices)
            list.Add(new DriverItem("problem", dev.Name, dev.PnpId, null, null, dev.ErrorCode == 28 ? "missing" : "error",
                [new("Windows Update → Optional updates", "ms-settings:windowsupdate-optionalupdates"), .. BoardLinks(p)]));

        return list;
    }

    public static List<DriverLink> BoardLinks(SystemProfile p) => p.Board.VendorKey switch
    {
        "asus" => [new("ASUS Support", "https://www.asus.com/support/download-center/")],
        "msi" => [new("MSI Support", "https://www.msi.com/support/download")],
        "gigabyte" => [new("GIGABYTE Support", "https://www.gigabyte.com/Support")],
        "asrock" => [new("ASRock Support", "https://www.asrock.com/support/index.asp")],
        _ => [new("Windows Update", "ms-settings:windowsupdate")],
    };

    public static Task<ProcessResult> RescanAsync() => ProcessRunner.RunAsync("pnputil.exe", "/scan-devices", TimeSpan.FromMinutes(2));
}

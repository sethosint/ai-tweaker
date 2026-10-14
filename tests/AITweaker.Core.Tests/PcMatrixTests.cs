using System.Text.Json;
using AITweaker.Core.Catalog;
using AITweaker.Core.Conditions;
using AITweaker.Core.Engine;
using AITweaker.Core.Model;
using AITweaker.Core.Profile;

namespace AITweaker.Core.Tests;

/// <summary>The full plan for many kinds of PC: each gets exactly one power plan and no two changes fight over the same setting.</summary>
public class PcMatrixTests
{
    private static readonly RecommendationEngine Engine = new(TweakCatalog.LoadEmbedded());

    public static TheoryData<string> Pcs => new(Matrix.Keys);

    private static readonly Dictionary<string, Func<SystemProfile>> Matrix = new()
    {
        ["desktop-amd-x3d-nvidia"] = TestProfiles.Desktop,
        ["desktop-dual-ccd-x3d"] = TestProfiles.DualCcdX3D,
        ["laptop-intel-hybrid-nvidia"] = TestProfiles.Laptop,
        ["desktop-intel-hybrid-amd-gpu"] = () =>
        {
            var p = TestProfiles.Desktop();
            p.Cpu = new CpuInfo { Name = "Intel(R) Core(TM) i7-14700K", Vendor = "Intel", Cores = 20, Threads = 28, Topology = TestProfiles.Cores(20, pCores: 8) };
            p.Gpus = [new GpuInfo { Name = "AMD Radeon RX 7800 XT", Vendor = "AMD", IsDiscrete = true, PnpId = "PCI\\VEN_1002&DEV_747E" }];
            p.Board = new BoardInfo { Manufacturer = "Micro-Star International Co., Ltd.", Product = "MAG Z790 TOMAHAWK WIFI" };
            return p;
        },
        ["desktop-intel-arc-hdd"] = () =>
        {
            var p = TestProfiles.Desktop();
            p.Cpu = new CpuInfo { Name = "Intel(R) Core(TM) i5-12400F", Vendor = "Intel", Cores = 6, Threads = 12, Topology = TestProfiles.Cores(6) };
            p.Gpus = [new GpuInfo { Name = "Intel(R) Arc(TM) B580 Graphics", Vendor = "Intel", IsDiscrete = true, PnpId = "PCI\\VEN_8086&DEV_E20B" }];
            p.Disks = [new DiskInfo { Model = "Kingston A400", MediaType = "SSD", BusType = "SATA", IsSystem = true, SizeGb = 480 }, new DiskInfo { Model = "WD Blue", MediaType = "HDD", BusType = "SATA", SizeGb = 2000 }];
            p.Network = [new NetAdapterInfo { Description = "Intel Wi-Fi 6 AX200", Kind = "wifi", Connected = true }];
            return p;
        },
        ["laptop-amd-apu-only"] = () =>
        {
            var p = TestProfiles.Laptop();
            p.Cpu = new CpuInfo { Name = "AMD Ryzen 7 7840U w/ Radeon 780M Graphics", Vendor = "AMD", Cores = 8, Threads = 16, Topology = TestProfiles.Cores(8) };
            p.Gpus = [new GpuInfo { Name = "AMD Radeon 780M Graphics", Vendor = "AMD", IsDiscrete = false, PnpId = "PCI\\VEN_1002&DEV_15BF" }];
            p.Machine = new MachineInfo { IsLaptop = true, HasBattery = true, Manufacturer = "HP", Model = "EliteBook 845 G10" };
            return p;
        },
        ["laptop-amd-nvidia-hybrid"] = () =>
        {
            var p = TestProfiles.Laptop();
            p.Cpu = new CpuInfo { Name = "AMD Ryzen 9 8945HS", Vendor = "AMD", Cores = 8, Threads = 16, Topology = TestProfiles.Cores(8) };
            p.Gpus =
            [
                new GpuInfo { Name = "AMD Radeon 780M Graphics", Vendor = "AMD", IsDiscrete = false, PnpId = "PCI\\VEN_1002&DEV_15BF" },
                new GpuInfo { Name = "NVIDIA GeForce RTX 4060 Laptop GPU", Vendor = "NVIDIA", IsDiscrete = true, PnpId = "PCI\\VEN_10DE&DEV_28E0" },
            ];
            p.Machine = new MachineInfo { IsLaptop = true, HasBattery = true, Manufacturer = "ASUSTeK COMPUTER INC.", Model = "ROG Zephyrus G14" };
            return p;
        },
        ["desktop-windows10-old"] = () =>
        {
            var p = TestProfiles.Desktop();
            p.Os = new OsInfo { Caption = "Microsoft Windows 10 Pro", Build = 19045, Ubr = 5000, Edition = "Professional", DisplayVersion = "22H2" };
            p.Cpu = new CpuInfo { Name = "Intel(R) Core(TM) i7-8700K", Vendor = "Intel", Cores = 6, Threads = 12, Topology = TestProfiles.Cores(6) };
            p.Gpus = [new GpuInfo { Name = "NVIDIA GeForce GTX 1070", Vendor = "NVIDIA", IsDiscrete = true, PnpId = "PCI\\VEN_10DE&DEV_1B81" }];
            p.Memory = new MemoryInfo { TotalGb = 16, Type = "DDR4", Modules = [new MemoryModule { CapacityGb = 8, Speed = 3200, ConfiguredSpeed = 2133 }, new MemoryModule { CapacityGb = 8, Speed = 3200, ConfiguredSpeed = 2133 }] };
            p.Security = new SecurityInfo { SecureBoot = false, Tpm = false };
            return p;
        },
        ["virtual-machine"] = () =>
        {
            var p = TestProfiles.Desktop();
            p.Machine.IsVirtualMachine = true;
            p.Gpus = [new GpuInfo { Name = "Microsoft Basic Display Adapter", Vendor = "Other" }];
            return p;
        },
    };

    private static (SystemProfile Profile, TunePlan Plan) Build(string pc)
    {
        var p = Matrix[pc]();
        return (p, Engine.BuildPlan(p, TuneProfile.Extreme));
    }

    [Theory]
    [MemberData(nameof(Pcs))]
    public void ExactlyOnePowerPlan(string pc)
    {
        var (p, plan) = Build(pc);
        var facts = p.ToFacts();
        var plans = plan.Items.Where(i => i.Tweak.Actions.Any(a => a is PowerSchemeAction && ConditionEvaluator.Evaluate(a.When, facts))).Select(i => i.Tweak.Id).ToList();
        Assert.True(plans.Count == 1, $"{pc}: {string.Join(", ", plans)}");
    }

    [Theory]
    [MemberData(nameof(Pcs))]
    public void NoTwoChangesFightOverOneSetting(string pc)
    {
        var (p, plan) = Build(pc);
        var facts = p.ToFacts();
        var seen = new Dictionary<string, (string Tweak, string Value)>(StringComparer.OrdinalIgnoreCase);
        var clashes = new List<string>();
        foreach (var item in plan.Items)
            foreach (var a in item.Tweak.Actions.Where(a => ConditionEvaluator.Evaluate(a.When, facts)))
            {
                (string Key, string Value)? target = a switch
                {
                    RegistryAction r => ($"reg:{r.Path}\\{r.Name}", r.Delete ? "<delete>" : r.Value?.GetRawText() ?? "null"),
                    ServiceAction s => ($"svc:{s.Name}", JsonSerializer.Serialize(s)),
                    PowerSettingAction ps => ($"pwr:{ps.Subgroup}/{ps.Setting}", $"{ps.Ac}/{ps.Dc}"),
                    BcdAction b => ($"bcd:{b.Element}", b.Value ?? "<delete>"),
                    _ => null,
                };
                if (target is not { } t) continue;
                if (seen.TryGetValue(t.Key, out var prev) && prev.Value != t.Value && prev.Tweak != item.Tweak.Id)
                    clashes.Add($"{t.Key}: {prev.Tweak}={prev.Value} vs {item.Tweak.Id}={t.Value}");
                seen.TryAdd(t.Key, (item.Tweak.Id, t.Value));
            }
        Assert.True(clashes.Count == 0, $"{pc}:\n" + string.Join("\n", clashes));
    }

    /// <summary>USB selective suspend, USB 3 link power, PCIe ASPM, disk idle, SATA link power, Wi-Fi power saving.</summary>
    private static readonly HashSet<string> PowerSaving = new(StringComparer.OrdinalIgnoreCase) { "48e6b7a6", "d4e98f31", "ee12f906", "6738e2c4", "0b2d69d7", "12bbebe6" };

    [Theory]
    [MemberData(nameof(Pcs))]
    public void LaptopsNeverLoseBatterySaving(string pc)
    {
        var (p, plan) = Build(pc);
        if (!p.Machine.IsLaptop) return;
        var facts = p.ToFacts();
        // on battery a laptop keeps Windows' power saving: no battery (DC) index of the CPU, link power states, disks or Wi-Fi is forced on
        var dc = plan.Items.SelectMany(i => i.Tweak.Actions.Where(a => ConditionEvaluator.Evaluate(a.When, facts)).OfType<PowerSettingAction>()
            .Where(a => a.Dc is not null && (a.Subgroup.StartsWith("54533251", StringComparison.OrdinalIgnoreCase) || PowerSaving.Contains(a.Setting[..8])))
            .Select(a => $"{i.Tweak.Id} {a.Alias ?? a.Setting}={a.Dc}")).ToList();
        Assert.True(dc.Count == 0, string.Join("\n", dc));
    }
}

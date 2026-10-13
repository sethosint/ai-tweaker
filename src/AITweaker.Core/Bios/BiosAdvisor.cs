using System.Text.RegularExpressions;
using AITweaker.Core.Catalog;
using AITweaker.Core.Conditions;
using AITweaker.Core.Model;
using AITweaker.Core.Profile;

namespace AITweaker.Core.Bios;

public sealed record BiosAdvice(BiosRecommendation Item, string Path, string Status);

public static class BiosAdvisor
{
    public static IReadOnlyList<BiosAdvice> Advise(TweakCatalog catalog, SystemProfile profile)
    {
        var facts = profile.ToFacts();
        var vendor = Vendor(catalog.Vendors, profile);
        var result = new List<BiosAdvice>();
        foreach (var item in catalog.Bios)
        {
            if (!ConditionEvaluator.Evaluate(item.When, facts)) continue;
            result.Add(new BiosAdvice(item, PathFor(item, vendor.Key, profile.Machine.IsLaptop), StatusFor(item.Id, profile)));
        }
        return result.OrderBy(a => a.Item.Priority switch { "high" => 0, "medium" => 1, _ => 2 }).ToList();
    }

    /// <summary>
    /// Laptops and prebuilt PCs are told apart by the system maker (Alienware, Lenovo…), self-built desktops by the board maker.
    /// Falls back to the generic "laptop" or "other" entry.
    /// </summary>
    public static FirmwareVendor Vendor(IReadOnlyList<FirmwareVendor> vendors, SystemProfile p)
    {
        bool laptop = p.Machine.IsLaptop;
        string[] names = laptop ? [p.Machine.Manufacturer, p.Board.Manufacturer] : [p.Board.Manufacturer, p.Machine.Manufacturer];
        foreach (var name in names.Where(n => !string.IsNullOrWhiteSpace(n)))
            foreach (var v in vendors)
                if ((v.Laptop is null || v.Laptop == laptop) && Regex.IsMatch(name, v.Match, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                    return v;
        return vendors.First(v => v.Key == (laptop ? "laptop" : "other"));
    }

    public static string PathFor(BiosRecommendation item, string vendorKey, bool laptop) =>
        item.Paths.GetValueOrDefault(vendorKey) ?? (laptop ? item.Paths.GetValueOrDefault("laptop") : null) ?? item.Paths.GetValueOrDefault("other") ?? "";

    /// <summary>"ok" when the scan proves the setting is already right, "fix" when it is wrong, "check" when unknown.</summary>
    private static string StatusFor(string id, SystemProfile p) => id switch
    {
        "xmp" => p.Memory.ConfiguredSpeed == 0 ? "check" : p.Memory.XmpLikelyOff ? "fix" : "ok",
        "secureboot" => p.Security.SecureBoot ? "ok" : "fix",
        "tpm" => p.Security.Tpm ? "ok" : "fix",
        "microcode" => p.Cpu.MicrocodeRevision is >= 0x12B ? "ok" : "fix",
        _ => "check",
    };
}

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
        var vendor = profile.Board.VendorKey;
        var result = new List<BiosAdvice>();
        foreach (var item in catalog.Bios)
        {
            if (!ConditionEvaluator.Evaluate(item.When, facts)) continue;
            var path = item.Paths.GetValueOrDefault(vendor) ?? item.Paths.GetValueOrDefault("other") ?? "";
            result.Add(new BiosAdvice(item, path, StatusFor(item.Id, profile)));
        }
        return result.OrderBy(a => a.Item.Priority switch { "high" => 0, "medium" => 1, _ => 2 }).ToList();
    }

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

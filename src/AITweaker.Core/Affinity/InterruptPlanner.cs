using AITweaker.Core.Profile;

namespace AITweaker.Core.Affinity;

public sealed record AffinityAssignment(string Target, int CoreIndex, int LogicalProcessor)
{
    /// <summary>KAFFINITY bitmask for <c>AssignmentSetOverride</c> (processor group 0).</summary>
    public ulong Mask => 1UL << LogicalProcessor;

    /// <summary>Little-endian bytes as stored in the REG_BINARY AssignmentSetOverride value.</summary>
    public byte[] MaskBytes => BitConverter.GetBytes(Mask);
}

/// <summary>
/// Picks dedicated performance cores for device interrupts. Rules:
/// never core 0 (it services most system interrupts and DPCs), prefer the highest efficiency class
/// (P-cores on hybrid CPUs), one physical core per device class, first logical processor of that core.
/// </summary>
public static class InterruptPlanner
{
    public static IReadOnlyList<AffinityAssignment> Plan(IReadOnlyList<CoreInfo> cores, IEnumerable<string> targets)
    {
        var list = targets.ToList();
        var candidates = cores
            .Where(c => c.LogicalProcessors.Count > 0 && !c.LogicalProcessors.Contains(0) && c.LogicalProcessors.Min() < 64)
            .OrderByDescending(c => c.EfficiencyClass)
            .ThenBy(c => c.Index)
            .ToList();

        // Not worth it (and risky) on machines with fewer than 4 cores.
        if (cores.Count < 4 || candidates.Count == 0) return [];

        var result = new List<AffinityAssignment>(list.Count);
        for (int i = 0; i < list.Count; i++)
        {
            var core = candidates[i % candidates.Count];
            result.Add(new AffinityAssignment(list[i], core.Index, core.LogicalProcessors.Min()));
        }
        return result;
    }
}

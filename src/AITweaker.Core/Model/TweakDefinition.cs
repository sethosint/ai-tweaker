using System.Text.Json.Serialization;

namespace AITweaker.Core.Model;

[JsonConverter(typeof(JsonStringEnumConverter<RiskLevel>))]
public enum RiskLevel
{
    Low,
    Medium,
    High,
}

/// <summary>Optimization profiles. A tweak with <see cref="TweakDefinition.Level"/> N is included in profile N and above.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TuneProfile>))]
public enum TuneProfile
{
    Safe = 1,
    Balanced = 2,
    Performance = 3,
    Extreme = 4,
}

public sealed class TweakDefinition
{
    public required string Id { get; init; }
    public required string Category { get; init; }
    public required LocalizedText Name { get; init; }
    public required LocalizedText Description { get; init; }

    /// <summary>1 = safe, 2 = balanced, 3 = performance, 4 = extreme. 0 = never automatic (manual only).</summary>
    public int Level { get; init; } = 2;

    public RiskLevel Risk { get; init; } = RiskLevel.Low;

    /// <summary>The tweak disables or removes something people may actually use (Store, Xbox, printing...). The UI must ask first.</summary>
    public bool Confirm { get; init; }

    /// <summary>What the user loses if they say yes. Shown in the confirmation dialog.</summary>
    public LocalizedText? ConfirmText { get; init; }

    public bool Reboot { get; init; }
    public bool Logoff { get; init; }
    public bool RestartExplorer { get; init; }

    /// <summary>False when rollback is not possible automatically (e.g. removing an app package).</summary>
    public bool Reversible { get; init; } = true;

    /// <summary>Condition over hardware facts, see <see cref="Conditions.ConditionEvaluator"/>. Null = always applicable.</summary>
    public string? When { get; init; }

    /// <summary>Human explanation of why the engine picks this tweak for the current machine.</summary>
    public LocalizedText? Why { get; init; }

    public List<string> Tags { get; init; } = [];

    public required List<TweakAction> Actions { get; init; }
}

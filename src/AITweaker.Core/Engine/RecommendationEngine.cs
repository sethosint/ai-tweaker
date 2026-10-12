using AITweaker.Core.Catalog;
using AITweaker.Core.Conditions;
using AITweaker.Core.Model;
using AITweaker.Core.Profile;

namespace AITweaker.Core.Engine;

public sealed record PlanItem(TweakDefinition Tweak, bool Recommended, bool NeedsConfirm, LocalizedText Reason);

public sealed record TunePlan(TuneProfile Profile, IReadOnlyList<PlanItem> Items, IReadOnlyList<string> SkippedIds)
{
    public IEnumerable<PlanItem> Recommended => Items.Where(i => i.Recommended);
}

/// <summary>
/// The local "AI" engine: picks the tweaks that fit this exact machine (CPU vendor, hybrid cores, laptop vs desktop,
/// GPU vendor, disk type, network...) and the selected aggressiveness profile.
/// </summary>
public sealed class RecommendationEngine(TweakCatalog catalog)
{
    public TunePlan BuildPlan(SystemProfile profile, TuneProfile level)
    {
        var facts = profile.ToFacts();
        var items = new List<PlanItem>();
        var skipped = new List<string>();

        foreach (var tweak in catalog.Tweaks)
        {
            if (!ConditionEvaluator.Evaluate(tweak.When, facts))
            {
                skipped.Add(tweak.Id);
                continue;
            }

            // Skip tweaks whose every action is filtered out on this machine.
            if (tweak.Actions.Count > 0 && tweak.Actions.All(a => !ConditionEvaluator.Evaluate(a.When, facts)))
            {
                skipped.Add(tweak.Id);
                continue;
            }

            bool inProfile = tweak.Level > 0 && tweak.Level <= (int)level;
            bool recommended = inProfile && !(profile.Machine.IsVirtualMachine && IsHardwareTweak(tweak));
            items.Add(new PlanItem(tweak, recommended, tweak.Confirm || tweak.Risk == RiskLevel.High, ReasonFor(tweak, profile)));
        }

        return new TunePlan(level, items, skipped);
    }

    /// <summary>Hardware-level tweaks are pointless (or harmful) inside a virtual machine.</summary>
    private static bool IsHardwareTweak(TweakDefinition t) =>
        t.Actions.Any(a => a is InterruptAffinityAction or MsiModeAction or BcdAction or NicPropertyAction or DeviceAction);

    private static LocalizedText ReasonFor(TweakDefinition t, SystemProfile p)
    {
        if (t.Why is not null) return t.Why;
        return t.Category switch
        {
            "privacy" => new("Меньше фоновой телеметрии — меньше нагрузки на CPU, диск и сеть.", "Less background telemetry means less CPU, disk and network load."),
            "gaming" => new("Снижает задержку кадра и фоновые прерывания во время игры.", "Reduces frame latency and background interruptions while gaming."),
            "input" => new("Прямой отклик мыши и клавиатуры без сглаживания и задержек.", "Raw, consistent mouse and keyboard response."),
            "network" => new("Стабильнее пинг и меньше джиттер в онлайн-играх.", "Lower jitter and steadier ping in online games."),
            "power" => p.Machine.IsLaptop
                ? new("Ноутбук: максимальная производительность от сети, без жёсткой экономии.", "Laptop: full performance on AC without aggressive power saving.")
                : new("ПК: процессор и шины не уходят в энергосбережение посреди кадра.", "Desktop: CPU and buses never drop into power saving mid-frame."),
            "system" => new("Освобождает ресурсы, которые Windows тратит впустую.", "Frees resources Windows spends on things you don't use."),
            "interface" => new("Быстрее и чище интерфейс, меньше анимаций и рекламы.", "Snappier, cleaner shell with fewer animations and ads."),
            "security" => new("Компромисс безопасности ради FPS — только по вашему решению.", "A security-for-FPS trade-off — only if you decide so."),
            "devices" => new("Прерывания устройств уходят на быстрые ядра, лишние устройства отключаются.", "Device interrupts move to fast cores, unused devices are disabled."),
            "apps" => new("Настройки установленных программ, чтобы они не мешали играм.", "Settings for installed apps so they stay out of your games' way."),
            _ => new("Рекомендовано для вашей системы.", "Recommended for your system."),
        };
    }
}

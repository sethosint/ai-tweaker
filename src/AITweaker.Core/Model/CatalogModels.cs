namespace AITweaker.Core.Model;

public sealed class TweakCategory
{
    public required string Id { get; init; }
    public required LocalizedText Name { get; init; }
    public string Icon { get; init; } = "";
}

/// <summary>A built-in Windows app package that can be removed in the Debloat tab.</summary>
public sealed class AppPackage
{
    /// <summary>Appx package name (wildcards allowed), e.g. <c>Microsoft.BingNews</c>.</summary>
    public required string Package { get; init; }
    public required LocalizedText Name { get; init; }
    public string Group { get; init; } = "other";
    /// <summary>Selected by default in the auto debloat.</summary>
    public bool Recommended { get; init; }
    /// <summary>Removing it breaks something people use (Store, Xbox, Phone Link...). Always ask.</summary>
    public bool Important { get; init; }
    public LocalizedText? Warning { get; init; }
    /// <summary>Hide or keep the package when this condition is true (e.g. Game Bar on dual-CCD X3D CPUs).</summary>
    public string? KeepWhen { get; init; }
    public LocalizedText? KeepReason { get; init; }
    /// <summary>winget / Store id to reinstall it later.</summary>
    public string? StoreId { get; init; }
}

public sealed class GuideSetting
{
    /// <summary>Setting label exactly as the app shows it (apps are mostly in English).</summary>
    public required string Name { get; init; }
    /// <summary>ON / OFF / free text value.</summary>
    public required string Value { get; init; }
    public LocalizedText? Note { get; init; }
}

public sealed class GuideSection
{
    public required LocalizedText Title { get; init; }
    /// <summary>Settings path inside the app, e.g. "User Settings → Game Overlay".</summary>
    public string Path { get; init; } = "";
    public string? Image { get; init; }
    public List<GuideSetting> Settings { get; init; } = [];
    public LocalizedText? Note { get; init; }
}

public sealed class AppGuide
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Icon { get; init; } = "";
    /// <summary>Fact that tells whether the app is installed, e.g. <c>app.discord</c>.</summary>
    public string? Detect { get; init; }
    public string? When { get; init; }
    /// <summary>"master" = recreated from the setup master's screenshots, "ai" = AI Tweaker's own recommendation.</summary>
    public string Source { get; init; } = "master";
    public LocalizedText? Summary { get; init; }
    public List<GuideSection> Sections { get; init; } = [];
    /// <summary>Catalog tweak ids that automate part of this guide.</summary>
    public List<string> AutoTweaks { get; init; } = [];
}

public sealed class BiosRecommendation
{
    public required string Id { get; init; }
    public required LocalizedText Title { get; init; }
    public required LocalizedText Detail { get; init; }
    /// <summary>Recommended value, e.g. "Enabled", "XMP I", "Disabled".</summary>
    public required string Value { get; init; }
    public string? When { get; init; }
    /// <summary>high | medium | low</summary>
    public string Priority { get; init; } = "medium";
    /// <summary>Menu path per board vendor: asus, msi, gigabyte, asrock, other.</summary>
    public Dictionary<string, string> Paths { get; init; } = [];
}

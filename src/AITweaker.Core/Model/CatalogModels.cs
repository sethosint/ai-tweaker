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

/// <summary>A program offered in the Install tab: installed silently with winget, or downloaded from its official site.</summary>
public sealed class InstallProgram
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    /// <summary>browsers | communication | gaming | media | tools | runtimes | look</summary>
    public string Group { get; init; } = "tools";
    /// <summary>winget package id (Microsoft's package manager), e.g. <c>Valve.Steam</c>.</summary>
    public string? Winget { get; init; }
    /// <summary>Installer arguments passed to winget instead of the package's own silent switches (e.g. to install only some components).</summary>
    public string? Override { get; init; }
    /// <summary>Direct link to an official portable zip (used when there is no winget package).</summary>
    public string? Download { get; init; }
    /// <summary>Official page, opened when neither winget nor the direct download works.</summary>
    public string? Url { get; init; }
    /// <summary>DisplayName fragments in "Apps &amp; features" ("|" = any of them) that mean it is already installed.</summary>
    public string? Detect { get; init; }
    public LocalizedText? Note { get; init; }
    public bool Recommended { get; init; }
    /// <summary>Paid software: the official installer/trial only — licensing is between the user and the author.</summary>
    public bool Paid { get; init; }
    /// <summary>The installer refuses to run elevated (e.g. Spotify), so it runs as the signed-in user.</summary>
    public bool User { get; init; }
    public string? When { get; init; }
}

/// <summary>A Chrome extension AI Tweaker installs through Chrome's ExtensionSettings policy (or the user opens it in the store).</summary>
public sealed class BrowserExtension
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    /// <summary>Chrome Web Store id: 32 letters a–p.</summary>
    public required string ExtensionId { get; init; }
    public string? MinVersion { get; init; }
    public LocalizedText? Note { get; init; }
    public string StoreUrl => "https://chromewebstore.google.com/detail/" + ExtensionId;
}

/// <summary>A "do you use this?" question: when the answer is no, its tweaks switch the feature off.</summary>
public sealed class FeatureQuestion
{
    public required string Id { get; init; }
    public required LocalizedText Name { get; init; }
    /// <summary>Fact that means the feature is in use (pre-ticks "I use it").</summary>
    public string? UsedWhen { get; init; }
    /// <summary>Security features: ticked "I use it" unless the user unticks it.</summary>
    public bool KeepByDefault { get; init; }
    public List<string> Tweaks { get; init; } = [];
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

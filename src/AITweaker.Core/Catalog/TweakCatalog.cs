using System.Reflection;
using System.Text.Json;
using AITweaker.Core.Model;

namespace AITweaker.Core.Catalog;

/// <summary>Loads the embedded data files (tweaks, debloat apps, app guides, BIOS advice, programs, browser extensions).</summary>
public sealed class TweakCatalog
{
    private const string Prefix = "AITweaker.Data.";

    public IReadOnlyList<TweakCategory> Categories { get; }
    public IReadOnlyList<TweakDefinition> Tweaks { get; }
    public IReadOnlyList<AppPackage> Apps { get; }
    public IReadOnlyList<AppGuide> Guides { get; }
    public IReadOnlyList<BiosRecommendation> Bios { get; }
    public IReadOnlyList<InstallProgram> Programs { get; }
    public IReadOnlyList<BrowserExtension> Extensions { get; }
    public IReadOnlyList<FeatureQuestion> Features { get; }

    private readonly Dictionary<string, TweakDefinition> _byId;

    private TweakCatalog(
        List<TweakCategory> categories, List<TweakDefinition> tweaks, List<AppPackage> apps,
        List<AppGuide> guides, List<BiosRecommendation> bios, List<InstallProgram> programs, List<BrowserExtension> extensions,
        List<FeatureQuestion> features)
    {
        Categories = categories;
        Tweaks = tweaks;
        Apps = apps;
        Guides = guides;
        Bios = bios;
        Programs = programs;
        Extensions = extensions;
        Features = features;
        _byId = new Dictionary<string, TweakDefinition>(StringComparer.Ordinal);
        foreach (var t in tweaks)
        {
            if (!_byId.TryAdd(t.Id, t)) throw new InvalidDataException($"Duplicate tweak id '{t.Id}'");
        }
    }

    public TweakDefinition? Find(string id) => _byId.GetValueOrDefault(id);

    private static TweakCatalog? _default;
    public static TweakCatalog Default => _default ??= LoadEmbedded();

    public static TweakCatalog LoadEmbedded()
    {
        var asm = typeof(TweakCatalog).Assembly;
        var names = asm.GetManifestResourceNames().Where(n => n.StartsWith(Prefix, StringComparison.Ordinal)).OrderBy(n => n, StringComparer.Ordinal).ToList();

        var tweaks = new List<TweakDefinition>();
        foreach (var name in names.Where(n => n.StartsWith(Prefix + "tweaks", StringComparison.Ordinal)))
        {
            tweaks.AddRange(Read<List<TweakDefinition>>(asm, name));
        }

        return new TweakCatalog(
            Read<List<TweakCategory>>(asm, Prefix + "categories.json"),
            tweaks,
            Read<List<AppPackage>>(asm, Prefix + "apps.json"),
            Read<List<AppGuide>>(asm, Prefix + "guides.json"),
            Read<List<BiosRecommendation>>(asm, Prefix + "bios.json"),
            Read<List<InstallProgram>>(asm, Prefix + "programs.json"),
            Read<List<BrowserExtension>>(asm, Prefix + "extensions.json"),
            Read<List<FeatureQuestion>>(asm, Prefix + "features.json"));
    }

    private static T Read<T>(Assembly asm, string resource)
    {
        using var stream = asm.GetManifestResourceStream(resource) ?? throw new FileNotFoundException($"Embedded resource {resource} missing");
        try
        {
            return JsonSerializer.Deserialize<T>(stream, JsonDefaults.Options) ?? throw new InvalidDataException($"{resource} is empty");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{resource}: {ex.Message}", ex);
        }
    }

    public static IEnumerable<string> ResourceNames() =>
        typeof(TweakCatalog).Assembly.GetManifestResourceNames().Where(n => n.StartsWith(Prefix, StringComparison.Ordinal));
}

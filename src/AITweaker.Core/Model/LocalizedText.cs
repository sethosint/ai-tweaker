using System.Text.Json.Serialization;

namespace AITweaker.Core.Model;

/// <summary>Bilingual UI string. Russian is the primary language, English the fallback.</summary>
public sealed record LocalizedText
{
    [JsonPropertyName("ru")] public string Ru { get; init; } = "";
    [JsonPropertyName("en")] public string En { get; init; } = "";

    public LocalizedText() { }

    public LocalizedText(string ru, string en)
    {
        Ru = ru;
        En = en;
    }

    public string Get(string lang) => lang == "en" ? (En.Length > 0 ? En : Ru) : (Ru.Length > 0 ? Ru : En);

    public override string ToString() => Ru;
}

using System.Text.Json;
using System.Text.Json.Serialization;
using AITweaker.Core.Catalog;
using AITweaker.Core.Engine;
using AITweaker.Core.Model;
using AITweaker.Core.Profile;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta.Messages;

namespace AITweaker.Core.Ai;

public sealed class AiRecommendation
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("reason")] public string Reason { get; set; } = "";
}

public sealed class AiAnalysis
{
    [JsonPropertyName("summary")] public string Summary { get; set; } = "";
    [JsonPropertyName("recommendations")] public List<AiRecommendation> Recommendations { get; set; } = [];
    [JsonPropertyName("avoid")] public List<AiRecommendation> Avoid { get; set; } = [];
    [JsonPropertyName("tips")] public List<string> Tips { get; set; } = [];
    /// <summary>Filled from the API response (not part of the JSON schema Claude answers with).</summary>
    [JsonPropertyName("model")] public string Model { get; set; } = "";
}

public sealed class AiAdvisorException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Optional Claude-powered consultant. Claude only ever picks tweak ids from our own catalog — it never
/// produces commands — so the AI cannot make the program do anything the catalog does not already allow.
/// </summary>
public sealed class ClaudeAdvisor(string apiKey, string model = ClaudeAdvisor.DefaultModel)
{
    public const string DefaultModel = "claude-opus-5-5";

    public static readonly string[] Models = ["claude-opus-5-5", "claude-sonnet-5-5", "claude-haiku-5-5"];

    private const string SystemPrompt =
        """
        You are the analysis engine inside "AI Tweaker", a Windows 11 optimization tool run by gamers who want
        more FPS and lower input/network latency. You receive the scanned hardware/software profile of one PC,
        the diagnostics the local engine already found, and the catalog of tweaks the tool can apply.

        Decide which catalog tweaks fit THIS machine best and which ones it should avoid. Be specific to the
        hardware: CPU vendor and core layout (hybrid P/E cores, multi-CCD X3D), laptop vs desktop, GPU vendor,
        disk type, RAM amount and speed, Wi-Fi vs Ethernet, installed apps, and the user's chosen profile.
        Prefer measurable latency/FPS wins over placebo; flag security trade-offs honestly. Only use tweak ids
        that appear in the catalog. Give each reason in one or two short sentences in the requested language.
        Tips may cover things the tool cannot do itself (BIOS, drivers, monitor, in-game settings).
        """;

    public async Task<AiAnalysis> AnalyzeAsync(SystemProfile profile, TunePlan plan, IReadOnlyList<Insight> insights, string lang, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) throw new AiAdvisorException("Claude API key is not set");

        var client = new AnthropicClient { ApiKey = apiKey };
        var input = BuildInput(profile, plan, insights, lang);

        BetaMessage response;
        try
        {
            response = await client.Beta.Messages.Create(new MessageCreateParams
            {
                Model = model,
                MaxTokens = 16000,
                Betas = ["server-side-fallback-2026-07-01"],
                Fallbacks = new Default(),
                OutputConfig = new BetaOutputConfig
                {
                    Effort = Effort.Medium,
                    Format = new BetaJsonOutputFormat { Schema = Schema() },
                },
                System = SystemPrompt,
                Messages = [new() { Role = Role.User, Content = input }],
            }, ct);
        }
        catch (AnthropicUnauthorizedException ex)
        {
            throw new AiAdvisorException(lang == "en" ? "Invalid Claude API key." : "Неверный API-ключ Claude.", ex);
        }
        catch (AnthropicRateLimitException ex)
        {
            throw new AiAdvisorException(lang == "en" ? "Rate limit reached, try again in a minute." : "Превышен лимит запросов, попробуйте через минуту.", ex);
        }
        catch (AnthropicApiException ex)
        {
            throw new AiAdvisorException((lang == "en" ? "Claude API error: " : "Ошибка Claude API: ") + ex.Message, ex);
        }
        catch (HttpRequestException ex)
        {
            throw new AiAdvisorException(lang == "en" ? "No connection to the Claude API." : "Нет соединения с Claude API.", ex);
        }

        if (response.StopReason == "refusal")
            throw new AiAdvisorException(lang == "en" ? "Claude declined this request." : "Claude отклонил запрос.");

        var text = string.Concat(response.Content.Select(b => b.Value).OfType<BetaTextBlock>().Select(t => t.Text));
        var analysis = Parse(text, plan);
        analysis.Model = response.Model.ToString();
        return analysis;
    }

    /// <summary>Parses Claude's JSON and drops any id that is not part of the plan for this machine.</summary>
    public static AiAnalysis Parse(string json, TunePlan plan)
    {
        AiAnalysis? a;
        try
        {
            a = JsonSerializer.Deserialize<AiAnalysis>(json);
        }
        catch (JsonException ex)
        {
            throw new AiAdvisorException("Claude returned malformed JSON", ex);
        }
        if (a is null) throw new AiAdvisorException("Claude returned an empty answer");

        var allowed = plan.Items.Select(i => i.Tweak.Id).ToHashSet(StringComparer.Ordinal);
        a.Recommendations = a.Recommendations.Where(r => allowed.Contains(r.Id)).DistinctBy(r => r.Id).ToList();
        a.Avoid = a.Avoid.Where(r => allowed.Contains(r.Id)).DistinctBy(r => r.Id).ToList();
        return a;
    }

    internal static string BuildInput(SystemProfile profile, TunePlan plan, IReadOnlyList<Insight> insights, string lang)
    {
        var payload = new
        {
            language = lang == "en" ? "English" : "Russian",
            profile = plan.Profile.ToString(),
            facts = profile.ToFacts().Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value),
            hardware = new
            {
                cpu = profile.Cpu.Name,
                gpus = profile.Gpus.Select(g => new { g.Name, g.DriverVersion, driverDate = g.DriverDate?.ToString("yyyy-MM-dd") }),
                ram = new { profile.Memory.TotalGb, profile.Memory.Type, speed = profile.Memory.ConfiguredSpeed, sticks = profile.Memory.Modules.Count },
                disks = profile.Disks.Select(d => new { d.Model, d.MediaType, d.BusType, d.IsSystem }),
                network = profile.Network.Select(n => new { n.Description, n.Kind, n.Connected, n.SpeedMbps }),
                displays = profile.Displays.Select(d => new { d.Name, d.Width, d.Height, d.RefreshHz, d.MaxRefreshHz }),
                board = $"{profile.Board.Manufacturer} {profile.Board.Product} (BIOS {profile.Board.BiosVersion})",
                os = $"{profile.Os.Caption} {profile.Os.DisplayVersion} build {profile.Os.Build}.{profile.Os.Ubr}",
            },
            diagnostics = insights.Select(i => new { i.Id, severity = i.Severity.ToString(), title = i.Title.En }),
            catalog = plan.Items.Select(i => new
            {
                id = i.Tweak.Id,
                category = i.Tweak.Category,
                name = i.Tweak.Name.En,
                risk = i.Tweak.Risk.ToString(),
                inProfile = i.Recommended,
                asksUser = i.NeedsConfirm,
            }),
        };
        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = false });
    }

    private static Dictionary<string, JsonElement> Schema()
    {
        var item = new
        {
            type = "object",
            properties = new { id = new { type = "string" }, reason = new { type = "string" } },
            required = new[] { "id", "reason" },
            additionalProperties = false,
        };
        return new Dictionary<string, JsonElement>
        {
            ["type"] = JsonSerializer.SerializeToElement("object"),
            ["properties"] = JsonSerializer.SerializeToElement(new
            {
                summary = new { type = "string" },
                recommendations = new { type = "array", items = item },
                avoid = new { type = "array", items = item },
                tips = new { type = "array", items = new { type = "string" } },
            }),
            ["required"] = JsonSerializer.SerializeToElement(new[] { "summary", "recommendations", "avoid", "tips" }),
            ["additionalProperties"] = JsonSerializer.SerializeToElement(false),
        };
    }
}

using AITweaker.Core.Ai;
using AITweaker.Core.Catalog;
using AITweaker.Core.Engine;
using AITweaker.Core.Model;

namespace AITweaker.Core.Tests;

public class ClaudeAdvisorTests
{
    private static readonly TunePlan Plan = new RecommendationEngine(TweakCatalog.LoadEmbedded()).BuildPlan(TestProfiles.Desktop(), TuneProfile.Performance);

    [Fact]
    public void ParseDropsUnknownAndDuplicateIds()
    {
        const string json = """
        {"summary":"ok","recommendations":[{"id":"gaming.hags","reason":"a"},{"id":"rm -rf","reason":"b"},{"id":"gaming.hags","reason":"c"}],
         "avoid":[{"id":"power.plan.laptop","reason":"not in plan"}],"tips":["enable xmp"]}
        """;
        var a = ClaudeAdvisor.Parse(json, Plan);
        Assert.Equal("ok", a.Summary);
        Assert.Equal(new[] { "gaming.hags" }, a.Recommendations.Select(r => r.Id));
        Assert.Empty(a.Avoid);
        Assert.Single(a.Tips);
    }

    [Fact]
    public void ParseRejectsGarbage() => Assert.Throws<AiAdvisorException>(() => ClaudeAdvisor.Parse("not json", Plan));

    [Fact]
    public void InputContainsCatalogAndHardware()
    {
        var input = ClaudeAdvisor.BuildInput(TestProfiles.Desktop(), Plan, InsightEngine.Analyze(TestProfiles.Desktop()), "ru");
        Assert.Contains("gaming.hags", input);
        Assert.Contains("RTX 4070", input);
        Assert.Contains("Russian", input);
    }

    [Fact]
    public async Task MissingKeyFailsFast() =>
        await Assert.ThrowsAsync<AiAdvisorException>(() => new ClaudeAdvisor("").AnalyzeAsync(TestProfiles.Desktop(), Plan, [], "en"));
}

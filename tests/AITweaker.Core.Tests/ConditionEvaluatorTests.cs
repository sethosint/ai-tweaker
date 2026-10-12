using AITweaker.Core.Conditions;

namespace AITweaker.Core.Tests;

public class ConditionEvaluatorTests
{
    private static readonly Dictionary<string, object?> Facts = new()
    {
        ["gpu.nvidia"] = true,
        ["system.laptop"] = false,
        ["ram.gb"] = 32.0,
        ["cpu.cores"] = 8,
        ["cpu.vendor"] = "AMD",
        ["cpu.name"] = "AMD Ryzen 7 7800X3D",
    };

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("gpu.nvidia", true)]
    [InlineData("!gpu.nvidia", false)]
    [InlineData("gpu.nvidia && !system.laptop", true)]
    [InlineData("system.laptop || ram.gb >= 32", true)]
    [InlineData("ram.gb > 32", false)]
    [InlineData("cpu.cores >= 4 && cpu.cores < 16", true)]
    [InlineData("cpu.vendor == \"amd\"", true)]
    [InlineData("cpu.vendor != 'Intel'", true)]
    [InlineData("cpu.name ~ 'x3d'", true)]
    [InlineData("!(gpu.nvidia && system.laptop)", true)]
    [InlineData("unknown.fact", false)]
    [InlineData("!unknown.fact", true)]
    [InlineData("unknown.fact == 1", false)]
    [InlineData("unknown.fact != 1", true)]
    [InlineData("gpu.nvidia && (system.laptop || cpu.cores == 8)", true)]
    public void Evaluates(string? expr, bool expected) => Assert.Equal(expected, ConditionEvaluator.Evaluate(expr, Facts));

    [Theory]
    [InlineData("gpu.nvidia &&")]
    [InlineData("(gpu.nvidia")]
    [InlineData("cpu.vendor == 'AMD")]
    [InlineData("ram.gb $ 4")]
    [InlineData("a b")]
    public void RejectsBadSyntax(string expr) => Assert.Throws<ConditionSyntaxException>(() => ConditionEvaluator.Parse(expr));

    [Fact]
    public void CollectsIdentifiers()
    {
        var ids = ConditionEvaluator.Identifiers("gpu.nvidia && (ram.gb >= 16 || !system.laptop) && cpu.vendor == 'AMD'");
        Assert.Equal(new[] { "cpu.vendor", "gpu.nvidia", "ram.gb", "system.laptop" }, ids.OrderBy(x => x));
    }
}

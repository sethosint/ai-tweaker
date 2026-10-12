using Anthropic.Models.Beta.Messages;

namespace AITweaker.Core.Tests;

public class ClaudeRequestTests
{
    [Fact]
    public void RefusalFallbackSerializesAsDefault()
    {
        var p = new MessageCreateParams
        {
            Model = "claude-opus-5-5",
            MaxTokens = 10,
            Fallbacks = new Default(),
            Messages = [new() { Role = Role.User, Content = "hi" }],
        };
        var s = p.ToString();
        Assert.Contains("\"fallbacks\"", s);
        Assert.Contains("default", s);
    }
}

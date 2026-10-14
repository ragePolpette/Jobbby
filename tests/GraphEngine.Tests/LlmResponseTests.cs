using System.Text.Json;
using Xunit;

namespace GraphEngine.Tests;

public class LlmResponseTests
{
    [Theory]
    [InlineData("```json\n{\"a\":1}\n```", "{\"a\":1}")]
    [InlineData("```\n{\"a\":1}\n```\nHope this helps", "{\"a\":1}")]
    [InlineData("  {\"a\":1}  ", "{\"a\":1}")]
    [InlineData("```{\"a\":1}```", "```{\"a\":1}```")]
    public void StripCodeFence_RemovesOneWrappingFence(string content, string expected)
    {
        Assert.Equal(expected, LlmResponse.StripCodeFence(content));
    }

    [Fact]
    public void Deserialize_MalformedJson_IsAnLlmException()
    {
        var error = Assert.Throws<LlmException>(() => LlmResponse.Deserialize<Dto>("nope", new JsonSerializerOptions(), "a dto"));

        Assert.Contains("a dto", error.Message);
        Assert.IsAssignableFrom<JsonException>(error.InnerException);
    }

    [Fact]
    public void Deserialize_JsonNull_IsAnLlmException()
    {
        Assert.Throws<LlmException>(() => LlmResponse.Deserialize<Dto>("null", new JsonSerializerOptions(), "a dto"));
    }

    [Fact]
    public async Task Decorator_StripsTheFence_OfAnyClient()
    {
        var client = new CodeFenceStrippingLlmClient(new MockLlmClient("```json\n[1]\n```"));

        Assert.Equal("[1]", await client.CompleteAsync("prompt"));
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("  abc  ", "abc")]
    [InlineData("abcdef", "abc")]
    public void Clamp_TrimsAndCaps(string? value, string expected)
    {
        Assert.Equal(expected, LlmResponse.Clamp(value, 3));
    }

    private sealed record Dto(int A);
}

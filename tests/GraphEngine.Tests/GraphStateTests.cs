using GraphEngine;
using Xunit;

namespace GraphEngine.Tests;

public class GraphStateTests
{
    [Fact]
    public void Get_ReturnsDefault_WhenKeyMissingOrWrongType()
    {
        var state = new GraphState();
        state.Set("name", "graph");

        Assert.Equal(0, state.Get<int>("missing"));
        Assert.Equal(0, state.Get<int>("name")); // wrong type -> default
        Assert.Equal("graph", state.Get<string>("name"));
    }

    [Fact]
    public void Snapshot_IsIndependentCopy()
    {
        var state = new GraphState();
        state.Set("count", 1);

        var snapshot = state.Snapshot();
        state.Set("count", 2);

        Assert.Equal(1, snapshot["count"]);
        Assert.Equal(2, state.Get<int>("count"));
    }
}

public class MockLlmClientTests
{
    [Fact]
    public async Task CompleteAsync_ReturnsFixedResponse_ByDefault()
    {
        var client = new MockLlmClient("hello");

        Assert.Equal("hello", await client.CompleteAsync("any prompt"));
        Assert.Equal("hello", await client.CompleteAsync("any other prompt"));
    }

    [Fact]
    public async Task CompleteAsync_DequeuesScriptedResponses_ThenFallsBackToDefault()
    {
        var client = new MockLlmClient(new[] { "first", "second" });

        Assert.Equal("first", await client.CompleteAsync("p1"));
        Assert.Equal("second", await client.CompleteAsync("p2"));
        Assert.Equal("mock response", await client.CompleteAsync("p3"));
    }
}

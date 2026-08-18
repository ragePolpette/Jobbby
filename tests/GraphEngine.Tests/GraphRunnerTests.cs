using GraphEngine;
using Xunit;

namespace GraphEngine.Tests;

public class GraphRunnerTests
{
    [Fact]
    public async Task RunAsync_TwoNodeCycle_TerminatesWhenRoutingReturnsEnd()
    {
        var runner = new GraphRunner(maxSteps: 20);

        runner.RegisterNode("A", new DelegateNode(state =>
        {
            var count = state.Get<int>("count");
            return NodeResult.From("count", count + 1);
        }));

        runner.RegisterNode("B", new DelegateNode(_ => NodeResult.Empty));

        runner.RegisterEdge("A", _ => "B");
        runner.RegisterEdge("B", state => state.Get<int>("count") >= 3 ? GraphRunner.End : "A");

        var state = new GraphState(new Dictionary<string, object> { ["count"] = 0 });

        var result = await runner.RunAsync("A", state);

        Assert.Equal(GraphRunStatus.Completed, result.Status);
        Assert.Equal(6, result.StepsExecuted); // A,B three times each
        Assert.Equal(3, result.FinalState.Get<int>("count"));
    }

    [Fact]
    public async Task RunAsync_RecordsHistoryForEveryStep()
    {
        var runner = new GraphRunner();

        runner.RegisterNode("Start", new DelegateNode(_ => NodeResult.Empty));
        runner.RegisterEdge("Start", _ => GraphRunner.End);

        var state = new GraphState();

        var result = await runner.RunAsync("Start", state);

        var step = Assert.Single(result.FinalState.History);
        Assert.Equal("Start", step.NodeName);
        Assert.Equal(GraphRunner.End, step.NextNode);
        Assert.Equal(0, step.StepNumber);
    }

    [Fact]
    public async Task RunAsync_InfiniteCycle_ThrowsWhenMaxStepsExceeded()
    {
        var runner = new GraphRunner(maxSteps: 5);

        runner.RegisterNode("A", new DelegateNode(_ => NodeResult.Empty));
        runner.RegisterNode("B", new DelegateNode(_ => NodeResult.Empty));

        // Neither edge ever returns END, so this graph would loop forever without the safety net.
        runner.RegisterEdge("A", _ => "B");
        runner.RegisterEdge("B", _ => "A");

        var state = new GraphState();

        var ex = await Assert.ThrowsAsync<GraphMaxStepsExceededException>(
            () => runner.RunAsync("A", state));

        Assert.Equal(5, ex.MaxSteps);
        Assert.Equal(5, state.History.Count);
    }

    [Fact]
    public async Task Steer_InjectsValueIntoStateBeforeNextNodeRuns()
    {
        var runner = new GraphRunner(maxSteps: 10);
        var injectionApplied = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        runner.RegisterNode("A", new DelegateNode(_ => NodeResult.Empty));
        runner.RegisterNode("B", new DelegateNode(state =>
        {
            if (state.TryGet<string>("external", out var value))
                injectionApplied.TrySetResult(value == "steered");

            return NodeResult.Empty;
        }));

        runner.RegisterEdge("A", _ => "B");
        runner.RegisterEdge("B", _ => GraphRunner.End);

        runner.Steer("external", "steered");

        var state = new GraphState();
        await runner.RunAsync("A", state);

        Assert.True(injectionApplied.Task.IsCompletedSuccessfully);
        Assert.True(await injectionApplied.Task);
    }
}

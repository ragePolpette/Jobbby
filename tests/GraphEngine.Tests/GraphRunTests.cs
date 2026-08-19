using GraphEngine;
using Xunit;

namespace GraphEngine.Tests;

public class GraphRunTests
{
    [Fact]
    public async Task RunAsync_TwoNodeCycle_TerminatesWhenRoutingReturnsEnd()
    {
        var definition = new GraphDefinition(maxSteps: 20);

        definition.RegisterNode("A", new DelegateNode(state =>
        {
            var count = state.Get<int>("count");
            return NodeResult.From("count", count + 1);
        }));

        definition.RegisterNode("B", new DelegateNode(_ => NodeResult.Empty));

        definition.RegisterEdge("A", _ => "B");
        definition.RegisterEdge("B", state => state.Get<int>("count") >= 3 ? GraphDefinition.End : "A");

        var state = new GraphState(new Dictionary<string, object> { ["count"] = 0 });

        var result = await definition.CreateRun().RunAsync("A", state);

        Assert.Equal(GraphRunStatus.Completed, result.Status);
        Assert.Equal(6, result.StepsExecuted); // A,B three times each
        Assert.Equal(3, result.FinalState.Get<int>("count"));
    }

    [Fact]
    public async Task RunAsync_RecordsHistoryForEveryStep()
    {
        var definition = new GraphDefinition();

        definition.RegisterNode("Start", new DelegateNode(_ => NodeResult.Empty));
        definition.RegisterEdge("Start", _ => GraphDefinition.End);

        var state = new GraphState();

        var result = await definition.CreateRun().RunAsync("Start", state);

        var step = Assert.Single(result.FinalState.History);
        Assert.Equal("Start", step.NodeName);
        Assert.Equal(GraphDefinition.End, step.NextNode);
        Assert.Equal(0, step.StepNumber);
    }

    [Fact]
    public async Task RunAsync_InfiniteCycle_ThrowsWhenMaxStepsExceeded()
    {
        var definition = new GraphDefinition(maxSteps: 5);

        definition.RegisterNode("A", new DelegateNode(_ => NodeResult.Empty));
        definition.RegisterNode("B", new DelegateNode(_ => NodeResult.Empty));

        // Neither edge ever returns END, so this graph would loop forever without the safety net.
        definition.RegisterEdge("A", _ => "B");
        definition.RegisterEdge("B", _ => "A");

        var state = new GraphState();

        var ex = await Assert.ThrowsAsync<GraphMaxStepsExceededException>(
            () => definition.CreateRun().RunAsync("A", state));

        Assert.Equal(5, ex.MaxSteps);
        Assert.Equal(5, state.History.Count);
    }

    [Fact]
    public async Task Steer_InjectsValueIntoStateBeforeNextNodeRuns()
    {
        var definition = new GraphDefinition(maxSteps: 10);
        var injectionApplied = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        definition.RegisterNode("A", new DelegateNode(_ => NodeResult.Empty));
        definition.RegisterNode("B", new DelegateNode(state =>
        {
            if (state.TryGet<string>("external", out var value))
                injectionApplied.TrySetResult(value == "steered");

            return NodeResult.Empty;
        }));

        definition.RegisterEdge("A", _ => "B");
        definition.RegisterEdge("B", _ => GraphDefinition.End);

        var run = definition.CreateRun();
        run.Steer("external", "steered");

        var state = new GraphState();
        await run.RunAsync("A", state);

        Assert.True(injectionApplied.Task.IsCompletedSuccessfully);
        Assert.True(await injectionApplied.Task);
    }

    /// <summary>Loops until "owner" is present in its own state, waiting a beat each
    /// iteration so external Steer() calls have time to land while it spins.</summary>
    private sealed class WaitForOwnerNode : INode
    {
        public async Task<NodeResult> ExecuteAsync(GraphState state)
        {
            await Task.Delay(10);
            state.TryGet<string>("owner", out var owner);
            return NodeResult.From("observedOwner", owner ?? string.Empty);
        }
    }

    [Fact]
    public async Task ConcurrentRuns_OfSameDefinition_DoNotShareSteeringState()
    {
        // Two runs of the same definition, spinning on the same node concurrently, each
        // waiting for its own steered "owner" value before finishing. If Steer() state
        // leaked between runs, one run could observe the other's injected value.
        var definition = new GraphDefinition(maxSteps: 200);

        definition.RegisterNode("Wait", new WaitForOwnerNode());
        definition.RegisterEdge("Wait", state => state.ContainsKey("owner") ? GraphDefinition.End : "Wait");

        var runA = definition.CreateRun();
        var runB = definition.CreateRun();

        var stateA = new GraphState();
        var stateB = new GraphState();

        var taskA = runA.RunAsync("Wait", stateA);
        var taskB = runB.RunAsync("Wait", stateB);

        await Task.Delay(30);
        runB.Steer("owner", "B");

        await Task.Delay(30);
        runA.Steer("owner", "A");

        await Task.WhenAll(taskA, taskB);

        Assert.Equal("A", stateA.Get<string>("owner"));
        Assert.Equal("A", stateA.Get<string>("observedOwner"));

        Assert.Equal("B", stateB.Get<string>("owner"));
        Assert.Equal("B", stateB.Get<string>("observedOwner"));
    }
}

using GraphEngine;

namespace Example;

/// <summary>
/// Minimal validation graph: two nodes, "Ping" and "Pong", pass control back and forth.
/// Each Ping -> Pong hop is one "round"; after 3 rounds the Pong node's edge routes to
/// <see cref="GraphRunner.End"/> instead of back to Ping. This exists purely to prove
/// that conditional routing and cycles work end-to-end in the engine - it carries no
/// domain logic of its own.
/// </summary>
public static class PingPongExample
{
    private const int TotalRounds = 3;

    private sealed class PingNode : INode
    {
        public Task<NodeResult> ExecuteAsync(GraphState state)
        {
            var round = state.Get<int>("round");
            Console.WriteLine($"[Ping] starting round {round + 1}/{TotalRounds}");
            return Task.FromResult(NodeResult.From("lastSpeaker", "Ping"));
        }
    }

    private sealed class PongNode : INode
    {
        public Task<NodeResult> ExecuteAsync(GraphState state)
        {
            var completedRound = state.Get<int>("round") + 1;
            Console.WriteLine($"[Pong] completing round {completedRound}/{TotalRounds}");

            return Task.FromResult(NodeResult.From(new Dictionary<string, object>
            {
                ["lastSpeaker"] = "Pong",
                ["round"] = completedRound,
            }));
        }
    }

    public static GraphRunner BuildRunner(int maxSteps = 50)
    {
        var runner = new GraphRunner(maxSteps);

        runner.RegisterNode("Ping", new PingNode());
        runner.RegisterNode("Pong", new PongNode());

        runner.RegisterEdge("Ping", _ => "Pong");
        runner.RegisterEdge("Pong", state =>
            state.Get<int>("round") >= TotalRounds ? GraphRunner.End : "Ping");

        return runner;
    }

    public static Task<GraphRunResult> RunAsync()
    {
        var runner = BuildRunner();
        var state = new GraphState(new Dictionary<string, object> { ["round"] = 0 });
        return runner.RunAsync("Ping", state);
    }
}

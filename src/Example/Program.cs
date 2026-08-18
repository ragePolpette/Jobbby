using Example;

var result = await PingPongExample.RunAsync();

Console.WriteLine();
Console.WriteLine($"Finished after {result.StepsExecuted} steps, status: {result.Status}");
Console.WriteLine($"Final round count: {result.FinalState.Get<int>("round")}");

Console.WriteLine();
Console.WriteLine("Step history:");
foreach (var step in result.FinalState.History)
{
    Console.WriteLine(
        $"  #{step.StepNumber} {step.NodeName,-4} @ {step.Timestamp:O} -> next: {step.NextNode}");
}

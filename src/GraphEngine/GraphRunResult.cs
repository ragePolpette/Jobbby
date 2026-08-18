namespace GraphEngine;

public enum GraphRunStatus
{
    Completed
}

/// <summary>Outcome of a completed graph run.</summary>
public sealed record GraphRunResult(GraphState FinalState, int StepsExecuted, GraphRunStatus Status);

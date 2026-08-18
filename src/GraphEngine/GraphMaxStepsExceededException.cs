namespace GraphEngine;

/// <summary>
/// Thrown when a graph run reaches its configured max_steps without routing to
/// <see cref="Edge.End"/>. This is the safety net against runaway/infinite cycles.
/// </summary>
public sealed class GraphMaxStepsExceededException : Exception
{
    public int MaxSteps { get; }

    public GraphMaxStepsExceededException(int maxSteps)
        : base($"Graph execution exceeded the configured max_steps ({maxSteps}) without reaching '{Edge.End}'.")
    {
        MaxSteps = maxSteps;
    }
}

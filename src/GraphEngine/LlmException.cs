namespace GraphEngine;

public sealed class LlmException : Exception
{
    public LlmException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

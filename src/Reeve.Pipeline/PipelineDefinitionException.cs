namespace Reeve.Pipeline;

/// <summary>A pipeline that cannot be resolved. Thrown by <see cref="Pipeline{TContext}.Resolve"/>, never by
/// a tick.</summary>
public sealed class PipelineDefinitionException : Exception
{
    /// <summary>Creates the exception with the message a caller is shown.</summary>
    public PipelineDefinitionException(string message)
        : base(message)
    {
    }
}

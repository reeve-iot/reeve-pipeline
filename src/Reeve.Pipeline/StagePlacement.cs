namespace Reeve.Pipeline;

/// <summary>Where a stage sits relative to another. The order comes from these declarations, never from the
/// order in which stages are passed to <see cref="Pipeline{TContext}.Resolve"/>.</summary>
public sealed record StagePlacement
{
    private StagePlacement(string? afterId, string? beforeId, int ordinal)
    {
        AfterId = afterId;
        BeforeId = beforeId;
        Ordinal = ordinal;
    }

    /// <summary>The stage this one follows, or null.</summary>
    public string? AfterId { get; }

    /// <summary>The stage this one precedes, or null.</summary>
    public string? BeforeId { get; }

    /// <summary>Breaks ties between stages otherwise equally free to run. Lower goes first; equal ordinals
    /// fall back to the id, so the order never depends on the order the stages were handed over.</summary>
    public int Ordinal { get; }

    /// <summary>True for the head of the pipeline.</summary>
    public bool IsFirst => AfterId is null && BeforeId is null;

    /// <summary>The head. Exactly one stage may declare it.</summary>
    public static StagePlacement First { get; } = new(null, null, 0);

    /// <summary>Sits after <paramref name="id"/>.</summary>
    public static StagePlacement After(string id, int ordinal = 0) => new(Required(id), null, ordinal);

    /// <summary>Sits before <paramref name="id"/>.</summary>
    public static StagePlacement Before(string id, int ordinal = 0) => new(null, Required(id), ordinal);

    private static string Required(string id) =>
        string.IsNullOrWhiteSpace(id)
            ? throw new ArgumentException("A placement anchor must name a stage.", nameof(id))
            : id;
}

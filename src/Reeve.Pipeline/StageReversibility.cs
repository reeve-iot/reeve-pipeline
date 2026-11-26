namespace Reeve.Pipeline;

/// <summary>Whether entering a stage forfeits the run's ability to unwind, and what to tell an operator
/// when it does.</summary>
public sealed record StageReversibility
{
    private StageReversibility(bool isReversible, string? operatorReason)
    {
        IsReversible = isReversible;
        OperatorReason = operatorReason;
    }

    /// <summary>Whether a run that entered this stage may still be compensated.</summary>
    public bool IsReversible { get; }

    /// <summary>Why the run cannot be unwound past this stage; null when it can.</summary>
    public string? OperatorReason { get; }

    /// <summary>The default: entering this stage forfeits nothing.</summary>
    public static StageReversibility Reversible { get; } = new(true, null);

    /// <summary>Entering this stage makes the run forward-only. The reason is required and is quoted
    /// verbatim wherever a refusal is reported.</summary>
    public static StageReversibility IrreversibleOnceEntered(string operatorReason)
    {
        if (string.IsNullOrWhiteSpace(operatorReason))
        {
            throw new ArgumentException(
                "An irreversible stage must say why, because the reason is what an operator is shown.",
                nameof(operatorReason));
        }

        return new StageReversibility(false, operatorReason);
    }
}

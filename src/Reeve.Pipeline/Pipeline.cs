namespace Reeve.Pipeline;

/// <summary>A resolved, ordered set of stages. Built once, at startup, so a mistake in the placements
/// fails there rather than on a live run.</summary>
/// <typeparam name="TContext">Whatever the caller's stages need.</typeparam>
public sealed class Pipeline<TContext>
{
    private readonly Dictionary<string, IStage<TContext>> _byId;

    private Pipeline(IReadOnlyList<string> order, Dictionary<string, IStage<TContext>> byId)
    {
        Order = order;
        _byId = byId;

        foreach (var id in order.Where(id => !byId[id].Reversibility.IsReversible))
        {
            FirstIrreversibleStageId = id;
            FirstIrreversibleReason = byId[id].Reversibility.OperatorReason;
            break;
        }
    }

    /// <summary>The stage ids, in the order they run forward.</summary>
    public IReadOnlyList<string> Order { get; }

    /// <summary>The first stage that makes the run forward-only, or null when none does. Known before
    /// anything has run, so a caller can warn about the point of no return in advance.</summary>
    public string? FirstIrreversibleStageId { get; }

    /// <summary>Why that stage is irreversible; null when there is none.</summary>
    public string? FirstIrreversibleReason { get; }

    /// <summary>The stage with this id, or null.</summary>
    public IStage<TContext>? Find(string id) => _byId.GetValueOrDefault(id);

    /// <summary>Turns declared placements into a total order.</summary>
    /// <exception cref="PipelineDefinitionException">The set is empty, has a stage without an id, names a
    /// duplicate id, anchors on a stage that is not there, declares other than exactly one head, or contains
    /// a cycle.</exception>
    public static Pipeline<TContext> Resolve(IEnumerable<IStage<TContext>> stages)
    {
        ArgumentNullException.ThrowIfNull(stages);

        var all = stages.ToList();
        if (all.Count == 0)
        {
            throw new PipelineDefinitionException("A pipeline needs at least one stage.");
        }

        var byId = new Dictionary<string, IStage<TContext>>(StringComparer.Ordinal);
        foreach (var stage in all)
        {
            if (string.IsNullOrWhiteSpace(stage.Id))
            {
                throw new PipelineDefinitionException("A stage must have an id.");
            }

            if (!byId.TryAdd(stage.Id, stage))
            {
                throw new PipelineDefinitionException($"Two stages share the id '{stage.Id}'.");
            }
        }

        var head = SoleHead(all);
        var (successors, indegree) = BuildEdges(all, byId, head);
        var order = Walk(byId, successors, indegree, head);

        if (order.Count != byId.Count)
        {
            var stuck = byId.Keys.Except(order).OrderBy(id => id, StringComparer.Ordinal);
            throw new PipelineDefinitionException(
                $"The placements form a cycle; these stages can never run: {string.Join(", ", stuck)}.");
        }

        return new Pipeline<TContext>(order, byId);
    }

    private static string SoleHead(List<IStage<TContext>> all)
    {
        var heads = all.Where(s => s.Placement.IsFirst)
            .Select(s => s.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        return heads.Count switch
        {
            1 => heads[0],
            0 => throw new PipelineDefinitionException(
                "No stage declares StagePlacement.First, so the pipeline has no head."),
            _ => throw new PipelineDefinitionException(
                $"More than one stage declares StagePlacement.First: {string.Join(", ", heads)}."),
        };
    }

    private static (Dictionary<string, HashSet<string>> Successors, Dictionary<string, int> Indegree) BuildEdges(
        List<IStage<TContext>> all, Dictionary<string, IStage<TContext>> byId, string head)
    {
        var successors = byId.Keys.ToDictionary(
            id => id, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
        var indegree = byId.Keys.ToDictionary(id => id, _ => 0, StringComparer.Ordinal);

        void Link(string before, string after)
        {
            if (successors[before].Add(after))
            {
                indegree[after]++;
            }
        }

        foreach (var stage in all)
        {
            var anchor = stage.Placement.AfterId ?? stage.Placement.BeforeId;
            if (anchor is null)
            {
                continue;
            }

            if (!byId.ContainsKey(anchor))
            {
                throw new PipelineDefinitionException(
                    $"Stage '{stage.Id}' is placed against '{anchor}', which is not in this pipeline.");
            }

            if (stage.Placement.AfterId is not null)
            {
                Link(anchor, stage.Id);
            }
            else
            {
                Link(stage.Id, anchor);
            }
        }

        var unconstrained = byId.Keys
            .Where(id => id != head && indegree[id] == 0)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
        foreach (var id in unconstrained)
        {
            Link(head, id);
        }

        return (successors, indegree);
    }

    private static List<string> Walk(
        Dictionary<string, IStage<TContext>> byId,
        Dictionary<string, HashSet<string>> successors,
        Dictionary<string, int> indegree,
        string head)
    {
        var order = new List<string>(byId.Count);
        var ready = new List<string> { head };

        while (ready.Count > 0)
        {
            ready.Sort((left, right) =>
            {
                var byOrdinal = byId[left].Placement.Ordinal.CompareTo(byId[right].Placement.Ordinal);
                return byOrdinal != 0 ? byOrdinal : string.CompareOrdinal(left, right);
            });

            var next = ready[0];
            ready.RemoveAt(0);
            order.Add(next);

            foreach (var successor in successors[next].OrderBy(id => id, StringComparer.Ordinal))
            {
                if (--indegree[successor] == 0)
                {
                    ready.Add(successor);
                }
            }
        }

        return order;
    }
}

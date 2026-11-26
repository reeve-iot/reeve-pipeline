using FluentAssertions;

namespace Reeve.Pipeline.Tests;

public sealed class ResolveTests
{
    private static ScriptedStage Stage(string id, StagePlacement placement) => new(id, placement);

    [Fact]
    public void A_chain_of_After_resolves_to_its_declared_order()
    {
        var pipeline = Pipeline<TestContext>.Resolve(
        [
            Stage("c", StagePlacement.After("b")),
            Stage("a", StagePlacement.First),
            Stage("b", StagePlacement.After("a")),
        ]);

        pipeline.Order.Should().Equal(
            ["a", "b", "c"],
            "order comes from the placements, not from the order the stages were handed over");
    }

    [Fact]
    public void Before_puts_a_stage_ahead_of_its_anchor()
    {
        var pipeline = Pipeline<TestContext>.Resolve(
        [
            Stage("a", StagePlacement.First),
            Stage("c", StagePlacement.After("a")),
            Stage("b", StagePlacement.Before("c")),
        ]);

        pipeline.Order.Should().Equal("a", "b", "c");
    }

    [Fact]
    public void Two_stages_after_the_same_anchor_are_ordered_by_ordinal()
    {
        var pipeline = Pipeline<TestContext>.Resolve(
        [
            Stage("a", StagePlacement.First),
            Stage("z", StagePlacement.After("a", ordinal: 1)),
            Stage("y", StagePlacement.After("a", ordinal: 2)),
        ]);

        pipeline.Order.Should().Equal(
            ["a", "z", "y"],
            "the ordinal decides, not the id and not the registration");
    }

    [Fact]
    public void Equal_ordinals_fall_back_to_the_id_so_the_order_is_never_arbitrary()
    {
        var pipeline = Pipeline<TestContext>.Resolve(
        [
            Stage("a", StagePlacement.First),
            Stage("z", StagePlacement.After("a")),
            Stage("y", StagePlacement.After("a")),
        ]);

        pipeline.Order.Should().Equal("a", "y", "z");
    }

    [Fact]
    public void Find_returns_the_stage_and_null_for_one_it_does_not_have()
    {
        var pipeline = Pipeline<TestContext>.Resolve([Stage("a", StagePlacement.First)]);

        pipeline.Find("a")!.Id.Should().Be("a");
        pipeline.Find("nope").Should().BeNull();
    }

    [Fact]
    public void An_empty_pipeline_is_refused()
    {
        var act = () => Pipeline<TestContext>.Resolve([]);

        act.Should().Throw<PipelineDefinitionException>().WithMessage("*at least one stage*");
    }

    [Fact]
    public void A_duplicate_id_is_refused_and_named()
    {
        var act = () => Pipeline<TestContext>.Resolve(
        [
            Stage("a", StagePlacement.First),
            Stage("a", StagePlacement.After("a")),
        ]);

        act.Should().Throw<PipelineDefinitionException>().WithMessage("*share the id 'a'*");
    }

    [Fact]
    public void An_unknown_anchor_is_refused_and_named()
    {
        var act = () => Pipeline<TestContext>.Resolve(
        [
            Stage("a", StagePlacement.First),
            Stage("b", StagePlacement.After("ghost")),
        ]);

        act.Should().Throw<PipelineDefinitionException>().WithMessage("*'ghost'*not in this pipeline*");
    }

    [Fact]
    public void No_head_is_refused()
    {
        var act = () => Pipeline<TestContext>.Resolve([Stage("a", StagePlacement.After("a"))]);

        act.Should().Throw<PipelineDefinitionException>().WithMessage("*no head*");
    }

    [Fact]
    public void Two_heads_are_refused_and_both_are_named()
    {
        var act = () => Pipeline<TestContext>.Resolve(
        [
            Stage("a", StagePlacement.First),
            Stage("b", StagePlacement.First),
        ]);

        act.Should().Throw<PipelineDefinitionException>().WithMessage("*More than one*a, b*");
    }

    [Fact]
    public void A_cycle_is_refused_and_its_stages_are_named()
    {
        var act = () => Pipeline<TestContext>.Resolve(
        [
            Stage("a", StagePlacement.First),
            Stage("b", StagePlacement.After("c")),
            Stage("c", StagePlacement.After("b")),
        ]);

        act.Should().Throw<PipelineDefinitionException>().WithMessage("*cycle*b, c*");
    }
}

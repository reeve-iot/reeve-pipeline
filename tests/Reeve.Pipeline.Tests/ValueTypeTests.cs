using FluentAssertions;

namespace Reeve.Pipeline.Tests;

public sealed class ValueTypeTests
{
    [Fact]
    public void A_failed_outcome_carries_its_reason()
    {
        var outcome = StageOutcome.Failed("the registry refused");

        outcome.Kind.Should().Be(StageOutcomeKind.Failed);
        outcome.Detail.Should().Be("the registry refused",
            because: "a failure a caller cannot explain is the defect this library exists to fix");
    }

    [Fact]
    public void A_failure_without_a_reason_is_refused()
    {
        var act = () => StageOutcome.Failed("   ");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void An_irreversible_stage_must_say_why()
    {
        var act = () => StageReversibility.IrreversibleOnceEntered(" ");

        act.Should().Throw<ArgumentException>(
            because: "a refusal an operator cannot read is what this replaces");
    }

    [Fact]
    public void A_budget_with_no_limit_is_never_spent()
    {
        StageBudget.None.Limit.Should().BeNull();
    }

    [Fact]
    public void A_budget_refuses_a_non_positive_limit()
    {
        var act = () => StageBudget.Of(TimeSpan.Zero, StageTimeoutPolicy.FailAndCompensate);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void First_is_the_only_placement_with_no_anchor()
    {
        StagePlacement.First.IsFirst.Should().BeTrue();
        StagePlacement.After("a").IsFirst.Should().BeFalse();
        StagePlacement.Before("a").IsFirst.Should().BeFalse();
    }

    [Fact]
    public void An_anchor_must_be_a_real_id()
    {
        var act = () => StagePlacement.After("");

        act.Should().Throw<ArgumentException>();
    }
}

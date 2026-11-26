using FluentAssertions;

namespace Reeve.Pipeline.Tests;

public sealed class JournalTests
{
    [Fact]
    public void A_new_journal_is_empty_running_and_forward()
    {
        var journal = PipelineJournal.New();

        journal.Entries.Should().BeEmpty();
        journal.Direction.Should().Be(PipelineDirection.Forward);
        journal.State.Should().Be(PipelineState.Running);
    }

    [Fact]
    public void Only_Running_is_not_terminal()
    {
        PipelineJournal.New().IsTerminal.Should().BeFalse();

        foreach (var state in new[] { PipelineState.Completed, PipelineState.Compensated, PipelineState.Failed })
        {
            (PipelineJournal.New() with { State = state }).IsTerminal
                .Should().BeTrue(because: $"{state} is an outcome, not a position");
        }
    }
}

using ApplicationLedger;
using GraphEngine;
using Host.Nodes;

namespace Host.Tests;

public class RunSummaryTextTests
{
    [Theory]
    [InlineData(ApplicationOutcomes.Shortlisted, "Selezionato automaticamente (confidenza 0.82).", "Selezionato automaticamente (confidenza 0.82).")]
    [InlineData(ApplicationOutcomes.Pending, "Da decidere (confidenza 0.50 sotto la soglia 0.70).", "Da decidere (confidenza 0.50 sotto la soglia 0.70).")]
    [InlineData(ApplicationOutcomes.Pending, "informazioni insufficienti", "da decidere (informazioni insufficienti)")]
    [InlineData(ApplicationOutcomes.AutoRejected, "retribuzione sotto il minimo", "scartato automaticamente (retribuzione sotto il minimo)")]
    public void DescribeOutcome_DoesNotRepeatTheOutcomeAlreadyInTheReason(string outcome, string reason, string expected)
    {
        var state = new GraphState();
        state.Set(RecordOutcomeNode.OutcomeStateKey, outcome);
        state.Set(RecordOutcomeNode.ReasonStateKey, reason);

        Assert.Equal(expected, RunSummaryText.DescribeOutcome(state));
    }
}

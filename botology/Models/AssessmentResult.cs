namespace botology.Models;

public sealed record AssessmentResult(AssessmentSeverity Severity, string Summary, string Details)
{
    internal string? UiSummaryKey { get; init; }
    internal object[] UiSummaryArguments { get; init; }=[];
    internal AssessmentResult? UiUnderlyingAssessment { get; init; }

    // Presentation metadata must not change the record's original domain equality.
    public bool Equals(AssessmentResult? other)
        => other is not null && Severity==other.Severity && Summary==other.Summary && Details==other.Details;
    public override int GetHashCode()
    {
        var value=EqualityComparer<Type>.Default.GetHashCode(EqualityContract);
        value=unchecked(value*-1521134295+EqualityComparer<AssessmentSeverity>.Default.GetHashCode(Severity));
        value=unchecked(value*-1521134295+EqualityComparer<string>.Default.GetHashCode(Summary));
        return unchecked(value*-1521134295+EqualityComparer<string>.Default.GetHashCode(Details));
    }
}

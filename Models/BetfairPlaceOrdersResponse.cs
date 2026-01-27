namespace BetfairReplicator.Models;

public class PlaceExecutionReport
{
    public string? customerRef { get; set; }
    public string? status { get; set; }                 // SUCCESS / FAILURE / PROCESSED_WITH_ERRORS
    public string? errorCode { get; set; }              // es. INSUFFICIENT_FUNDS
    public string? marketId { get; set; }
    public List<PlaceInstructionReport>? instructionReports { get; set; }
}

public class PlaceInstructionReport
{
    public string? status { get; set; }                 // SUCCESS / FAILURE
    public string? errorCode { get; set; }
    public PlaceInstructionReportResult? instruction { get; set; }
    public PlaceInstructionReportOutcome? outcome { get; set; }
    public string? betId { get; set; }
    public double? placedDate { get; set; }            // a volte non c'è, dipende dalla response
    public double? averagePriceMatched { get; set; }
    public double? sizeMatched { get; set; }
}

public class PlaceInstructionReportResult
{
    public long selectionId { get; set; }
    public string? side { get; set; }                  // BACK / LAY
    public string? orderType { get; set; }
}

public class PlaceInstructionReportOutcome
{
    public string? status { get; set; }
}

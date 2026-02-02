namespace BetfairReplicator.Models;

public class ListCurrentOrdersParams
{
    public List<string>? betIds { get; set; }
    public List<string>? marketIds { get; set; }
    public int? recordCount { get; set; }
}

public class CurrentOrderSummaryReport
{
    public List<CurrentOrderSummary>? currentOrders { get; set; }
}

public class CurrentOrderSummary
{
    public string? betId { get; set; }
    public string? marketId { get; set; }
    public long? selectionId { get; set; }
    public string? side { get; set; }          // BACK / LAY
    public string? status { get; set; }        // EXECUTABLE / EXECUTION_COMPLETE
    public double? priceSize { get; set; }     // non sempre presente: dipende dalla response
    public double? sizeRemaining { get; set; }
    public DateTime? placedDate { get; set; }

    public double? sizeMatched { get; set; }
    public double? averagePriceMatched { get; set; }
}

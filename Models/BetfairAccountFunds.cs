namespace BetfairReplicator.Models;

public class BetfairAccountFundsParams
{
    public string? wallet { get; set; } // null = default wallet
}

public class BetfairAccountFundsResult
{
    public double? availableToBetBalance { get; set; }
    public double? exposure { get; set; }
    public double? retainedCommission { get; set; }
    public double? exposureLimit { get; set; }
}

namespace BetfairReplicator.Options;

public class BetfairOptions
{
    public List<BetfairAccountOptions> Accounts { get; set; } = new();
}

public class BetfairAccountOptions
{
    public string DisplayName { get; set; } = "";
    public string AppKeyDelayed { get; set; } = "";
}

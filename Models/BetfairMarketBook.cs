namespace BetfairReplicator.Models;

public class ListMarketBookParams
{
    public List<string> marketIds { get; set; } = new();
    public PriceProjection priceProjection { get; set; } = new();
}

public class PriceProjection
{
    public HashSet<string> priceData { get; set; } = new() { "EX_BEST_OFFERS" }; // migliori quote
}

public class MarketBook
{
    public string? marketId { get; set; }
    public bool? isMarketDataDelayed { get; set; }
    public string? status { get; set; }
    public List<RunnerBook>? runners { get; set; }

    public bool? inplay { get; set; }

}

public class RunnerBook
{
    public long selectionId { get; set; }
    public ExchangePrices? ex { get; set; }
}

public class ExchangePrices
{
    public List<PriceSize>? availableToBack { get; set; }
    public List<PriceSize>? availableToLay { get; set; }
}

public class PriceSize
{
    public double price { get; set; }
    public double size { get; set; }
}

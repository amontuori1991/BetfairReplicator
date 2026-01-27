namespace BetfairReplicator.Models;

public class PlaceOrdersParams
{
    public string marketId { get; set; } = "";
    public List<PlaceInstruction> instructions { get; set; } = new();
    public string? customerRef { get; set; } // opzionale
}

public class PlaceInstruction
{
    public long selectionId { get; set; }
    public string side { get; set; } = "BACK"; // BACK o LAY
    public string orderType { get; set; } = "LIMIT";
    public LimitOrder limitOrder { get; set; } = new();
}

public class LimitOrder
{
    public double size { get; set; }        // stake
    public double price { get; set; }       // quota
    public string persistenceType { get; set; } = "LAPSE"; // LAPSE / PERSIST / MARKET_ON_CLOSE
}

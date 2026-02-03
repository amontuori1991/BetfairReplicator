using System.Reflection;
using System.Text.Json.Serialization;
namespace BetfairReplicator.Models;

// ----- listEventTypes -----
public class EventTypeResult
{
    public EventType? eventType { get; set; }
    public int marketCount { get; set; }
}
public class EventType
{
    public string? id { get; set; }
    public string? name { get; set; }
}

// ----- listEvents -----
public class ListEventsParams
{
    public MarketFilter filter { get; set; } = new();
}


public class EventResult
{
    [JsonPropertyName("event")]
    public BetfairEvent? Event { get; set; }

    public int marketCount { get; set; }
}


public class BetfairEvent
{
    public string? id { get; set; }
    public string? name { get; set; }
    public DateTime? openDate { get; set; }
}

// ----- listMarketCatalogue -----
public class ListMarketCatalogueParams
{
    public MarketFilter filter { get; set; } = new();
    public HashSet<string> marketProjection { get; set; } = new(); // RUNNER_DESCRIPTION, EVENT, MARKET_START_TIME
    public string sort { get; set; } = "FIRST_TO_START";
    public int maxResults { get; set; } = 50;
}
public class MarketCatalogue
{
    public string? marketId { get; set; }
    public string? marketName { get; set; }
    public DateTime? marketStartTime { get; set; }
    public List<RunnerCatalogue>? runners { get; set; }
    public EventInfo? @event { get; set; }

}
public class EventInfo
{
    public string? id { get; set; }
    public string? name { get; set; }
}

public class RunnerCatalogue
{
    public long selectionId { get; set; }
    public string? runnerName { get; set; }
    public double? handicap { get; set; }
}

// ----- common filter -----
public class MarketFilter
{
    public HashSet<string>? eventTypeIds { get; set; }
    public HashSet<string>? eventIds { get; set; }
    public HashSet<string>? marketTypeCodes { get; set; }
    public string? textQuery { get; set; }
    public TimeRange? marketStartTime { get; set; }
    public HashSet<string>? marketIds { get; set; }

}
public class TimeRange
{
    public DateTime? from { get; set; }
    public DateTime? to { get; set; }
}

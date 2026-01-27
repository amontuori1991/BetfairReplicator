namespace BetfairReplicator.Models;

public class BetfairRpcRequest<TParams>
{
    public string jsonrpc { get; set; } = "2.0";
    public string method { get; set; } = "";
    public TParams? @params { get; set; }
    public int id { get; set; } = 1;
}

public class BetfairRpcResponse<TResult>
{
    public string? jsonrpc { get; set; }
    public TResult? result { get; set; }
    public BetfairRpcError? error { get; set; }
    public int id { get; set; }
}

public class BetfairRpcError
{
    public int code { get; set; }
    public string? message { get; set; }
    public object? data { get; set; }
}

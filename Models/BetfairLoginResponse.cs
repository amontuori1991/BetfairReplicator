namespace BetfairReplicator.Models;

public class BetfairLoginResponse
{
    public string? token { get; set; }
    public string? product { get; set; }
    public string? status { get; set; }
    public string? error { get; set; }

    // In Italia spesso arriva anche questo campo
    public string? lastLoginDate { get; set; }
}

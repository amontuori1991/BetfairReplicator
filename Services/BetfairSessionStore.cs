namespace BetfairReplicator.Services;

public class BetfairSessionStore
{
    // DisplayName -> SessionToken
    private readonly Dictionary<string, string> _tokens = new();

    public void SetToken(string displayName, string token) => _tokens[displayName] = token;

    public string? GetToken(string displayName)
        => _tokens.TryGetValue(displayName, out var t) ? t : null;

    public void ClearToken(string displayName) => _tokens.Remove(displayName);
}

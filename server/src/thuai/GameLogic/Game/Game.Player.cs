namespace Thuai.GameLogic;

using Serilog;

public partial class Game
{
    public Dictionary<string, Player> Players { get; } = new();
    public Dictionary<string, int> Scoreboard { get; } = new();

    private int _nextPlayerId;
    private readonly HashSet<string> _queuedPlayerJoins = new();
    private readonly HashSet<string> _queuedPlayerRemovals = new();

    /// <summary>
    /// Register a new player in the active game state.
    /// Returns true if the player was successfully added.
    /// </summary>
    public bool AddPlayer(string token)
    {
        lock (_lock)
        {
            return AddPlayerInternal(token);
        }
    }

    public bool QueuePlayerJoin(string token)
    {
        lock (_lock)
        {
            if (string.IsNullOrWhiteSpace(token))
                return false;

            if (Players.ContainsKey(token))
                return _queuedPlayerRemovals.Remove(token);

            if (_queuedPlayerJoins.Contains(token))
                return false;

            _queuedPlayerJoins.Add(token);
            return true;
        }
    }

    public bool QueuePlayerRemoval(string token)
    {
        lock (_lock)
        {
            if (string.IsNullOrWhiteSpace(token))
                return false;

            if (_queuedPlayerJoins.Remove(token))
                return true;

            if (!Players.ContainsKey(token))
                return false;

            return _queuedPlayerRemovals.Add(token);
        }
    }

    /// <summary>
    /// Look up a player by token.
    /// </summary>
    public Player? FindPlayer(string token)
    {
        lock (_lock)
        {
            Players.TryGetValue(token, out var player);
            return player;
        }
    }

    /// <summary>
    /// Look up a player by player ID.
    /// </summary>
    public Player? FindPlayerById(int playerId)
    {
        lock (_lock)
        {
            return Players.Values.FirstOrDefault(p => p.PlayerId == playerId);
        }
    }

    public List<Player> GetPlayersSnapshot()
    {
        lock (_lock)
        {
            return Players.Values
                .OrderBy(player => player.PlayerId)
                .ToList();
        }
    }

    public Dictionary<string, int> GetScoreboardSnapshot()
    {
        lock (_lock)
        {
            return new Dictionary<string, int>(Scoreboard);
        }
    }

    public Dictionary<string, long> GetCumulativeNavsSnapshot()
    {
        lock (_lock)
        {
            return new Dictionary<string, long>(CumulativeNavs);
        }
    }

    private bool AddPlayerInternal(string token)
    {
        if (Players.ContainsKey(token))
            return false;

        var player = new Player(token, _nextPlayerId++, _settings);
        Players[token] = player;
        Scoreboard[token] = 0;
        CumulativeNavs[token] = 0;

        if (Stage == GameStage.StrategySelection)
            _playerStrategySelected[token] = false;

        Log.Information("Player {Token} joined game with PlayerId {PlayerId}", token, player.PlayerId);
        return true;
    }

    private void ProcessQueuedPlayerChanges()
    {
        if (_queuedPlayerRemovals.Count > 0)
        {
            foreach (var token in _queuedPlayerRemovals.ToList())
            {
                RemovePlayerInternal(token);
            }

            _queuedPlayerRemovals.Clear();
        }

        if (_queuedPlayerJoins.Count > 0)
        {
            foreach (var token in _queuedPlayerJoins.ToList())
            {
                AddPlayerInternal(token);
            }

            _queuedPlayerJoins.Clear();
        }
    }

    private void RemovePlayerInternal(string token)
    {
        if (!Players.ContainsKey(token))
            return;

        CurrentTradingDay?.CancelPlayerOrders(token);
        Players.Remove(token);
        Scoreboard.Remove(token);
        CumulativeNavs.Remove(token);
        _playerStrategySelected.Remove(token);
        Log.Information("Player {Token} removed from game", token);
    }
}

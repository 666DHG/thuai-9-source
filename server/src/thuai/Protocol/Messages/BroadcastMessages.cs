namespace Thuai.Protocol.Messages;

using System.Text.Json.Serialization;

public record GameStateMessage : Message
{
    public override string MessageType => "GAME_STATE";

    [JsonPropertyName("stage")]
    public string Stage { get; init; } = "";

    [JsonPropertyName("currentMonth")]
    public int CurrentMonth { get; init; }

    [JsonPropertyName("currentDay")]
    public int CurrentDay { get; init; }

    [JsonPropertyName("currentTick")]
    public int CurrentTick { get; init; }

    [JsonPropertyName("totalTicks")]
    public int TotalTicks { get; init; }

    [JsonPropertyName("scores")]
    public List<PlayerScore>? Scores { get; init; }
}

public record PlayerScore
{
    [JsonPropertyName("playerId")]
    public int PlayerId { get; init; }

    [JsonPropertyName("score")]
    public int Score { get; init; }
}

public record MarketStateMessage : Message
{
    public override string MessageType => "MARKET_STATE";

    [JsonPropertyName("bids")]
    public List<PriceLevel>? Bids { get; init; }

    [JsonPropertyName("asks")]
    public List<PriceLevel>? Asks { get; init; }

    [JsonPropertyName("lastPrice")]
    public long LastPrice { get; init; }

    [JsonPropertyName("midPrice")]
    public long MidPrice { get; init; }

    [JsonPropertyName("volume")]
    public int Volume { get; init; }

    [JsonPropertyName("tick")]
    public int Tick { get; init; }
}

public record PriceLevel
{
    [JsonPropertyName("price")]
    public long Price { get; init; }

    [JsonPropertyName("quantity")]
    public int Quantity { get; init; }
}

public record PlayerStateMessage : Message
{
    public override string MessageType => "PLAYER_STATE";

    [JsonPropertyName("playerId")]
    public int PlayerId { get; init; }

    [JsonPropertyName("mora")]
    public long Mora { get; init; }

    [JsonPropertyName("frozenMora")]
    public long FrozenMora { get; init; }

    [JsonPropertyName("gold")]
    public int Gold { get; init; }

    [JsonPropertyName("frozenGold")]
    public int FrozenGold { get; init; }

    [JsonPropertyName("lockedGold")]
    public int LockedGold { get; init; }

    [JsonPropertyName("nav")]
    public long Nav { get; init; }

    [JsonPropertyName("networkDelay")]
    public int NetworkDelay { get; init; }

    [JsonPropertyName("immediateOrdersUsedToday")]
    public int ImmediateOrdersUsedToday { get; init; }

    [JsonPropertyName("restingOrdersUsedToday")]
    public int RestingOrdersUsedToday { get; init; }

    [JsonPropertyName("bonusImmediateOrdersToday")]
    public int BonusImmediateOrdersToday { get; init; }

    [JsonPropertyName("monthlyTradeCount")]
    public int MonthlyTradeCount { get; init; }

    [JsonPropertyName("activeCards")]
    public List<string>? ActiveCards { get; init; }

    [JsonPropertyName("pendingOrders")]
    public List<OrderInfo>? PendingOrders { get; init; }
}

public record OrderInfo
{
    [JsonPropertyName("orderId")]
    public long OrderId { get; init; }

    [JsonPropertyName("arrivalTick")]
    public int ArrivalTick { get; init; }

    [JsonPropertyName("side")]
    public string Side { get; init; } = "";

    [JsonPropertyName("price")]
    public long Price { get; init; }

    [JsonPropertyName("quantity")]
    public int Quantity { get; init; }

    [JsonPropertyName("remainingQuantity")]
    public int RemainingQuantity { get; init; }

    [JsonPropertyName("status")]
    public string Status { get; init; } = "";

    [JsonPropertyName("intent")]
    public string Intent { get; init; } = "";
}

public record NewsBroadcastMessage : Message
{
    public override string MessageType => "NEWS_BROADCAST";

    [JsonPropertyName("month")]
    public int Month { get; init; }

    [JsonPropertyName("day")]
    public int Day { get; init; }

    [JsonPropertyName("newsId")]
    public int NewsId { get; init; }

    [JsonPropertyName("content")]
    public string Content { get; init; } = "";

    [JsonPropertyName("publishTick")]
    public int PublishTick { get; init; }
}

public record ReportResultMessage : Message
{
    public override string MessageType => "REPORT_RESULT";

    [JsonPropertyName("newsId")]
    public int NewsId { get; init; }

    [JsonPropertyName("submissionRank")]
    public int SubmissionRank { get; init; }

    [JsonPropertyName("submitTick")]
    public int SubmitTick { get; init; }

    [JsonPropertyName("settlementTick")]
    public int SettlementTick { get; init; }

    [JsonPropertyName("prediction")]
    public string Prediction { get; init; } = "";

    [JsonPropertyName("isCorrect")]
    public bool IsCorrect { get; init; }

    [JsonPropertyName("reward")]
    public long Reward { get; init; }

    [JsonPropertyName("actualChange")]
    public long ActualChange { get; init; }
}

public record StrategyOptionsMessage : Message
{
    public override string MessageType => "STRATEGY_OPTIONS";

    [JsonPropertyName("infrastructure")]
    public CardOption? Infrastructure { get; init; }

    [JsonPropertyName("riskControl")]
    public CardOption? RiskControl { get; init; }

    [JsonPropertyName("finTech")]
    public CardOption? FinTech { get; init; }
}

public record CardOption
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("description")]
    public string Description { get; init; } = "";

    [JsonPropertyName("category")]
    public string Category { get; init; } = "";
}

public record TradeNotificationMessage : Message
{
    public override string MessageType => "TRADE_NOTIFICATION";

    [JsonPropertyName("tradeId")]
    public long TradeId { get; init; }

    [JsonPropertyName("orderId")]
    public long OrderId { get; init; }

    [JsonPropertyName("price")]
    public long Price { get; init; }

    [JsonPropertyName("quantity")]
    public int Quantity { get; init; }

    [JsonPropertyName("side")]
    public string Side { get; init; } = "";

    [JsonPropertyName("fee")]
    public long Fee { get; init; }
}

public record SkillEffectMessage : Message
{
    public override string MessageType => "SKILL_EFFECT";

    [JsonPropertyName("skillName")]
    public string SkillName { get; init; } = "";

    [JsonPropertyName("sourcePlayerId")]
    public int SourcePlayerId { get; init; }

    [JsonPropertyName("targetPlayerId")]
    public int? TargetPlayerId { get; init; }

    [JsonPropertyName("description")]
    public string Description { get; init; } = "";
}

public record DaySettlementMessage : Message
{
    public override string MessageType => "DAY_SETTLEMENT";

    [JsonPropertyName("day")]
    public int Day { get; init; }

    [JsonPropertyName("month")]
    public int Month { get; init; }

    [JsonPropertyName("winnerPlayerId")]
    public int WinnerPlayerId { get; init; }

    [JsonPropertyName("reason")]
    public string Reason { get; init; } = "";

    [JsonPropertyName("players")]
    public List<DaySettlementPlayer>? Players { get; init; }

    [JsonPropertyName("cumulativeNavs")]
    public Dictionary<int, long>? CumulativeNavs { get; init; }

    [JsonPropertyName("finalBonusWinnerPlayerId")]
    public int FinalBonusWinnerPlayerId { get; init; }

    [JsonPropertyName("finalBonusPoints")]
    public int FinalBonusPoints { get; init; }
}

public record DaySettlementPlayer
{
    [JsonPropertyName("playerId")]
    public int PlayerId { get; init; }

    [JsonPropertyName("nav")]
    public long Nav { get; init; }

    [JsonPropertyName("mora")]
    public long Mora { get; init; }

    [JsonPropertyName("gold")]
    public int Gold { get; init; }

    [JsonPropertyName("frozenMora")]
    public long FrozenMora { get; init; }

    [JsonPropertyName("frozenGold")]
    public int FrozenGold { get; init; }

    [JsonPropertyName("lockedGold")]
    public int LockedGold { get; init; }

    [JsonPropertyName("tradeCount")]
    public int TradeCount { get; init; }

    [JsonPropertyName("activeCards")]
    public List<string>? ActiveCards { get; init; }
}

public record ErrorMessage : Message
{
    public override string MessageType => "ERROR";

    [JsonPropertyName("errorCode")]
    public int ErrorCode { get; init; }

    [JsonPropertyName("message")]
    public string ErrorText { get; init; } = "";
}

// --- Debug responses (delivered to the requesting admin socket) ---

public record DebugAckMessage : Message
{
    public override string MessageType => "DEBUG_ACK";

    [JsonPropertyName("command")]
    public string Command { get; init; } = "";

    [JsonPropertyName("ok")]
    public bool Ok { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }
}

public record DebugQueryResponseMessage : Message
{
    public override string MessageType => "DEBUG_QUERY_RESPONSE";

    [JsonPropertyName("stage")]
    public string Stage { get; init; } = "";

    [JsonPropertyName("currentMonth")]
    public int CurrentMonth { get; init; }

    [JsonPropertyName("currentDay")]
    public int CurrentDay { get; init; }

    [JsonPropertyName("currentTick")]
    public int CurrentTick { get; init; }

    [JsonPropertyName("scoreboard")]
    public Dictionary<int, int>? Scoreboard { get; init; }

    [JsonPropertyName("cumulativeNavs")]
    public Dictionary<int, long>? CumulativeNavs { get; init; }

    [JsonPropertyName("players")]
    public List<DebugPlayerSnapshot>? Players { get; init; }

    [JsonPropertyName("draft")]
    public DebugDraftSnapshot? Draft { get; init; }
}

public record DebugPlayerSnapshot
{
    [JsonPropertyName("playerId")]
    public int PlayerId { get; init; }

    [JsonPropertyName("token")]
    public string Token { get; init; } = "";

    [JsonPropertyName("mora")]
    public long Mora { get; init; }

    [JsonPropertyName("frozenMora")]
    public long FrozenMora { get; init; }

    [JsonPropertyName("gold")]
    public int Gold { get; init; }

    [JsonPropertyName("frozenGold")]
    public int FrozenGold { get; init; }

    [JsonPropertyName("lockedGold")]
    public int LockedGold { get; init; }

    [JsonPropertyName("monthlyTradeCount")]
    public int MonthlyTradeCount { get; init; }

    [JsonPropertyName("activeCards")]
    public List<string>? ActiveCards { get; init; }
}

public record DebugDraftSnapshot
{
    [JsonPropertyName("infrastructure")]
    public string? Infrastructure { get; init; }

    [JsonPropertyName("riskControl")]
    public string? RiskControl { get; init; }

    [JsonPropertyName("finTech")]
    public string? FinTech { get; init; }
}

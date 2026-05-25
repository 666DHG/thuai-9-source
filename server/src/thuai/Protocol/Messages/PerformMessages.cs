namespace Thuai.Protocol.Messages;

using System.Text.Json.Serialization;

public record HelloMessage : PerformMessage
{
    public override string MessageType => "HELLO";

    [JsonPropertyName("role")]
    public string Role { get; init; } = "";

    [JsonPropertyName("adminSecret")]
    public string? AdminSecret { get; init; }
}

public record LimitBuyMessage : PerformMessage
{
    public override string MessageType => "LIMIT_BUY";

    [JsonPropertyName("price")]
    public long Price { get; init; }

    [JsonPropertyName("quantity")]
    public int Quantity { get; init; }
}

public record LimitSellMessage : PerformMessage
{
    public override string MessageType => "LIMIT_SELL";

    [JsonPropertyName("price")]
    public long Price { get; init; }

    [JsonPropertyName("quantity")]
    public int Quantity { get; init; }
}

public record CancelOrderMessage : PerformMessage
{
    public override string MessageType => "CANCEL_ORDER";

    [JsonPropertyName("orderId")]
    public long OrderId { get; init; }
}

public record SubmitReportMessage : PerformMessage
{
    public override string MessageType => "SUBMIT_REPORT";

    [JsonPropertyName("newsId")]
    public int NewsId { get; init; }

    [JsonPropertyName("prediction")]
    public string Prediction { get; init; } = "";
}

public record SelectStrategyMessage : PerformMessage
{
    public override string MessageType => "SELECT_STRATEGY";

    [JsonPropertyName("cardName")]
    public string CardName { get; init; } = "";
}

public record ActivateSkillMessage : PerformMessage
{
    public override string MessageType => "ACTIVATE_SKILL";

    [JsonPropertyName("skillName")]
    public string SkillName { get; init; } = "";

    [JsonPropertyName("targetPlayerId")]
    public int? TargetPlayerId { get; init; }

    [JsonPropertyName("variant")]
    public string? Variant { get; init; }
}

// --- Debug messages (admin role only) ---

public record DebugQueryMessage : PerformMessage
{
    public override string MessageType => "DEBUG_QUERY";
}

public record DebugGiveCardMessage : PerformMessage
{
    public override string MessageType => "DEBUG_GIVE_CARD";

    [JsonPropertyName("targetPlayerId")]
    public int? TargetPlayerId { get; init; }

    [JsonPropertyName("cardName")]
    public string CardName { get; init; } = "";
}

public record DebugInjectNewsMessage : PerformMessage
{
    public override string MessageType => "DEBUG_INJECT_NEWS";

    [JsonPropertyName("sentiment")]
    public string Sentiment { get; init; } = "";

    [JsonPropertyName("content")]
    public string? Content { get; init; }
}

public record DebugAdvanceStageMessage : PerformMessage
{
    public override string MessageType => "DEBUG_ADVANCE_STAGE";
}

public record DebugSetPlayerMessage : PerformMessage
{
    public override string MessageType => "DEBUG_SET_PLAYER";

    [JsonPropertyName("targetPlayerId")]
    public int? TargetPlayerId { get; init; }

    [JsonPropertyName("mora")]
    public long? Mora { get; init; }

    [JsonPropertyName("gold")]
    public int? Gold { get; init; }
}

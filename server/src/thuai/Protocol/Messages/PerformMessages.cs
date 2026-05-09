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

    [JsonPropertyName("targetToken")]
    public string? TargetToken { get; init; }

    [JsonPropertyName("variant")]
    public string? Variant { get; init; }
}

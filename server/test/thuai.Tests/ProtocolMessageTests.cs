using System.Text.Json;
using Thuai.Protocol.Messages;

namespace Thuai.Tests;

public class ProtocolMessageTests
{
    [Fact]
    public void PerformMessages_SerializeDocumentedFieldNames()
    {
        var limitBuy = ParseJson(new LimitBuyMessage
        {
            Token = "player-1",
            Price = 1234,
            Quantity = 5
        });
        Assert.Equal("LIMIT_BUY", limitBuy.GetProperty("messageType").GetString());
        Assert.Equal("player-1", limitBuy.GetProperty("token").GetString());
        Assert.Equal(1234, limitBuy.GetProperty("price").GetInt64());
        Assert.Equal(5, limitBuy.GetProperty("quantity").GetInt32());

        var cancelOrder = ParseJson(new CancelOrderMessage
        {
            Token = "player-2",
            OrderId = 88
        });
        Assert.Equal("CANCEL_ORDER", cancelOrder.GetProperty("messageType").GetString());
        Assert.Equal("player-2", cancelOrder.GetProperty("token").GetString());
        Assert.Equal(88, cancelOrder.GetProperty("orderId").GetInt64());

        var submitReport = ParseJson(new SubmitReportMessage
        {
            Token = "player-3",
            NewsId = 6,
            Prediction = "Long"
        });
        Assert.Equal("SUBMIT_REPORT", submitReport.GetProperty("messageType").GetString());
        Assert.Equal(6, submitReport.GetProperty("newsId").GetInt32());
        Assert.Equal("Long", submitReport.GetProperty("prediction").GetString());
    }

    [Fact]
    public void ActivateSkillMessage_OmitsOptionalFieldsWhenNull()
    {
        var json = ParseJson(new ActivateSkillMessage
        {
            Token = "player-1",
            SkillName = "MarketRadar"
        });

        Assert.Equal("ACTIVATE_SKILL", json.GetProperty("messageType").GetString());
        Assert.Equal("MarketRadar", json.GetProperty("skillName").GetString());
        Assert.False(json.TryGetProperty("targetPlayerId", out _));
        Assert.False(json.TryGetProperty("variant", out _));
    }

    [Fact]
    public void PlayerStateMessage_SerializesNestedProtocolShape()
    {
        var json = ParseJson(new PlayerStateMessage
        {
            PlayerId = 0,
            Mora = 1200,
            FrozenMora = 150,
            Gold = 8,
            FrozenGold = 2,
            LockedGold = 1,
            Nav = 1320,
            NetworkDelay = 40,
            ImmediateOrdersUsedToday = 3,
            RestingOrdersUsedToday = 4,
            BonusImmediateOrdersToday = 1,
            MonthlyTradeCount = 12,
            ActiveCards = ["Bridge", "Firewall"],
            PendingOrders =
            [
                new OrderInfo
                {
                    OrderId = 99,
                    ArrivalTick = 17,
                    Side = "Buy",
                    Price = 980,
                    Quantity = 6,
                    RemainingQuantity = 2,
                    Status = "Active",
                    Intent = "Resting"
                }
            ]
        });

        Assert.Equal("PLAYER_STATE", json.GetProperty("messageType").GetString());
        Assert.Equal(0, json.GetProperty("playerId").GetInt32());
        Assert.Equal(1200, json.GetProperty("mora").GetInt64());
        Assert.Equal(12, json.GetProperty("monthlyTradeCount").GetInt32());

        var cards = json.GetProperty("activeCards");
        Assert.Equal(2, cards.GetArrayLength());
        Assert.Equal("Bridge", cards[0].GetString());

        var order = json.GetProperty("pendingOrders")[0];
        Assert.Equal(99, order.GetProperty("orderId").GetInt64());
        Assert.Equal(17, order.GetProperty("arrivalTick").GetInt32());
        Assert.Equal("Buy", order.GetProperty("side").GetString());
        Assert.Equal(980, order.GetProperty("price").GetInt64());
        Assert.Equal(6, order.GetProperty("quantity").GetInt32());
        Assert.Equal(2, order.GetProperty("remainingQuantity").GetInt32());
        Assert.Equal("Active", order.GetProperty("status").GetString());
        Assert.Equal("Resting", order.GetProperty("intent").GetString());
    }

    [Fact]
    public void BroadcastMessages_SerializeCurrentRuleFields()
    {
        var gameState = ParseJson(new GameStateMessage
        {
            Stage = "TradingDay",
            CurrentMonth = 3,
            CurrentDay = 2,
            CurrentTick = 45,
            TotalTicks = 300,
            Scores =
            [
                new PlayerScore { PlayerId = 0, Score = 14 },
                new PlayerScore { PlayerId = 1, Score = 9 }
            ]
        });
        Assert.Equal(3, gameState.GetProperty("currentMonth").GetInt32());
        Assert.Equal(2, gameState.GetProperty("currentDay").GetInt32());
        Assert.Equal(2, gameState.GetProperty("scores").GetArrayLength());
        Assert.Equal(0, gameState.GetProperty("scores")[0].GetProperty("playerId").GetInt32());

        var news = ParseJson(new NewsBroadcastMessage
        {
            Month = 4,
            Day = 1,
            NewsId = 7,
            Content = "Quarterly demand is rising",
            PublishTick = 120
        });
        Assert.Equal(4, news.GetProperty("month").GetInt32());
        Assert.Equal(1, news.GetProperty("day").GetInt32());
        Assert.Equal(120, news.GetProperty("publishTick").GetInt32());

        var report = ParseJson(new ReportResultMessage
        {
            NewsId = 7,
            SubmissionRank = 1,
            SubmitTick = 122,
            SettlementTick = 200,
            Prediction = "Short",
            IsCorrect = true,
            Reward = 300,
            ActualChange = -90
        });
        Assert.Equal(1, report.GetProperty("submissionRank").GetInt32());
        Assert.True(report.GetProperty("isCorrect").GetBoolean());
        Assert.Equal(-90, report.GetProperty("actualChange").GetInt64());
    }

    [Fact]
    public void OptionalBroadcastFields_AreSerializedAsDocumented()
    {
        var skillEffect = ParseJson(new SkillEffectMessage
        {
            SkillName = "Hedge",
            SourcePlayerId = 0,
            Description = "Protected against the next loss"
        });
        Assert.Equal("SKILL_EFFECT", skillEffect.GetProperty("messageType").GetString());
        Assert.Equal(0, skillEffect.GetProperty("sourcePlayerId").GetInt32());
        Assert.False(skillEffect.TryGetProperty("targetPlayerId", out _));

        var settlement = ParseJson(new DaySettlementMessage
        {
            Day = 2,
            Month = 5,
            WinnerPlayerId = 0,
            Reason = "highest NAV",
            Players =
            [
                new DaySettlementPlayer
                {
                    PlayerId = 0,
                    Nav = 1500,
                    Mora = 900,
                    Gold = 6,
                    FrozenMora = 100,
                    FrozenGold = 1,
                    LockedGold = 0,
                    TradeCount = 11,
                    ActiveCards = ["Bridge"]
                }
            ],
            CumulativeNavs = new Dictionary<int, long> { [0] = 4500 },
            FinalBonusWinnerPlayerId = 1,
            FinalBonusPoints = 2
        });
        Assert.Equal(5, settlement.GetProperty("month").GetInt32());
        Assert.Equal(1, settlement.GetProperty("finalBonusWinnerPlayerId").GetInt32());
        Assert.Equal(4500, settlement.GetProperty("cumulativeNavs").GetProperty("0").GetInt64());

        var error = ParseJson(new ErrorMessage
        {
            ErrorCode = 4001,
            ErrorText = "invalid card"
        });
        Assert.Equal("ERROR", error.GetProperty("messageType").GetString());
        Assert.Equal(4001, error.GetProperty("errorCode").GetInt32());
        Assert.Equal("invalid card", error.GetProperty("message").GetString());
        Assert.False(error.TryGetProperty("errorText", out _));
    }

    private static JsonElement ParseJson(Message message)
    {
        using var document = JsonDocument.Parse(message.Json);
        return document.RootElement.Clone();
    }
}

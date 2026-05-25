namespace Thuai.GameController;

using Serilog;
using Thuai.GameLogic;
using Thuai.Protocol.Messages;
using Thuai.Connection;

public partial class GameController
{
    /// <summary>
    /// Called when AgentServer receives and parses a message from a player.
    /// Routes the message to the appropriate handler based on its type.
    /// </summary>
    public void HandleAfterMessageReceiveEvent(object? sender, AgentServer.AfterMessageReceiveEventArgs e)
    {
        var message = e.Message;
        string token = message.Token;

        // Find the player
        var player = Game.FindPlayer(token);
        if (player == null)
        {
            if (Game.QueuePlayerJoin(token))
            {
                Log.Information("Player {Token} queued to join from an action message", token);
            }
            else
            {
                Log.Warning("Message from unknown player token: {Token}", token);
            }
            return;
        }

        switch (message)
        {
            case LimitBuyMessage buy:
                HandleLimitBuy(token, buy);
                break;

            case LimitSellMessage sell:
                HandleLimitSell(token, sell);
                break;

            case CancelOrderMessage cancel:
                HandleCancelOrder(token, cancel);
                break;

            case SubmitReportMessage report:
                HandleSubmitReport(token, report);
                break;

            case SelectStrategyMessage strategy:
                HandleSelectStrategy(token, strategy);
                break;

            case ActivateSkillMessage skill:
                HandleActivateSkill(token, skill);
                break;

            default:
                Log.Warning("Unhandled message type: {Type} from {Token}", message.MessageType, token);
                break;
        }
    }

    /// <summary>
    /// Called when AgentServer identifies a new player connection.
    /// Queues the player to (re)join the game if needed.
    /// </summary>
    public void HandleAfterPlayerConnectEvent(object? sender, AgentServer.AfterPlayerConnectEventArgs e)
    {
        if (Game.FindPlayer(e.Token) != null)
        {
            Log.Information("Player {Token} connected", e.Token);
            return;
        }

        if (Game.QueuePlayerJoin(e.Token))
            Log.Information("Player {Token} queued to join the running game", e.Token);
    }

    private void HandleLimitBuy(string token, LimitBuyMessage msg)
    {
        if (Game.Stage != GameStage.TradingDay || Game.CurrentTradingDay == null) return;

        bool success = Game.CurrentTradingDay.HandleLimitBuy(token, msg.Price, msg.Quantity);
        if (!success)
        {
            Log.Debug("LimitBuy rejected for {Token}: price={Price}, qty={Qty}", token, msg.Price, msg.Quantity);
        }
    }

    private void HandleLimitSell(string token, LimitSellMessage msg)
    {
        if (Game.Stage != GameStage.TradingDay || Game.CurrentTradingDay == null) return;

        bool success = Game.CurrentTradingDay.HandleLimitSell(token, msg.Price, msg.Quantity);
        if (!success)
        {
            Log.Debug("LimitSell rejected for {Token}: price={Price}, qty={Qty}", token, msg.Price, msg.Quantity);
        }
    }

    private void HandleCancelOrder(string token, CancelOrderMessage msg)
    {
        if (Game.Stage != GameStage.TradingDay || Game.CurrentTradingDay == null) return;
        Game.CurrentTradingDay.HandleCancelOrder(token, msg.OrderId);
    }

    private void HandleSubmitReport(string token, SubmitReportMessage msg)
    {
        if (Game.Stage != GameStage.TradingDay || Game.CurrentTradingDay == null) return;

        if (!Enum.TryParse<Prediction>(msg.Prediction, true, out var prediction))
        {
            Log.Warning("Invalid prediction value: {Prediction} from {Token}", msg.Prediction, token);
            return;
        }

        Game.CurrentTradingDay.HandleSubmitReport(token, msg.NewsId, prediction);
    }

    private void HandleSelectStrategy(string token, SelectStrategyMessage msg)
    {
        if (Game.Stage != GameStage.StrategySelection) return;
        Game.SelectStrategy(token, msg.CardName);
    }

    private void HandleActivateSkill(string token, ActivateSkillMessage msg)
    {
        if (Game.Stage != GameStage.TradingDay || Game.CurrentTradingDay == null) return;
        Game.CurrentTradingDay.HandleActivateSkill(token, msg.SkillName, msg.TargetPlayerId, msg.Variant);
    }
}

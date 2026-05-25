namespace Thuai;

using Serilog;
using Serilog.Events;
using Utility;
using Connection;
using GameLogic;
using Protocol.Messages;
using ServerRuntime = Thuai.Runtime;

public class Program
{
    public static void Main(string[] args)
    {
        Config config;
        try
        {
            config = Tools.LoadOrCreateConfig("config/config.json");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to load config: {ex.Message}");
            return;
        }

        bool infiniteMode = config.Game.InfiniteMode
            || args.Any(arg => string.Equals(arg, "--infinite", StringComparison.OrdinalIgnoreCase));
        if (infiniteMode)
        {
            config = config with { Game = config.Game with { InfiniteMode = true } };
        }

        // Setup logging
        SetupLogger(config.Log);
        Log.Information("THUAI-9 Server starting...");
        if (infiniteMode)
            Log.Information("Infinite mode enabled: month numbers will continue past month 3");

        try
        {
            // Create components
            var agentServer = new AgentServer
            {
                Port = config.Server.Port,
                AdminSecret = Environment.GetEnvironmentVariable("THUAI_ADMIN_SECRET"),
                AcceptAnyToken = config.Server.AcceptAnyToken
            };
            if (!string.IsNullOrEmpty(agentServer.AdminSecret))
                Log.Information("Admin debug interface enabled (THUAI_ADMIN_SECRET set)");
            if (agentServer.AcceptAnyToken)
                Log.Warning("Open token mode enabled: any non-empty player token will be accepted");
            var gameController = new GameController.GameController(config.Game);
            using var recorder = new Recorder.Recorder("./data", config.Recorder.KeepRecord, config.Recorder.FlushEveryRecords);
            using var statRecorder = new Recorder.StatRecorder("./data", config.Recorder.EnableStatRecording, config.Recorder.StatFlushEveryRecords);
            var disconnectedPlayerRetentionTicks = config.Game.DisconnectedPlayerRetentionTicks;
            if (infiniteMode && disconnectedPlayerRetentionTicks <= 0)
                disconnectedPlayerRetentionTicks = Math.Max(config.Game.TicksPerSecond * 300, 1);
            var sessionTracker = new ServerRuntime.PlayerSessionTracker(disconnectedPlayerRetentionTicks);
            var statisticsWriter = new Recorder.PlayerStatisticsWriter(
                "./data",
                saveIntervalTicks: config.Recorder.StatisticsSaveIntervalTicks);
            if (infiniteMode)
                Log.Information("Disconnected players will be removed after {Ticks} ticks", disconnectedPlayerRetentionTicks);

            // Load tokens and add players
            var tokens = Tools.LoadTokens(config.Token);
            Log.Information("Loaded {Count} player tokens", tokens.Length);
            foreach (var token in tokens)
            {
                gameController.Game.AddPlayer(token);
                agentServer.RegisterValidToken(token);
                if (infiniteMode)
                    sessionTracker.SeedDisconnected(token, currentTick: 0);
                Log.Information("Added player: {Token}", token);
            }

            // Wire events
            // AgentServer -> GameController: player messages
            agentServer.AfterMessageReceiveEvent += gameController.HandleAfterMessageReceiveEvent;
            // AgentServer -> GameController: player connections
            agentServer.AfterPlayerConnectEvent += (sender, e) =>
            {
                sessionTracker.MarkConnected(e.Token, gameController.Game.CurrentTick);
                gameController.HandleAfterPlayerConnectEvent(sender, e);
            };
            agentServer.AfterPlayerDisconnectEvent += (sender, e) =>
            {
                sessionTracker.MarkDisconnected(e.Token, gameController.Game.CurrentTick);
            };
            // AgentServer -> AdminCommandHandler: debug commands from admin sockets
            agentServer.AfterAdminMessageEvent += (sender, e) =>
            {
                var response = AdminCommandHandler.Handle(gameController.Game, e.Message);
                agentServer.PublishToSocket(response, e.SocketId);
            };

            // Game -> Broadcast + Record: after each tick
            gameController.Game.AfterGameTickEvent += (sender, e) =>
            {
                ExpireDisconnectedPlayers(e.Game, sessionTracker);
                statRecorder.RecordFromGame(e.Game);
                BroadcastGameState(agentServer, e.Game);
                RecordGameState(recorder, e.Game);

                if (e.Game.Stage == GameStage.Settlement)
                {
                    recorder.SaveResults(e.Game.GetScoreboardSnapshot());
                }

                var sessions = sessionTracker.GetSnapshots(e.Game.CurrentTick);
                if (statisticsWriter.MaybeSave(e.Game, sessions))
                {
                    recorder.SaveResults(e.Game.GetScoreboardSnapshot());
                }
            };

            // Start server
            agentServer.Start();
            gameController.Start();

            Log.Information(infiniteMode
                ? "Server running in infinite mode."
                : "Server running. Waiting for game to finish...");

            // Poll until game finishes
            while (gameController.IsRunning)
            {
                Task.Delay(1000).Wait();
            }

            // Game finished - save results
            Log.Information("Game complete. Saving results...");
            recorder.Flush();
            statRecorder.Flush();

            var scores = gameController.Game.GetScoreboardSnapshot();
            statisticsWriter.Save(gameController.Game, sessionTracker.GetSnapshots(gameController.Game.CurrentTick));
            recorder.SaveResults(scores);

            foreach (var (token, score) in scores)
            {
                Log.Information("Player {Token}: {Score} points", token, score);
            }

            agentServer.Stop();
            gameController.Stop();
            Log.Information("Server shut down");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Server crashed");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static void ExpireDisconnectedPlayers(Game game, ServerRuntime.PlayerSessionTracker sessionTracker)
    {
        foreach (var token in sessionTracker.CollectExpiredTokens(game.CurrentTick))
        {
            if (game.QueuePlayerRemoval(token))
                Log.Information("Player {Token} queued for removal after disconnect timeout", token);
        }
    }

    private static void SetupLogger(LogSettings logSettings)
    {
        var levelSwitch = logSettings.MinimumLevel.ToLower() switch
        {
            "verbose" => LogEventLevel.Verbose,
            "debug" => LogEventLevel.Debug,
            "information" => LogEventLevel.Information,
            "warning" => LogEventLevel.Warning,
            "error" => LogEventLevel.Error,
            "fatal" => LogEventLevel.Fatal,
            _ => LogEventLevel.Information
        };

        var logConfig = new LoggerConfiguration()
            .MinimumLevel.Is(levelSwitch);

        if (logSettings.Target.ToLower() == "console" || logSettings.Target.ToLower() == "both")
        {
            logConfig = logConfig.WriteTo.Console();
        }

        if (logSettings.Target.ToLower() == "file" || logSettings.Target.ToLower() == "both")
        {
            var interval = logSettings.RollingInterval.ToLower() switch
            {
                "hour" => RollingInterval.Hour,
                "day" => RollingInterval.Day,
                "month" => RollingInterval.Month,
                _ => RollingInterval.Day
            };

            logConfig = logConfig.WriteTo.File(
                Path.Combine(logSettings.TargetDirectory, "thuai-.log"),
                rollingInterval: interval);
        }

        Log.Logger = logConfig.CreateLogger();
    }

    private static void BroadcastGameState(AgentServer agentServer, Game game)
    {
        var gameState = new GameStateMessage
        {
            Stage = game.Stage.ToString(),
            CurrentMonth = game.CurrentMonthNumber,
            CurrentDay = game.CurrentDayNumber,
            CurrentTick = game.CurrentTick,
            TotalTicks = 30,
            Scores = game.Players.Values
                .OrderBy(player => player.PlayerId)
                .Select(player => new PlayerScore
                {
                    PlayerId = player.PlayerId,
                    Score = game.Scoreboard.GetValueOrDefault(player.Token)
                })
                .ToList()
        };
        agentServer.PublishToAll(gameState);

        if (game.CurrentTradingDay is { HasPendingNotifications: true })
        {
            PublishTradingDayNotifications(agentServer, game);
            game.CurrentTradingDay.MarkNotificationsPublished();
        }

        if (game is { HasPendingSettlementNotification: true, CurrentTradingDay: not null })
        {
            agentServer.PublishToAll(BuildDaySettlementMessage(game));
            game.MarkSettlementNotificationPublished();
        }

        if ((game.Stage == GameStage.TradingDay || game.Stage == GameStage.Settlement) && game.CurrentTradingDay != null)
        {
            var day = game.CurrentTradingDay;
            var orderBook = day.OrderBook;

            var baseBids = orderBook.GetVisibleBids().Select(l => new PriceLevel
            {
                Price = l.Price,
                Quantity = l.Quantity
            }).ToList();
            var baseAsks = orderBook.GetVisibleAsks().Select(l => new PriceLevel
            {
                Price = l.Price,
                Quantity = l.Quantity
            }).ToList();

            // Public market snapshot — identical for everyone, broadcast once.
            var marketState = new MarketStateMessage
            {
                Bids = baseBids,
                Asks = baseAsks,
                LastPrice = orderBook.LastPrice,
                MidPrice = orderBook.MidPrice,
                Volume = orderBook.TotalVolume,
                Tick = day.CurrentTick
            };
            agentServer.PublishToAll(marketState);

            foreach (var player in game.Players.Values)
            {
                var pendingOrders = day.GetPlayerPendingOrders(player.Token);
                var playerState = new PlayerStateMessage
                {
                    PlayerId = player.PlayerId,
                    Mora = player.Mora,
                    FrozenMora = player.FrozenMora,
                    Gold = player.Gold,
                    FrozenGold = player.FrozenGold,
                    LockedGold = player.LockedGold,
                    Nav = player.CalculateNAV(orderBook.MidPrice),
                    NetworkDelay = player.NetworkDelay,
                    ImmediateOrdersUsedToday = player.ImmediateOrdersUsedToday,
                    RestingOrdersUsedToday = player.RestingOrdersUsedToday,
                    BonusImmediateOrdersToday = player.BonusImmediateOrdersToday,
                    MonthlyTradeCount = player.MonthlyTradeCount,
                    ActiveCards = player.ActiveCards.Select(c => c.Name).ToList(),
                    PendingOrders = pendingOrders.Select(o => new OrderInfo
                    {
                        OrderId = o.OrderId,
                        ArrivalTick = o.ArrivalTick,
                        Side = o.Side.ToString(),
                        Price = o.Price,
                        Quantity = o.Quantity,
                        RemainingQuantity = o.RemainingQuantity,
                        Status = o.Status.ToString(),
                        Intent = o.Intent?.ToString() ?? ""
                    }).ToList()
                };
                agentServer.Publish(playerState, player.Token);
            }
        }

        if (game.Stage == GameStage.StrategySelection)
        {
            var cardManager = game.CardManager;
            var options = new StrategyOptionsMessage
            {
                Infrastructure = cardManager.CurrentInfrastructure != null
                    ? new CardOption
                    {
                        Name = cardManager.CurrentInfrastructure.Name,
                        Description = cardManager.CurrentInfrastructure.Description,
                        Category = "Infrastructure"
                    }
                    : null,
                RiskControl = cardManager.CurrentRiskControl != null
                    ? new CardOption
                    {
                        Name = cardManager.CurrentRiskControl.Name,
                        Description = cardManager.CurrentRiskControl.Description,
                        Category = "RiskControl"
                    }
                    : null,
                FinTech = cardManager.CurrentFinTech != null
                    ? new CardOption
                    {
                        Name = cardManager.CurrentFinTech.Name,
                        Description = cardManager.CurrentFinTech.Description,
                        Category = "FinTech"
                    }
                    : null
            };
            agentServer.PublishToAll(options);
        }
    }

    private static void PublishTradingDayNotifications(AgentServer agentServer, Game game)
    {
        var tradingDay = game.CurrentTradingDay!;

        foreach (var news in tradingDay.PublishedNewsThisDay)
        {
            // Each player may receive a spoofed view of the news due to skill cards,
            // so the broadcast happens per-player. Observers and admins receive the
            // un-spoofed real news via a separate PublishToAll call below — admins
            // already get it through the per-player fan-out, so we only fan out to
            // the role-broadcast for the un-bound observers.
            foreach (var player in game.Players.Values)
            {
                News delivered = news;
                if (!news.IsFake && player.ConsumePendingFakeBroadcast())
                {
                    delivered = tradingDay.NewsSystem.CreateSpoofedView(news);
                }

                agentServer.Publish(new NewsBroadcastMessage
                {
                    Month = game.CurrentMonthNumber,
                    Day = delivered.PublishTick,
                    NewsId = delivered.NewsId,
                    Content = delivered.Content,
                    PublishTick = delivered.PublishTick
                }, player.Token);
            }

            agentServer.PublishToObservers(new NewsBroadcastMessage
            {
                Month = game.CurrentMonthNumber,
                Day = news.PublishTick,
                NewsId = news.NewsId,
                Content = news.Content,
                PublishTick = news.PublishTick
            });
        }

        foreach (var (playerToken, preview) in tradingDay.PendingInsiderPreviews)
        {
            agentServer.Publish(new NewsBroadcastMessage
            {
                Month = game.CurrentMonthNumber,
                Day = preview.PublishTick,
                NewsId = preview.NewsId,
                Content = preview.Content,
                PublishTick = preview.PublishTick
            }, playerToken);
        }

        foreach (var report in tradingDay.SettledReportsThisDay)
        {
            agentServer.Publish(new ReportResultMessage
            {
                NewsId = report.NewsId,
                SubmissionRank = report.SubmissionRank,
                SubmitTick = report.SubmitTick,
                SettlementTick = report.SettlementDay,
                Prediction = report.Prediction.ToString(),
                IsCorrect = report.IsCorrect ?? false,
                Reward = report.Reward,
                ActualChange = report.ActualChange
            }, report.PlayerToken);
        }

        foreach (var trade in tradingDay.TradesThisDay)
        {
            if (trade.BuyerToken != "SYSTEM")
            {
                agentServer.Publish(new TradeNotificationMessage
                {
                    TradeId = trade.TradeId,
                    OrderId = trade.BuyOrderId,
                    Price = trade.Price,
                    Quantity = trade.Quantity,
                    Side = "Buy",
                    Fee = trade.BuyerFee
                }, trade.BuyerToken);
            }

            if (trade.SellerToken != "SYSTEM")
            {
                agentServer.Publish(new TradeNotificationMessage
                {
                    TradeId = trade.TradeId,
                    OrderId = trade.SellOrderId,
                    Price = trade.Price,
                    Quantity = trade.Quantity,
                    Side = "Sell",
                    Fee = trade.SellerFee
                }, trade.SellerToken);
            }
        }

        foreach (var effect in tradingDay.SkillEffectsThisDay)
        {
            agentServer.PublishToAll(new SkillEffectMessage
            {
                SkillName = effect.SkillName,
                SourcePlayerId = effect.SourcePlayerId,
                TargetPlayerId = effect.TargetPlayerId,
                Description = effect.Description
            });
        }
    }

    private static DaySettlementMessage BuildDaySettlementMessage(Game game)
    {
        var settlement = game.LatestSettlement!;
        var day = game.CurrentTradingDay!;
        var midPrice = day.OrderBook.MidPrice;
        var players = game.Players.Values
            .Select(player => new DaySettlementPlayer
            {
                PlayerId = player.PlayerId,
                Nav = settlement.MonthNavs.GetValueOrDefault(player.Token, player.CalculateNAV(midPrice)),
                Mora = player.Mora,
                Gold = player.Gold,
                FrozenMora = player.FrozenMora,
                FrozenGold = player.FrozenGold,
                LockedGold = player.LockedGold,
                TradeCount = player.MonthlyTradeCount,
                ActiveCards = player.ActiveCards.Select(card => card.Name).ToList()
            })
            .OrderByDescending(player => player.Nav)
            .ThenByDescending(player => player.TradeCount)
            .ToList();

        var winnerPlayerId = game.FindPlayer(settlement.WinnerToken)?.PlayerId ?? -1;
        var finalBonusWinnerPlayerId = game.FindPlayer(settlement.FinalBonusWinnerToken)?.PlayerId ?? -1;
        var cumulativeNavsById = new Dictionary<int, long>();
        foreach (var (token, nav) in settlement.CumulativeNavs)
        {
            var player = game.FindPlayer(token);
            if (player != null)
                cumulativeNavsById[player.PlayerId] = nav;
        }

        return new DaySettlementMessage
        {
            Day = settlement.Month,
            Month = settlement.Month,
            WinnerPlayerId = winnerPlayerId,
            Reason = settlement.Reason,
            Players = players,
            CumulativeNavs = cumulativeNavsById,
            FinalBonusWinnerPlayerId = finalBonusWinnerPlayerId,
            FinalBonusPoints = settlement.FinalBonusPoints
        };
    }

    private static void RecordGameState(Recorder.Recorder recorder, Game game)
    {
        var snapshot = new
        {
            Tick = game.CurrentTick,
            Stage = game.Stage.ToString(),
            Month = game.CurrentMonthNumber,
            Day = game.CurrentDayNumber,
            Scores = game.Scoreboard,
            TradingDayTick = game.CurrentTradingDay?.CurrentTick,
            MarketState = game.CurrentTradingDay != null ? new
            {
                game.CurrentTradingDay.OrderBook.LastPrice,
                MidPrice = game.CurrentTradingDay.OrderBook.MidPrice,
                Volume = game.CurrentTradingDay.OrderBook.TotalVolume
            } : null,
            Players = game.Players.Values.Select(p => new
            {
                Token = p.Token,
                Mora = p.Mora,
                Gold = p.Gold,
                FrozenMora = p.FrozenMora,
                FrozenGold = p.FrozenGold,
                LockedGold = p.LockedGold,
                Nav = game.CurrentTradingDay != null
                    ? p.CalculateNAV(game.CurrentTradingDay.OrderBook.MidPrice)
                    : p.Mora
            }).ToList()
        };

        recorder.Record(snapshot);
    }
}

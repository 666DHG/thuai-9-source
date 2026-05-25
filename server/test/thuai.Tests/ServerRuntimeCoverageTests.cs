using System.Collections.Concurrent;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Serilog;
using Thuai;
using Thuai.Connection;
using Thuai.GameLogic;
using Thuai.GameLogic.StrategyCards;
using Thuai.Protocol.Messages;
using Thuai.Utility;
using Controller = Thuai.GameController.GameController;
using RecorderService = Thuai.Recorder.Recorder;

namespace Thuai.Tests;

public class AdminCommandHandlerCoverageTests
{
    [Fact]
    public void Handle_CoversDebugCommandsAndValidationBranches()
    {
        var waitingGame = TestServerHelpers.CreateGameInWaitingState();
        var noTradingDay = Assert.IsType<DebugAckMessage>(AdminCommandHandler.Handle(
            waitingGame,
            new DebugInjectNewsMessage { Sentiment = "Bullish" }));
        Assert.False(noTradingDay.Ok);
        Assert.Equal("no trading day in progress", noTradingDay.Error);

        var advance = Assert.IsType<DebugAckMessage>(AdminCommandHandler.Handle(
            waitingGame,
            new DebugAdvanceStageMessage()));
        Assert.True(advance.Ok);
        waitingGame.Tick();
        Assert.Equal(GameStage.PreparingGame, waitingGame.Stage);

        var tradingGame = TestServerHelpers.CreateGameAtTradingDay();
        var query = Assert.IsType<DebugQueryResponseMessage>(AdminCommandHandler.Handle(
            tradingGame,
            new DebugQueryMessage()));
        Assert.Equal(GameStage.TradingDay.ToString(), query.Stage);
        Assert.Contains(query.Players!, player => player.Token == "alpha");
        Assert.NotNull(query.Draft);
        Assert.False(string.IsNullOrEmpty(query.Draft!.Infrastructure));
        Assert.False(string.IsNullOrEmpty(query.Draft.RiskControl));
        Assert.False(string.IsNullOrEmpty(query.Draft.FinTech));

        var unknown = Assert.IsType<DebugAckMessage>(AdminCommandHandler.Handle(
            tradingGame,
            new UnknownPerformMessage()));
        Assert.False(unknown.Ok);
        Assert.Equal("unknown debug command", unknown.Error);

        var giveMissingPlayer = Assert.IsType<DebugAckMessage>(AdminCommandHandler.Handle(
            tradingGame,
            new DebugGiveCardMessage { TargetPlayerId = 999, CardName = "闪电交易" }));
        Assert.False(giveMissingPlayer.Ok);

        var giveUnknownCard = Assert.IsType<DebugAckMessage>(AdminCommandHandler.Handle(
            tradingGame,
            new DebugGiveCardMessage { TargetPlayerId = 0, CardName = "不存在的卡" }));
        Assert.False(giveUnknownCard.Ok);

        var availableCard = new[] { "内幕消息", "闪电交易", "止损名刀", "定向增发", "网络风暴", "舆情打击" }
            .First(cardName => tradingGame.FindPlayer("alpha")!.ActiveCards.All(card => card.Name != cardName));

        var giveCard = Assert.IsType<DebugAckMessage>(AdminCommandHandler.Handle(
            tradingGame,
            new DebugGiveCardMessage { TargetPlayerId = 0, CardName = availableCard }));
        Assert.True(giveCard.Ok);
        Assert.Contains(tradingGame.FindPlayer("alpha")!.ActiveCards, card => card.Name == availableCard);

        var duplicateCard = Assert.IsType<DebugAckMessage>(AdminCommandHandler.Handle(
            tradingGame,
            new DebugGiveCardMessage { TargetPlayerId = 0, CardName = availableCard }));
        Assert.False(duplicateCard.Ok);

        var invalidSentiment = Assert.IsType<DebugAckMessage>(AdminCommandHandler.Handle(
            tradingGame,
            new DebugInjectNewsMessage { Sentiment = "Sideways" }));
        Assert.False(invalidSentiment.Ok);

        var injectNews = Assert.IsType<DebugAckMessage>(AdminCommandHandler.Handle(
            tradingGame,
            new DebugInjectNewsMessage { Sentiment = "Bullish" }));
        Assert.True(injectNews.Ok);
        Assert.NotNull(tradingGame.CurrentTradingDay!.NewsSystem.LatestNews);
        Assert.True(tradingGame.CurrentTradingDay.NewsSystem.LatestNews!.IsFake);

        var advanceWrongStage = Assert.IsType<DebugAckMessage>(AdminCommandHandler.Handle(
            tradingGame,
            new DebugAdvanceStageMessage()));
        Assert.False(advanceWrongStage.Ok);

        var setMissingPlayer = Assert.IsType<DebugAckMessage>(AdminCommandHandler.Handle(
            tradingGame,
            new DebugSetPlayerMessage { TargetPlayerId = 999, Mora = 10 }));
        Assert.False(setMissingPlayer.Ok);

        var setPlayer = Assert.IsType<DebugAckMessage>(AdminCommandHandler.Handle(
            tradingGame,
            new DebugSetPlayerMessage { TargetPlayerId = 0, Mora = 54321, Gold = 12 }));
        Assert.True(setPlayer.Ok);
        Assert.Equal(54321, tradingGame.FindPlayer("alpha")!.Mora);
        Assert.Equal(12, tradingGame.FindPlayer("alpha")!.Gold);
    }
}

public class GameControllerRuntimeCoverageTests
{
    [Fact]
    public void HandleAfterPlayerConnectEvent_QueuesNewPlayersOnlyOnce()
    {
        var controller = new Controller(TestServerHelpers.FastSettings());

        var firstSocket = Guid.NewGuid();
        controller.HandleAfterPlayerConnectEvent(null,
            new AgentServer.AfterPlayerConnectEventArgs { SocketId = firstSocket, Token = "alpha" });
        controller.HandleAfterPlayerConnectEvent(null,
            new AgentServer.AfterPlayerConnectEventArgs { SocketId = Guid.NewGuid(), Token = "alpha" });
        controller.HandleAfterPlayerConnectEvent(null,
            new AgentServer.AfterPlayerConnectEventArgs { SocketId = Guid.NewGuid(), Token = "beta" });

        controller.Game.Tick();

        Assert.Equal(2, controller.Game.Players.Count);
        Assert.Equal("alpha", controller.Game.Players["alpha"].Token);
        Assert.Equal("beta", controller.Game.Players["beta"].Token);
    }

    [Fact]
    public void HandleAfterMessageReceiveEvent_RoutesStrategyAndTradingActions()
    {
        var controller = new Controller(TestServerHelpers.FastSettings());
        controller.Game.Initialize();
        controller.HandleAfterPlayerConnectEvent(null,
            new AgentServer.AfterPlayerConnectEventArgs { SocketId = Guid.NewGuid(), Token = "alpha" });
        controller.HandleAfterPlayerConnectEvent(null,
            new AgentServer.AfterPlayerConnectEventArgs { SocketId = Guid.NewGuid(), Token = "beta" });

        controller.Game.Tick();
        controller.Game.Tick();
        Assert.Equal(GameStage.StrategySelection, controller.Game.Stage);

        var options = controller.Game.CardManager.GetCurrentDraftOptionNames();
        controller.HandleAfterMessageReceiveEvent(null, new AgentServer.AfterMessageReceiveEventArgs
        {
            SocketId = Guid.NewGuid(),
            Message = new SelectStrategyMessage { Token = "alpha", CardName = options[0] }
        });
        controller.HandleAfterMessageReceiveEvent(null, new AgentServer.AfterMessageReceiveEventArgs
        {
            SocketId = Guid.NewGuid(),
            Message = new SelectStrategyMessage { Token = "beta", CardName = options[0] }
        });

        controller.Game.Tick();
        Assert.Equal(GameStage.TradingDay, controller.Game.Stage);

        var day = controller.Game.CurrentTradingDay!;
        var alpha = controller.Game.FindPlayer("alpha")!;
        alpha.ActiveCards.Add(new FlashTrading());

        controller.HandleAfterMessageReceiveEvent(null, new AgentServer.AfterMessageReceiveEventArgs
        {
            SocketId = Guid.NewGuid(),
            Message = new LimitBuyMessage { Token = "alpha", Price = 999, Quantity = 3 }
        });
        var buyOrderId = day.MatchEngine.GetPendingOrders("alpha").Single().OrderId;

        controller.HandleAfterMessageReceiveEvent(null, new AgentServer.AfterMessageReceiveEventArgs
        {
            SocketId = Guid.NewGuid(),
            Message = new LimitSellMessage { Token = "beta", Price = 1001, Quantity = 2 }
        });
        Assert.Single(day.MatchEngine.GetPendingOrders("beta"));

        controller.HandleAfterMessageReceiveEvent(null, new AgentServer.AfterMessageReceiveEventArgs
        {
            SocketId = Guid.NewGuid(),
            Message = new CancelOrderMessage { Token = "alpha", OrderId = buyOrderId }
        });
        Assert.Empty(day.MatchEngine.GetPendingOrders("alpha"));

        var news = day.NewsSystem.InjectFakeNews(0, "DEBUG", NewsSentiment.Bullish);
        controller.HandleAfterMessageReceiveEvent(null, new AgentServer.AfterMessageReceiveEventArgs
        {
            SocketId = Guid.NewGuid(),
            Message = new SubmitReportMessage { Token = "alpha", NewsId = news.NewsId, Prediction = "???invalid" }
        });
        Assert.Empty(day.ResearchSystem.PendingReports);

        controller.HandleAfterMessageReceiveEvent(null, new AgentServer.AfterMessageReceiveEventArgs
        {
            SocketId = Guid.NewGuid(),
            Message = new SubmitReportMessage { Token = "alpha", NewsId = news.NewsId, Prediction = "Long" }
        });
        Assert.Single(day.ResearchSystem.PendingReports);

        var moraBeforeSkill = alpha.Mora;
        controller.HandleAfterMessageReceiveEvent(null, new AgentServer.AfterMessageReceiveEventArgs
        {
            SocketId = Guid.NewGuid(),
            Message = new ActivateSkillMessage { Token = "alpha", SkillName = "闪电交易" }
        });
        Assert.Equal(moraBeforeSkill - 1000, alpha.Mora);

        controller.HandleAfterMessageReceiveEvent(null, new AgentServer.AfterMessageReceiveEventArgs
        {
            SocketId = Guid.NewGuid(),
            Message = new UnknownPerformMessage { Token = "alpha" }
        });
        controller.HandleAfterMessageReceiveEvent(null, new AgentServer.AfterMessageReceiveEventArgs
        {
            SocketId = Guid.NewGuid(),
            Message = new LimitBuyMessage { Token = "ghost", Price = 1000, Quantity = 1 }
        });
    }

    [Fact]
    public void Start_CompletesShortGameAndStopLeavesControllerStopped()
    {
        var settings = new GameSettings
        {
            MinimumPlayerCount = 1,
            PlayerWaitingTicks = 1,
            StrategySelectionTicks = 0,
            TradingDayTicks = 3,
            TradingDayCount = 1,
            InitialGoldPrice = 1000,
            NewsIntervalMin = 1,
            NewsIntervalMax = 1,
            ResearchWindowTicks = 2,
            ResearchSettlementDelay = 3,
            BaseResearchReward = 100,
            NpcOrdersPerTick = 0,
            TicksPerSecond = 1000
        };

        var controller = new Controller(settings);
        Assert.False(controller.IsRunning);
        Assert.NotNull(controller.Game);
        Assert.True(controller.Game.AddPlayer("solo"));

        controller.Start();

        Assert.True(SpinWait.SpinUntil(() => !controller.IsRunning, TimeSpan.FromSeconds(5)));
        Assert.Equal(GameStage.Finished, controller.Game.Stage);

        controller.Stop();
        Assert.False(controller.IsRunning);
    }
}

public class AgentServerRuntimeCoverageTests
{
    [Fact]
    public void ParseMessage_CoversHelloRoutingCompatibilityAndAdminGuards()
    {
        var server = new AgentServer { AdminSecret = "secret" };
        server.RegisterValidToken("alpha");
        server.RegisterValidToken("beta");

        var connectEvents = new List<string>();
        var playerMessages = new List<PerformMessage>();
        var adminMessages = new List<PerformMessage>();
        server.AfterPlayerConnectEvent += (_, e) => connectEvents.Add(e.Token);
        server.AfterMessageReceiveEvent += (_, e) => playerMessages.Add(e.Message);
        server.AfterAdminMessageEvent += (_, e) => adminMessages.Add(e.Message);

        var roles = TestServerHelpers.GetPrivateField<ConcurrentDictionary<Guid, SocketRole>>(server, "_socketRoles");
        var tokens = TestServerHelpers.GetPrivateField<ConcurrentDictionary<Guid, string>>(server, "_socketTokens");

        var playerSocket = Guid.NewGuid();
        TestServerHelpers.InvokePrivate(server, "ParseMessage", playerSocket,
            new HelloMessage { Role = "player", Token = "ghost" }.Json);
        Assert.False(roles.ContainsKey(playerSocket));

        TestServerHelpers.InvokePrivate(server, "ParseMessage", playerSocket,
            new HelloMessage { Role = "player", Token = "alpha" }.Json);
        Assert.Equal(SocketRole.Player, roles[playerSocket]);
        Assert.Equal("alpha", tokens[playerSocket]);
        Assert.Single(connectEvents);

        TestServerHelpers.InvokePrivate(server, "ParseMessage", playerSocket,
            new HelloMessage { Role = "observer" }.Json);
        Assert.Equal(SocketRole.Player, roles[playerSocket]);

        var observerSocket = Guid.NewGuid();
        TestServerHelpers.InvokePrivate(server, "ParseMessage", observerSocket,
            new HelloMessage { Role = "observer" }.Json);
        Assert.Equal(SocketRole.Observer, roles[observerSocket]);

        var badAdminSocket = Guid.NewGuid();
        TestServerHelpers.InvokePrivate(server, "ParseMessage", badAdminSocket,
            new HelloMessage { Role = "admin", AdminSecret = "wrong" }.Json);
        Assert.False(roles.ContainsKey(badAdminSocket));

        var adminSocket = Guid.NewGuid();
        TestServerHelpers.InvokePrivate(server, "ParseMessage", adminSocket,
            new HelloMessage { Role = "admin", AdminSecret = "secret" }.Json);
        Assert.Equal(SocketRole.Admin, roles[adminSocket]);

        var unknownRoleSocket = Guid.NewGuid();
        TestServerHelpers.InvokePrivate(server, "ParseMessage", unknownRoleSocket,
            new HelloMessage { Role = "mystery" }.Json);
        Assert.False(roles.ContainsKey(unknownRoleSocket));

        var compatibilitySocket = Guid.NewGuid();
        TestServerHelpers.InvokePrivate(server, "ParseMessage", compatibilitySocket,
            new LimitBuyMessage { Token = "beta", Price = 998, Quantity = 1 }.Json);
        Assert.Equal(SocketRole.Player, roles[compatibilitySocket]);
        Assert.Equal("beta", tokens[compatibilitySocket]);
        Assert.Equal(2, connectEvents.Count);
        Assert.Single(playerMessages, message => message is LimitBuyMessage buy && buy.Token == "beta");

        TestServerHelpers.InvokePrivate(server, "ParseMessage", playerSocket,
            new LimitBuyMessage { Token = "beta", Price = 997, Quantity = 1 }.Json);
        TestServerHelpers.InvokePrivate(server, "ParseMessage", observerSocket,
            new LimitBuyMessage { Token = "alpha", Price = 996, Quantity = 1 }.Json);
        TestServerHelpers.InvokePrivate(server, "ParseMessage", adminSocket,
            new CancelOrderMessage { Token = "ghost", OrderId = 1 }.Json);

        var actionCount = playerMessages.Count;
        TestServerHelpers.InvokePrivate(server, "ParseMessage", adminSocket,
            new CancelOrderMessage { Token = "alpha", OrderId = 1 }.Json);
        Assert.Equal(actionCount + 1, playerMessages.Count);

        TestServerHelpers.InvokePrivate(server, "ParseMessage", playerSocket,
            new DebugQueryMessage { Token = "alpha" }.Json);
        Assert.Empty(adminMessages);

        TestServerHelpers.InvokePrivate(server, "ParseMessage", adminSocket,
            new DebugQueryMessage { Token = "alpha" }.Json);

        TestServerHelpers.InvokePrivate(server, "ParseMessage", Guid.NewGuid(), "{}");
        TestServerHelpers.InvokePrivate(server, "ParseMessage", Guid.NewGuid(),
            """{"messageType":"BOGUS","token":"alpha"}""");
    }

    [Fact]
    public void ParseMessage_AcceptsAnyNonEmptyTokenWhenConfigured()
    {
        var server = new AgentServer
        {
            AdminSecret = "secret",
            AcceptAnyToken = true
        };

        var connectEvents = new List<string>();
        var routedMessages = new List<PerformMessage>();
        server.AfterPlayerConnectEvent += (_, e) => connectEvents.Add(e.Token);
        server.AfterMessageReceiveEvent += (_, e) => routedMessages.Add(e.Message);

        var roles = TestServerHelpers.GetPrivateField<ConcurrentDictionary<Guid, SocketRole>>(server, "_socketRoles");
        var tokens = TestServerHelpers.GetPrivateField<ConcurrentDictionary<Guid, string>>(server, "_socketTokens");

        var playerSocket = Guid.NewGuid();
        TestServerHelpers.InvokePrivate(server, "ParseMessage", playerSocket,
            new HelloMessage { Role = "player", Token = "ghost" }.Json);
        Assert.Equal(SocketRole.Player, roles[playerSocket]);
        Assert.Equal("ghost", tokens[playerSocket]);

        var blankTokenSocket = Guid.NewGuid();
        TestServerHelpers.InvokePrivate(server, "ParseMessage", blankTokenSocket,
            new HelloMessage { Role = "player", Token = "   " }.Json);
        Assert.False(roles.ContainsKey(blankTokenSocket));

        var compatibilitySocket = Guid.NewGuid();
        TestServerHelpers.InvokePrivate(server, "ParseMessage", compatibilitySocket,
            new LimitBuyMessage { Token = "late-joiner", Price = 998, Quantity = 1 }.Json);
        Assert.Equal(SocketRole.Player, roles[compatibilitySocket]);
        Assert.Equal("late-joiner", tokens[compatibilitySocket]);
        Assert.Contains(routedMessages, message => message is LimitBuyMessage buy && buy.Token == "late-joiner");

        var adminSocket = Guid.NewGuid();
        TestServerHelpers.InvokePrivate(server, "ParseMessage", adminSocket,
            new HelloMessage { Role = "admin", AdminSecret = "secret" }.Json);
        var routedCount = routedMessages.Count;
        TestServerHelpers.InvokePrivate(server, "ParseMessage", adminSocket,
            new CancelOrderMessage { Token = "ghost-admin-target", OrderId = 1 }.Json);
        Assert.Equal(routedCount + 1, routedMessages.Count);

        Assert.Equal(new[] { "ghost", "late-joiner" }, connectEvents);
    }

    [Fact]
    public async Task QueueingPublishAndCleanupPaths_CoverInternalSocketState()
    {
        var server = new AgentServer();
        server.RegisterValidToken("alpha");
        server.RegisterValidToken("");

        var validTokens = TestServerHelpers.GetPrivateField<ConcurrentDictionary<string, byte>>(server, "_validTokens");
        Assert.Single(validTokens);
        Assert.True(validTokens.ContainsKey("alpha"));

        var rawQueues = TestServerHelpers.GetPrivateField<ConcurrentDictionary<Guid, ConcurrentQueue<string>>>(
            server, "_socketRawTextReceivingQueue");
        var sendQueues = TestServerHelpers.GetPrivateField<ConcurrentDictionary<Guid, ConcurrentQueue<Message>>>(
            server, "_socketMessageSendingQueue");
        var roles = TestServerHelpers.GetPrivateField<ConcurrentDictionary<Guid, SocketRole>>(server, "_socketRoles");
        var tokens = TestServerHelpers.GetPrivateField<ConcurrentDictionary<Guid, string>>(server, "_socketTokens");
        var ctsMap = TestServerHelpers.GetPrivateField<ConcurrentDictionary<Guid, CancellationTokenSource>>(
            server, "_cancellationTokenSources");
        var parseTasks = TestServerHelpers.GetPrivateField<ConcurrentDictionary<Guid, Task>>(server, "_tasksForParsingMessage");
        var sendTasks = TestServerHelpers.GetPrivateField<ConcurrentDictionary<Guid, Task>>(server, "_tasksForSendingMessage");
        var sockets = TestServerHelpers.GetPrivateField<ConcurrentDictionary<Guid, Fleck.IWebSocketConnection>>(
            server, "_sockets");

        var playerSocket = Guid.NewGuid();
        var observerSocket = Guid.NewGuid();
        var adminSocket = Guid.NewGuid();
        var newPlayerSocket = Guid.NewGuid();
        var anonymousSocket = Guid.NewGuid();

        rawQueues[playerSocket] = new ConcurrentQueue<string>();
        sendQueues[playerSocket] = new ConcurrentQueue<Message>();
        sendQueues[observerSocket] = new ConcurrentQueue<Message>();
        sendQueues[adminSocket] = new ConcurrentQueue<Message>();
        sendQueues[newPlayerSocket] = new ConcurrentQueue<Message>();
        roles[playerSocket] = SocketRole.Player;
        roles[observerSocket] = SocketRole.Observer;
        roles[adminSocket] = SocketRole.Admin;
        roles[anonymousSocket] = SocketRole.Unidentified;
        tokens[playerSocket] = "alpha";
        ctsMap[playerSocket] = new CancellationTokenSource();
        ctsMap[anonymousSocket] = new CancellationTokenSource();
        parseTasks[playerSocket] = Task.CompletedTask;
        sendTasks[playerSocket] = Task.CompletedTask;
        sockets[playerSocket] = null!;

        for (int i = 0; i < 12; i++)
        {
            TestServerHelpers.InvokePrivate(server, "OnMessageReceived", playerSocket, $"msg-{i}");
        }

        Assert.Equal(11, rawQueues[playerSocket].Count);

        server.HandleAfterPlayerConnectEvent(null, new AgentServer.AfterPlayerConnectEventArgs
        {
            SocketId = newPlayerSocket,
            Token = "beta"
        });
        Assert.Equal(SocketRole.Player, roles[newPlayerSocket]);
        Assert.Equal("beta", tokens[newPlayerSocket]);

        var direct = new ErrorMessage { ErrorCode = 400, ErrorText = "direct" };
        var privateMessage = new PlayerStateMessage { Mora = 1, Gold = 2 };
        var publicMessage = new GameStateMessage { Stage = "TradingDay" };
        var observerMessage = new MarketStateMessage { Tick = 1 };

        server.PublishToSocket(direct, playerSocket);
        server.Publish(privateMessage, "alpha");
        server.PublishToAll(publicMessage);
        server.PublishToObservers(observerMessage);

        Assert.Equal(3, sendQueues[playerSocket].Count);
        Assert.Equal(2, sendQueues[adminSocket].Count);
        Assert.Equal(2, sendQueues[observerSocket].Count);
        Assert.Single(sendQueues[newPlayerSocket]);

        var disconnected = new List<string>();
        server.AfterPlayerDisconnectEvent += (_, e) => disconnected.Add(e.Token);

        var playerCts = ctsMap[playerSocket];
        TestServerHelpers.InvokePrivate(server, "RemoveSocket", playerSocket);
        Assert.True(playerCts.IsCancellationRequested);
        Assert.Single(disconnected);
        Assert.DoesNotContain(playerSocket, rawQueues.Keys);
        Assert.DoesNotContain(playerSocket, sendQueues.Keys);
        Assert.DoesNotContain(playerSocket, roles.Keys);
        Assert.DoesNotContain(playerSocket, tokens.Keys);
        Assert.DoesNotContain(playerSocket, parseTasks.Keys);
        Assert.DoesNotContain(playerSocket, sendTasks.Keys);
        Assert.DoesNotContain(playerSocket, ctsMap.Keys);

        TestServerHelpers.InvokePrivate(server, "RemoveSocket", anonymousSocket);

        await TestServerHelpers.InvokePrivateTask(server, "ParseMessageLoop", Guid.NewGuid(), CancellationToken.None);
        await TestServerHelpers.InvokePrivateTask(server, "SendMessageLoop", Guid.NewGuid(), CancellationToken.None);

        var finalCts = new CancellationTokenSource();
        ctsMap[Guid.NewGuid()] = finalCts;
        server.Stop();
        Assert.True(finalCts.IsCancellationRequested);
    }
}

public class ProgramRuntimeCoverageTests
{
    [Fact]
    public void SetupLogger_WritesConfiguredLogFiles()
    {
        var firstDir = TestServerHelpers.TempDir();
        var secondDir = TestServerHelpers.TempDir();

        try
        {
            TestServerHelpers.InvokePrivateStatic(typeof(Program), "SetupLogger", new LogSettings
            {
                Target = "file",
                MinimumLevel = "mystery",
                TargetDirectory = firstDir,
                RollingInterval = "mystery"
            });
            Log.Information("file target");
            Log.CloseAndFlush();
            Assert.Contains(Directory.GetFiles(firstDir), path => Path.GetFileName(path).StartsWith("thuai-"));

            TestServerHelpers.InvokePrivateStatic(typeof(Program), "SetupLogger", new LogSettings
            {
                Target = "both",
                MinimumLevel = "debug",
                TargetDirectory = secondDir,
                RollingInterval = "hour"
            });
            Log.Information("both target");
            Log.CloseAndFlush();
            Assert.Contains(Directory.GetFiles(secondDir), path => Path.GetFileName(path).StartsWith("thuai-"));
        }
        finally
        {
            Directory.Delete(firstDir, recursive: true);
            Directory.Delete(secondDir, recursive: true);
        }
    }

    [Fact]
    public void BroadcastGameState_PublishesStrategySelectionSnapshotToAllIdentifiedSockets()
    {
        var server = TestServerHelpers.CreateServerWithQueues(
            ("alpha-player", SocketRole.Player, "alpha"),
            ("observer", SocketRole.Observer, null),
            ("admin", SocketRole.Admin, null),
            ("anonymous", SocketRole.Unidentified, null));
        var game = TestServerHelpers.CreateGameAtStrategySelection();

        TestServerHelpers.InvokePrivateStatic(typeof(Program), "BroadcastGameState", server, game);

        Assert.Collection(TestServerHelpers.GetQueuedMessageTypes(server, "alpha-player"),
            type => Assert.Equal("GAME_STATE", type),
            type => Assert.Equal("STRATEGY_OPTIONS", type));
        Assert.Collection(TestServerHelpers.GetQueuedMessageTypes(server, "observer"),
            type => Assert.Equal("GAME_STATE", type),
            type => Assert.Equal("STRATEGY_OPTIONS", type));
        Assert.Collection(TestServerHelpers.GetQueuedMessageTypes(server, "admin"),
            type => Assert.Equal("GAME_STATE", type),
            type => Assert.Equal("STRATEGY_OPTIONS", type));
        Assert.Empty(TestServerHelpers.GetQueuedMessageTypes(server, "anonymous"));
    }

    [Fact]
    public void BroadcastGameState_PublishesTradingDayNotificationsSettlementAndPrivateViews()
    {
        var server = TestServerHelpers.CreateServerWithQueues(
            ("alpha-player", SocketRole.Player, "alpha"),
            ("beta-player", SocketRole.Player, "beta"),
            ("observer", SocketRole.Observer, null),
            ("admin", SocketRole.Admin, null));
        var game = TestServerHelpers.CreateGameAtTradingDay();
        var day = game.CurrentTradingDay!;

        game.FindPlayer("alpha")!.ActiveCards.Add(new FlashTrading());
        day.HandleLimitBuy("alpha", 999, 1);

        TestServerHelpers.SetPrivateField(game, "<HasPendingSettlementNotification>k__BackingField", true);
        TestServerHelpers.SetPrivateField(game, "<LatestSettlement>k__BackingField",
            new MonthSettlementResult(
                1,
                new Dictionary<string, long> { ["alpha"] = 1010, ["beta"] = 990 },
                new Dictionary<string, long> { ["alpha"] = 1010, ["beta"] = 990 },
                "alpha",
                "higher NAV",
                "",
                0));

        TestServerHelpers.SetPrivateField(day, "_hasPendingNotifications", true);
        TestServerHelpers.GetPrivateField<List<News>>(day, "_publishedNewsThisDay").Add(new News
        {
            NewsId = 1,
            PublishTick = 2,
            Content = "real-news",
            Sentiment = NewsSentiment.Bullish,
            IsFake = false
        });
        TestServerHelpers.GetPrivateField<List<(string PlayerToken, News Preview)>>(day, "_pendingInsiderPreviews")
            .Add(("alpha", new News
            {
                NewsId = 2,
                PublishTick = 3,
                Content = "preview-news",
                Sentiment = NewsSentiment.Bearish,
                IsFake = false
            }));
        TestServerHelpers.GetPrivateField<List<ResearchReport>>(day, "_settledReportsThisDay").Add(new ResearchReport
        {
            PlayerToken = "alpha",
            NewsId = 1,
            Prediction = Prediction.Long,
            SubmissionRank = 1,
            SubmitTick = 2,
            SettlementDay = 3,
            IsCorrect = true,
            Reward = 88,
            ActualChange = 50
        });
        TestServerHelpers.GetPrivateField<List<Trade>>(day, "_tradesThisDay").Add(new Trade
        {
            BuyOrderId = 10,
            SellOrderId = 11,
            BuyerToken = "alpha",
            SellerToken = "beta",
            Price = 1005,
            Quantity = 2,
            Tick = 3,
            BuyerFee = 1,
            SellerFee = 2
        });
        TestServerHelpers.GetPrivateField<List<SkillActivation>>(day, "_skillEffectsThisDay").Add(
            new SkillActivation(0, "闪电交易", "bonus action"));

        TestServerHelpers.InvokePrivateStatic(typeof(Program), "BroadcastGameState", server, game);

        Assert.False(game.HasPendingSettlementNotification);
        Assert.False(day.HasPendingNotifications);
        Assert.Empty(day.PublishedNewsThisDay);
        Assert.Empty(day.PendingInsiderPreviews);
        Assert.Empty(day.SettledReportsThisDay);
        Assert.Empty(day.TradesThisDay);
        Assert.Empty(day.SkillEffectsThisDay);

        var alphaMessages = TestServerHelpers.GetQueuedMessages(server, "alpha-player");
        var alphaTypes = alphaMessages.Select(message => message.MessageType).ToList();
        Assert.Contains("GAME_STATE", alphaTypes);
        Assert.Contains("DAY_SETTLEMENT", alphaTypes);
        Assert.Contains("MARKET_STATE", alphaTypes);
        Assert.Contains("PLAYER_STATE", alphaTypes);
        Assert.Contains("REPORT_RESULT", alphaTypes);
        Assert.Equal(2, alphaMessages.OfType<NewsBroadcastMessage>().Count());
        Assert.Contains(alphaMessages.OfType<NewsBroadcastMessage>(), message => message.Content == "preview-news");
        Assert.Contains(alphaMessages.OfType<TradeNotificationMessage>(), message => message.Side == "Buy");

        var betaMessages = TestServerHelpers.GetQueuedMessages(server, "beta-player");
        Assert.Contains(betaMessages.OfType<TradeNotificationMessage>(), message => message.Side == "Sell");
        Assert.Single(betaMessages.OfType<PlayerStateMessage>());

        var observerTypes = TestServerHelpers.GetQueuedMessageTypes(server, "observer");
        Assert.Contains("GAME_STATE", observerTypes);
        Assert.Contains("NEWS_BROADCAST", observerTypes);
        Assert.Contains("DAY_SETTLEMENT", observerTypes);
        Assert.Contains("MARKET_STATE", observerTypes);
        Assert.Contains("SKILL_EFFECT", observerTypes);
        Assert.DoesNotContain("PLAYER_STATE", observerTypes);
        Assert.DoesNotContain("REPORT_RESULT", observerTypes);
        Assert.DoesNotContain("TRADE_NOTIFICATION", observerTypes);

        var adminTypes = TestServerHelpers.GetQueuedMessageTypes(server, "admin");
        Assert.True(adminTypes.Count(type => type == "PLAYER_STATE") >= 2);
        Assert.Contains("REPORT_RESULT", adminTypes);
        Assert.Contains("TRADE_NOTIFICATION", adminTypes);
        Assert.Contains("NEWS_BROADCAST", adminTypes);
    }

    [Fact]
    public void RecordGameState_WritesReplaySnapshotFromCurrentTradingState()
    {
        var dir = TestServerHelpers.TempDir();
        try
        {
            var game = TestServerHelpers.CreateGameAtTradingDay();
            game.FindPlayer("alpha")!.AddMora(77);

            using (var recorder = new RecorderService(dir))
            {
                TestServerHelpers.InvokePrivateStatic(typeof(Program), "RecordGameState", recorder, game);
                recorder.Flush();
            }

            using var archive = ZipFile.OpenRead(Path.Combine(dir, "replay.dat"));
            using var entryStream = archive.GetEntry("1.json")!.Open();
            using var reader = new StreamReader(entryStream);
            using var document = JsonDocument.Parse(reader.ReadToEnd());
            var snapshot = document.RootElement[0];

            Assert.Equal(game.Stage.ToString(), snapshot.GetProperty("stage").GetString());
            Assert.Equal(game.CurrentMonthNumber, snapshot.GetProperty("month").GetInt32());
            Assert.Equal(game.CurrentDayNumber, snapshot.GetProperty("day").GetInt32());
            Assert.Equal(game.CurrentTradingDay!.CurrentTick, snapshot.GetProperty("tradingDayTick").GetInt32());
            Assert.Equal(game.CurrentTradingDay.OrderBook.LastPrice, snapshot.GetProperty("marketState").GetProperty("lastPrice").GetInt64());
            Assert.Contains(snapshot.GetProperty("players").EnumerateArray(),
                player => player.GetProperty("token").GetString() == "alpha");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

public record UnknownPerformMessage : PerformMessage
{
    public override string MessageType => "UNKNOWN";
}

internal static class TestServerHelpers
{
    public static GameSettings FastSettings() => new()
    {
        MinimumPlayerCount = 2,
        PlayerWaitingTicks = 1,
        StrategySelectionTicks = 1,
        TradingDayTicks = 3,
        TradingDayCount = 1,
        InitialGoldPrice = 1000,
        NewsIntervalMin = 1,
        NewsIntervalMax = 1,
        ResearchWindowTicks = 2,
        ResearchSettlementDelay = 3,
        BaseResearchReward = 100,
        NpcOrdersPerTick = 0
    };

    public static Game CreateGameInWaitingState()
    {
        var game = new Game(FastSettings());
        Assert.True(game.AddPlayer("alpha"));
        Assert.True(game.AddPlayer("beta"));
        game.Initialize();
        return game;
    }

    public static Game CreateGameAtStrategySelection()
    {
        var game = CreateGameInWaitingState();
        game.Tick();
        game.Tick();
        Assert.Equal(GameStage.StrategySelection, game.Stage);
        return game;
    }

    public static Game CreateGameAtTradingDay()
    {
        var game = CreateGameAtStrategySelection();
        var options = game.CardManager.GetCurrentDraftOptionNames();
        Assert.True(game.SelectStrategy("alpha", options[0]));
        Assert.True(game.SelectStrategy("beta", options[0]));
        game.Tick();
        Assert.Equal(GameStage.TradingDay, game.Stage);
        return game;
    }

    public static AgentServer CreateServerWithQueues(params (string Name, SocketRole Role, string? Token)[] sockets)
    {
        var server = new AgentServer();
        var roles = GetPrivateField<ConcurrentDictionary<Guid, SocketRole>>(server, "_socketRoles");
        var tokens = GetPrivateField<ConcurrentDictionary<Guid, string>>(server, "_socketTokens");
        var sendQueues = GetPrivateField<ConcurrentDictionary<Guid, ConcurrentQueue<Message>>>(
            server, "_socketMessageSendingQueue");
        var names = new Dictionary<string, Guid>();

        foreach (var (name, role, token) in sockets)
        {
            var socketId = Guid.NewGuid();
            names[name] = socketId;
            roles[socketId] = role;
            sendQueues[socketId] = new ConcurrentQueue<Message>();
            if (!string.IsNullOrEmpty(token))
                tokens[socketId] = token;
        }

        SocketNameStore.Values.Remove(server);
        SocketNameStore.Values.Add(server, names);
        return server;
    }

    public static List<Message> GetQueuedMessages(AgentServer server, string socketName)
    {
        var socketId = GetNamedSocketIds(server)[socketName];
        var sendQueues = GetPrivateField<ConcurrentDictionary<Guid, ConcurrentQueue<Message>>>(
            server, "_socketMessageSendingQueue");
        return sendQueues[socketId].ToList();
    }

    public static List<string> GetQueuedMessageTypes(AgentServer server, string socketName) =>
        GetQueuedMessages(server, socketName).Select(message => message.MessageType).ToList();

    public static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "thuai-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static T GetPrivateField<T>(object instance, string name)
    {
        var field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(instance.GetType().FullName, name);
        return (T)field.GetValue(instance)!;
    }

    public static void SetPrivateField(object instance, string name, object? value)
    {
        var field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(instance.GetType().FullName, name);
        field.SetValue(instance, value);
    }

    public static object? InvokePrivate(object instance, string name, params object?[] args)
    {
        var method = instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(instance.GetType().FullName, name);
        return method.Invoke(instance, args);
    }

    public static async Task InvokePrivateTask(object instance, string name, params object?[] args)
    {
        var task = (Task)(InvokePrivate(instance, name, args)
            ?? throw new InvalidOperationException($"Method {name} returned null."));
        await task;
    }

    public static object? InvokePrivateStatic(Type type, string name, params object?[] args)
    {
        var method = type.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(type.FullName, name);
        return method.Invoke(null, args);
    }

    private static Dictionary<string, Guid> GetNamedSocketIds(AgentServer server)
    {
        if (SocketNameStore.Values.TryGetValue(server, out var names))
            return names;

        throw new InvalidOperationException("Named socket store was not initialized for this server instance.");
    }

    private static class SocketNameStore
    {
        public static readonly ConditionalWeakTable<AgentServer, Dictionary<string, Guid>> Values = new();
    }
}

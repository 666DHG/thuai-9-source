using System.IO.Compression;
using System.Text.Json;
using Thuai.GameLogic;
using Thuai.GameLogic.StrategyCards;
using Thuai.Protocol.Messages;
using Thuai.Recorder;
using Thuai.Utility;
using RecorderService = Thuai.Recorder.Recorder;

namespace Thuai.Tests;

public class UtilityCoverageTests
{
    [Fact]
    public void LoadOrCreateConfig_CreatesMissingFile()
    {
        var dir = TempDir();
        try
        {
            var path = Path.Combine(dir, "config.json");

            var config = Tools.LoadOrCreateConfig(path);

            Assert.True(File.Exists(path));
            Assert.Equal(14514, config.Server.Port);
            Assert.False(config.Server.AcceptAnyToken);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void LoadOrCreateConfig_ReadsExistingFile()
    {
        var dir = TempDir();
        try
        {
            var path = Path.Combine(dir, "config.json");
            File.WriteAllText(path, """
                {"server":{"port":20001,"acceptAnyToken":true},"token":{"loadTokenFromEnv":false,"tokenLocation":"tokens.txt","tokenDelimiter":"|"},"log":{"target":"File","minimumLevel":"Debug","targetDirectory":"./tmp","rollingInterval":"Hour"},"game":{"ticksPerSecond":30},"recorder":{"keepRecord":true}}
                """);

            var config = Tools.LoadOrCreateConfig(path);

            Assert.Equal(20001, config.Server.Port);
            Assert.True(config.Server.AcceptAnyToken);
            Assert.False(config.Token.LoadTokenFromEnv);
            Assert.Equal("|", config.Token.TokenDelimiter);
            Assert.True(config.Recorder.KeepRecord);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void LoadTokens_ReadsEnvironmentAndFileSources()
    {
        var envName = $"THUAI_TEST_{Guid.NewGuid():N}";
        var original = Environment.GetEnvironmentVariable(envName);
        var dir = TempDir();

        try
        {
            Environment.SetEnvironmentVariable(envName, "alpha, beta ,gamma");
            var fromEnv = Tools.LoadTokens(new TokenSettings
            {
                LoadTokenFromEnv = true,
                TokenLocation = envName,
                TokenDelimiter = ","
            });
            Assert.Equal(new[] { "alpha", "beta", "gamma" }, fromEnv);

            var tokenFile = Path.Combine(dir, "tokens.txt");
            File.WriteAllText(tokenFile, "x| y |z");
            var fromFile = Tools.LoadTokens(new TokenSettings
            {
                LoadTokenFromEnv = false,
                TokenLocation = tokenFile,
                TokenDelimiter = "|"
            });
            Assert.Equal(new[] { "x", "y", "z" }, fromFile);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envName, original);
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void LoadTokens_ReturnsEmptyWhenConfiguredSourceIsMissing()
    {
        var envName = $"THUAI_TEST_{Guid.NewGuid():N}";
        var original = Environment.GetEnvironmentVariable(envName);

        try
        {
            Environment.SetEnvironmentVariable(envName, null);
            var fromMissingEnv = Tools.LoadTokens(new TokenSettings
            {
                LoadTokenFromEnv = true,
                TokenLocation = envName,
                TokenDelimiter = ","
            });
            Assert.Empty(fromMissingEnv);

            var missingFile = Path.Combine(TempDir(), "missing-tokens.txt");
            var fromMissingFile = Tools.LoadTokens(new TokenSettings
            {
                LoadTokenFromEnv = false,
                TokenLocation = missingFile,
                TokenDelimiter = ","
            });
            Assert.Empty(fromMissingFile);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envName, original);
        }
    }

    [Fact]
    public void Config_RoundTripPreservesSections()
    {
        var config = new Config
        {
            Server = new ServerSettings { Port = 18080, AcceptAnyToken = true },
            Token = new TokenSettings { LoadTokenFromEnv = false, TokenLocation = "tokens.txt", TokenDelimiter = ";" },
            Log = new LogSettings { Target = "Both", MinimumLevel = "Warning", TargetDirectory = "./logs", RollingInterval = "Month" },
            Game = new GameSettings { TicksPerSecond = 20, InitialGold = 777 },
            Recorder = new RecorderSettings { KeepRecord = true }
        };

        var json = JsonSerializer.Serialize(config);
        var roundTrip = JsonSerializer.Deserialize<Config>(json)!;

        Assert.Equal(18080, roundTrip.Server.Port);
        Assert.True(roundTrip.Server.AcceptAnyToken);
        Assert.False(roundTrip.Token.LoadTokenFromEnv);
        Assert.Equal(";", roundTrip.Token.TokenDelimiter);
        Assert.Equal("Both", roundTrip.Log.Target);
        Assert.Equal(20, roundTrip.Game.TicksPerSecond);
        Assert.True(roundTrip.Recorder.KeepRecord);
    }

    [Fact]
    public void ClockProvider_StoresDelayAndCreatesCompletedClock()
    {
        var clock = new ClockProvider(0);

        Assert.Equal(0, clock.Milliseconds);
        Assert.True(clock.CreateClock().IsCompletedSuccessfully);
    }

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "thuai-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}

public class ProtocolCoverageTests
{
    [Fact]
    public void PerformMessages_IncludeDocumentedFields()
    {
        var sell = ParseJson(new LimitSellMessage
        {
            Token = "seller",
            Price = 123,
            Quantity = 4
        });
        Assert.Equal("LIMIT_SELL", sell.GetProperty("messageType").GetString());
        Assert.Equal(123, sell.GetProperty("price").GetInt64());
        Assert.Equal(4, sell.GetProperty("quantity").GetInt32());

        var select = ParseJson(new SelectStrategyMessage
        {
            Token = "player",
            CardName = "闪电交易"
        });
        Assert.Equal("SELECT_STRATEGY", select.GetProperty("messageType").GetString());
        Assert.Equal("闪电交易", select.GetProperty("cardName").GetString());
    }

    [Fact]
    public void BroadcastMessages_IncludeMarketTradeAndStrategyShapes()
    {
        var market = ParseJson(new MarketStateMessage
        {
            Bids = [new PriceLevel { Price = 980, Quantity = 10 }],
            Asks = [new PriceLevel { Price = 1020, Quantity = 12 }],
            LastPrice = 1000,
            MidPrice = 1000,
            Volume = 22,
            Tick = 7
        });
        Assert.Equal("MARKET_STATE", market.GetProperty("messageType").GetString());
        Assert.Equal(1, market.GetProperty("bids").GetArrayLength());
        Assert.Equal(980, market.GetProperty("bids")[0].GetProperty("price").GetInt64());

        var trade = ParseJson(new TradeNotificationMessage
        {
            TradeId = 99,
            OrderId = 11,
            Price = 1001,
            Quantity = 5,
            Side = "Buy",
            Fee = 2
        });
        Assert.Equal("TRADE_NOTIFICATION", trade.GetProperty("messageType").GetString());
        Assert.Equal(2, trade.GetProperty("fee").GetInt64());

        var options = ParseJson(new StrategyOptionsMessage
        {
            Infrastructure = new CardOption { Name = "内幕消息", Description = "d", Category = "Infrastructure" },
            RiskControl = new CardOption { Name = "止损名刀", Description = "d", Category = "RiskControl" },
            FinTech = new CardOption { Name = "网络风暴", Description = "d", Category = "FinTech" }
        });
        Assert.Equal("STRATEGY_OPTIONS", options.GetProperty("messageType").GetString());
        Assert.Equal("网络风暴", options.GetProperty("finTech").GetProperty("name").GetString());
    }

    [Fact]
    public void BroadcastMessages_IncludeSettlementAndErrorShapes()
    {
        var settlement = ParseJson(new DaySettlementMessage
        {
            Day = 2,
            Month = 5,
            WinnerPlayerId = 0,
            Reason = "higher NAV",
            Players =
            [
                new DaySettlementPlayer
                {
                    PlayerId = 0,
                    Nav = 1500,
                    Mora = 1000,
                    Gold = 2,
                    FrozenMora = 0,
                    FrozenGold = 0,
                    LockedGold = 0,
                    TradeCount = 4,
                    ActiveCards = ["闪电交易"]
                }
            ],
            CumulativeNavs = new Dictionary<int, long> { [0] = 2500 },
            FinalBonusWinnerPlayerId = 1,
            FinalBonusPoints = 2
        });
        Assert.Equal(2, settlement.GetProperty("day").GetInt32());
        Assert.Equal(1, settlement.GetProperty("finalBonusWinnerPlayerId").GetInt32());
        Assert.Equal(2500, settlement.GetProperty("cumulativeNavs").GetProperty("0").GetInt64());

        var error = ParseJson(new ErrorMessage
        {
            ErrorCode = 4001,
            ErrorText = "invalid card"
        });
        Assert.Equal("ERROR", error.GetProperty("messageType").GetString());
        Assert.Equal(4001, error.GetProperty("errorCode").GetInt32());
        Assert.Equal("invalid card", error.GetProperty("message").GetString());
    }

    private static JsonElement ParseJson(Message message)
    {
        using var document = JsonDocument.Parse(message.Json);
        return document.RootElement.Clone();
    }
}

public class RecorderCoverageTests
{
    [Fact]
    public void RecordPage_BuffersToJsonAndClears()
    {
        var page = new RecordPage();

        page.Enqueue("{\"a\":1}");
        page.Enqueue("{\"b\":2}");

        Assert.Equal(2, page.Length);
        Assert.Equal("[{\"a\":1},{\"b\":2}]", page.ToJson());

        page.Clear();

        Assert.Equal(0, page.Length);
        Assert.Equal("[]", page.ToJson());
    }

    [Fact]
    public void Recorder_FlushesAndSavesResults()
    {
        var dir = TempDir();
        try
        {
            using (var recorder = new RecorderService(dir))
            {
                recorder.Record(new { stage = "TradingDay", tick = 3 });
                recorder.SaveResults(new Dictionary<string, int> { ["alpha"] = 10 });
            }

            var replayFile = Path.Combine(dir, "replay.dat");
            var resultFile = Path.Combine(dir, "result.json");
            Assert.True(File.Exists(replayFile));
            Assert.True(File.Exists(resultFile));

            using var archive = ZipFile.OpenRead(replayFile);
            var entry = archive.GetEntry("1.json");
            Assert.NotNull(entry);
            using var reader = new StreamReader(entry!.Open());
            using var payload = JsonDocument.Parse(reader.ReadToEnd());
            Assert.Equal("TradingDay", payload.RootElement[0].GetProperty("stage").GetString());
            Assert.Equal(3, payload.RootElement[0].GetProperty("tick").GetInt32());

            using var result = JsonDocument.Parse(File.ReadAllText(resultFile));
            Assert.Equal(10, result.RootElement.GetProperty("scores").GetProperty("alpha").GetInt32());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Recorder_KeepRecordCreatesCopyArchive()
    {
        var dir = TempDir();
        try
        {
            using (var recorder = new RecorderService(dir, keepRecord: true))
            {
                recorder.Record(new { ok = true });
            }

            var copyRoot = Path.Combine(dir, "copy");
            Assert.True(Directory.Exists(copyRoot));
            Assert.NotEmpty(Directory.GetDirectories(copyRoot));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "thuai-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}

public class StrategyCardCoverageTests
{
    private sealed class DummyCard : StrategyCard
    {
        public override string Name => "Dummy";
        public override CardCategory Category => CardCategory.FinTech;
        public override string Description => "Dummy card";
        public override bool IsPassive => false;
        public override int Cooldown => 3;
    }

    [Fact]
    public void StrategyCardBase_DecaysCooldownAndReportsActivationState()
    {
        var card = new DummyCard { CurrentCooldown = 2 };

        Assert.False(card.CanActivate());
        card.OnTick(new Player("player", 0), 0);
        Assert.Equal(1, card.CurrentCooldown);
        card.StartCooldown();
        Assert.Equal(3, card.CurrentCooldown);
    }

    [Fact]
    public void InsiderInfo_ActivatePreviewAndResetMonthly()
    {
        var player = new Player("player", 0);
        var card = new InsiderInfo();

        Assert.True(card.CanActivate(currentDay: 0, nextNewsDay: 10));
        card.Activate(player, currentDay: 0, nextNewsDay: 10, cheapMode: false, previewIsFake: false);

        Assert.True(card.UsedThisMonth);
        Assert.Equal(10, card.PreviewNewsDay);
        Assert.Equal(7, card.PreviewDeliveryDay);
        Assert.True(player.HasInsiderPriorityDay(10));
        Assert.False(card.TryConsumePreview(6, out _));
        Assert.True(card.TryConsumePreview(7, out var isFake));
        Assert.False(isFake);

        card.ResetMonthly();
        Assert.False(card.UsedThisMonth);
        Assert.Equal(0, card.PreviewNewsDay);
    }

    [Fact]
    public void FlashTrading_ActivatesAndTicksBonusOrders()
    {
        var player = new Player("player", 0);
        var card = new FlashTrading();

        Assert.True(card.CanActivateThisMonth);
        card.OnActivate(player, 0);
        card.OnTick(player, 1);

        Assert.Equal(1, player.BonusImmediateOrdersToday);
        Assert.True(card.IsActive(1));

        card.ResetMonthly();
        Assert.True(card.CanActivateThisMonth);
        Assert.False(card.IsActive(1));
    }

    [Fact]
    public void StopLossBlade_ActivatesAndExpires()
    {
        var player = new Player("player", 0);
        var card = new StopLossBlade();

        card.Activate(player, 0, 1000);
        Assert.True(player.IsImmune);
        Assert.Equal(2, player.ImmuneUntilTick);

        card.OnTick(player, 1);
        Assert.True(player.IsImmune);
        card.OnTick(player, 3);
        Assert.False(player.IsImmune);
        Assert.Equal(0, player.ProtectedMidPrice);

        card.ResetMonthly();
        Assert.False(card.UsedThisMonth);
    }

    [Fact]
    public void RiskControlAndFinTechCardsExposeUsageState()
    {
        var targeted = new TargetedPurchase();
        Assert.False(targeted.IsUsed);
        targeted.MarkUsed();
        Assert.True(targeted.IsUsed);
        targeted.ResetMonthly();
        Assert.False(targeted.IsUsed);

        var storm = new NetworkStorm();
        Assert.Equal(1000, storm.ActivationCost);
        storm.MarkUsed();
        storm.MarkUsed();
        Assert.Equal(2, storm.UsesUsedThisGame);
        Assert.Equal(3000, storm.ActivationCost);
        Assert.True(storm.CanUse);

        var attack = new PublicOpinionAttack();
        Assert.True(attack.CanUse);
        attack.MarkUsed();
        Assert.False(attack.CanUse);
    }

    [Fact]
    public void StrategyCardManager_SelectsCopiesAndResetsMonthlyState()
    {
        var manager = new StrategyCardManager();
        Assert.True(manager.GenerateDraftOptions());

        var options = manager.GetCurrentDraftOptionNames();
        Assert.NotEmpty(options);

        var first = new Player("first", 0);
        var second = new Player("second", 1);
        var selectedFirst = manager.SelectCard(first, options[0]);
        Assert.NotNull(selectedFirst);
        Assert.Same(selectedFirst, StrategyCardManager.FindActiveCard(first, options[0]));
        Assert.Null(manager.SelectCard(first, options[0]));

        var selectedSecond = manager.SelectCard(second, options[0]);
        Assert.NotNull(selectedSecond);
        Assert.NotSame(selectedFirst, selectedSecond);

        var insider = new InsiderInfo();
        insider.Activate(first, 0, 10, cheapMode: false, previewIsFake: false);
        var flash = new FlashTrading();
        flash.OnActivate(first, 0);
        var blade = new StopLossBlade();
        blade.Activate(first, 0, 1000);
        var purchase = new TargetedPurchase();
        purchase.MarkUsed();
        var storm = new NetworkStorm();
        storm.MarkUsed();
        storm.CurrentCooldown = 2;
        var opinion = new PublicOpinionAttack();
        opinion.MarkUsed();
        opinion.CurrentCooldown = 3;

        first.ActiveCards.AddRange(new IStrategyCard[] { insider, flash, blade, purchase, storm, opinion });
        StrategyCardManager.ResetMonthlyCardState(first);

        Assert.False(insider.UsedThisMonth);
        Assert.True(flash.CanActivateThisMonth);
        Assert.False(blade.UsedThisMonth);
        Assert.False(purchase.IsUsed);
        Assert.Equal(0, storm.CurrentCooldown);
        Assert.Equal(0, opinion.CurrentCooldown);
    }
}

public class ResearchSystemCoverageTests
{
    [Fact]
    public void SubmitReport_EnforcesWindowAndDuplicateRules()
    {
        var newsSystem = new NewsSystem();
        var research = new ResearchSystem(newsSystem, baseReward: 100, researchWindow: 2, settlementDelay: 3);
        var news = newsSystem.InjectFakeNews(10, "source", NewsSentiment.Bullish);

        Assert.NotNull(research.SubmitReport("alpha", news.NewsId, Prediction.Long, currentTick: 11));
        Assert.Null(research.SubmitReport("alpha", news.NewsId, Prediction.Long, currentTick: 12));
        Assert.NotNull(research.SubmitReport("beta", news.NewsId, Prediction.Short, currentTick: 12));
        Assert.Null(research.SubmitReport("gamma", news.NewsId, Prediction.Long, currentTick: 13));
        Assert.Equal(2, research.PendingReports.Count);
    }

    [Fact]
    public void SettleReports_UsesRanksAndFakeNewsRules()
    {
        var newsSystem = new NewsSystem();
        var research = new ResearchSystem(newsSystem, baseReward: 100, researchWindow: 3, settlementDelay: 3);
        var news = newsSystem.InjectFakeNews(10, "alpha", NewsSentiment.Bullish);

        var first = research.SubmitReport("alpha", news.NewsId, Prediction.Long, currentTick: 11)!;
        var second = research.SubmitReport("beta", news.NewsId, Prediction.Short, currentTick: 12)!;

        var settled = research.SettleReports(13, tick => tick == 10 ? 100 : 120);

        Assert.Collection(settled,
            report =>
            {
                Assert.Same(first, report);
                Assert.Equal(1, report.SubmissionRank);
                Assert.True(report.IsCorrect);
                Assert.True(report.Reward > 0);
            },
            report =>
            {
                Assert.Same(second, report);
                Assert.Equal(2, report.SubmissionRank);
                Assert.False(report.IsCorrect);
                Assert.True(report.Reward < 0);
            });

        Assert.Empty(research.PendingReports);
        Assert.Equal(2, research.SettledReports.Count);
    }

    [Fact]
    public void Reset_ClearsReports()
    {
        var newsSystem = new NewsSystem();
        var research = new ResearchSystem(newsSystem);
        var news = newsSystem.InjectFakeNews(0, "source", NewsSentiment.Bearish);
        Assert.NotNull(research.SubmitReport("alpha", news.NewsId, Prediction.Short, currentTick: 0));

        research.Reset();

        Assert.Empty(research.PendingReports);
        Assert.Empty(research.SettledReports);
    }
}

public class NpcTraderCoverageTests
{
    [Fact]
    public void GenerateOrders_SkipsWhenPriceReferenceIsMissing()
    {
        var orderBook = new OrderBook(0);
        var engine = new MatchEngine(orderBook, new Dictionary<string, Player>());
        var trader = new NPCTrader(3);

        trader.GenerateOrders(engine, orderBook, sentiment: null, currentTick: 0);

        Assert.Empty(engine.PendingOrders);
    }

    [Fact]
    public void GenerateOrders_AddsSystemOrdersWhenMidPriceExists()
    {
        var orderBook = new OrderBook(1000);
        var engine = new MatchEngine(orderBook, new Dictionary<string, Player>());
        var trader = new NPCTrader(3);

        trader.GenerateOrders(engine, orderBook, NewsSentiment.Bullish, currentTick: 0);

        Assert.NotEmpty(engine.PendingOrders);
        Assert.All(engine.PendingOrders, order => Assert.Equal("SYSTEM", order.PlayerToken));
    }
}

public class TradingDayCoverageTests
{
    [Fact]
    public void Initialize_SeedsLiquidityAndResetsCounters()
    {
        var (day, players) = CreateTradingDay(initialize: false);
        players["alpha"].OrdersSentThisTick = 2;
        players["alpha"].ReportsSentThisTick = 1;

        day.Initialize();

        Assert.Equal(5, day.OrderBook.Bids.Count);
        Assert.Equal(5, day.OrderBook.Asks.Count);
        Assert.Equal(999, day.OrderBook.BestBid);
        Assert.Equal(1001, day.OrderBook.BestAsk);
        Assert.Equal(1000, day.GetMidPriceAtTick(0));
        Assert.Equal(0, players["alpha"].OrdersSentThisTick);
        Assert.False(day.HasPendingNotifications);
    }

    [Fact]
    public void HandleLimitBuy_RejectsWhenArrivalExceedsMaxTicks()
    {
        var (day, players) = CreateTradingDay(maxTicks: 1);
        players["alpha"].NetworkDelay = 2;

        Assert.False(day.HandleLimitBuy("alpha", 100, 1));
        Assert.Empty(day.MatchEngine.PendingOrders);
        Assert.Equal(1_000_000, players["alpha"].Mora);
    }

    [Fact]
    public void HandleSubmitReport_SettlesReportsAndPublishesNotifications()
    {
        var (day, players) = CreateTradingDay(maxTicks: 5);
        day.OrderBook.Clear();
        day.OrderBook.UpdateLastPrice(1000);

        var news = day.NewsSystem.InjectFakeNews(0, "alpha", NewsSentiment.Bullish);
        Assert.True(day.HandleSubmitReport("alpha", news.NewsId, Prediction.Long));
        Assert.True(day.HandleSubmitReport("beta", news.NewsId, Prediction.Short));

        day.Tick();
        day.Tick();
        day.OrderBook.UpdateLastPrice(2000);
        day.Tick();

        Assert.Equal(2, day.SettledReportsThisDay.Count);
        Assert.True(players["alpha"].Mora > players["beta"].Mora);
        Assert.True(day.HasPendingNotifications);

        day.MarkNotificationsPublished();
        Assert.False(day.HasPendingNotifications);
        Assert.Empty(day.SettledReportsThisDay);
        Assert.Empty(day.PublishedNewsThisDay);
    }

    [Fact]
    public void HandleActivateSkill_ExercisesTradingSkills()
    {
        var (day, players) = CreateTradingDay(maxTicks: 5);
        day.OrderBook.Clear();
        day.OrderBook.UpdateLastPrice(1000);

        players["alpha"].ActiveCards.Add(new StopLossBlade());
        players["alpha"].ActiveCards.Add(new TargetedPurchase());
        players["alpha"].ActiveCards.Add(new NetworkStorm());
        players["alpha"].ActiveCards.Add(new PublicOpinionAttack());
        players["alpha"].ActiveCards.Add(new FlashTrading());

        Assert.True(day.HandleLimitBuy("alpha", 100, 1));
        Assert.True(day.HandleActivateSkill("alpha", "止损名刀"));
        Assert.Equal(990_000, players["alpha"].Mora);
        Assert.Empty(day.GetPlayerPendingOrders("alpha"));
        Assert.True(players["alpha"].IsImmune);

        Assert.True(day.HandleActivateSkill("alpha", "定向增发"));
        Assert.Equal(892_000, players["alpha"].Mora);
        Assert.Equal(100, players["alpha"].LockedGold);

        Assert.True(day.HandleActivateSkill("alpha", "网络风暴", targetPlayerId: 1));
        Assert.True(day.HandleLimitBuy("beta", 100, 1));
        Assert.Equal(2, day.MatchEngine.GetPendingOrders("beta").Single().ArrivalTick);

        Assert.True(day.HandleActivateSkill("alpha", "舆情打击"));
        Assert.NotNull(day.NewsSystem.LatestNews);
        Assert.True(day.NewsSystem.LatestNews!.IsFake);
        Assert.True(players["beta"].PendingFakeBroadcast);
        Assert.True(players["beta"].PendingCheapInsiderCorruption);

        Assert.True(day.HandleActivateSkill("alpha", "闪电交易"));
        day.Tick();
        Assert.Equal(1, players["alpha"].BonusImmediateOrdersToday);

        day.Tick();
        day.Tick();
        day.Tick();
        Assert.False(players["alpha"].IsImmune);
    }

    private static (TradingDay Day, Dictionary<string, Player> Players) CreateTradingDay(int maxTicks = 5, bool initialize = true)
    {
        var players = new Dictionary<string, Player>
        {
            ["alpha"] = new Player("alpha", 0),
            ["beta"] = new Player("beta", 1)
        };

        var day = new TradingDay(players, maxTicks, 1000, 1, 1, 2, 3, 100, 0);
        if (initialize)
        {
            day.Initialize();
        }
        return (day, players);
    }
}

public class GameCoverageTests
{
    [Fact]
    public void Initialize_RegistersExistingPlayersInScoreboard()
    {
        var game = new Game(FastSettings());
        Assert.True(game.AddPlayer("alpha"));
        Assert.True(game.AddPlayer("beta"));

        game.Initialize();

        Assert.Equal(0, game.Scoreboard["alpha"]);
        Assert.Equal(0, game.CumulativeNavs["beta"]);
    }

    [Fact]
    public void Tick_FullFlowEndsInSettlementAndUsesTradeCountTiebreaker()
    {
        var game = new Game(FastSettings());
        game.AddPlayer("alpha");
        game.AddPlayer("beta");
        game.Initialize();

        game.Tick();
        game.Tick();

        var options = game.CardManager.GetCurrentDraftOptionNames();
        Assert.NotEmpty(options);
        Assert.True(game.SelectStrategy("alpha", options[0]));
        Assert.True(game.SelectStrategy("beta", options[0]));

        game.Tick();
        Assert.Equal(GameStage.TradingDay, game.Stage);

        game.FindPlayer("alpha")!.MonthlyTradeCount = 3;
        game.Tick();
        Assert.Equal(GameStage.Settlement, game.Stage);

        game.Tick();
        Assert.True(game.HasPendingSettlementNotification);
        Assert.NotNull(game.LatestSettlement);
        Assert.Equal("alpha", game.LatestSettlement!.WinnerToken);
        Assert.Equal(1, game.Scoreboard["alpha"]);

        game.MarkSettlementNotificationPublished();
        Assert.False(game.HasPendingSettlementNotification);

        game.Tick();
        Assert.Equal(GameStage.Finished, game.Stage);
    }

    private static GameSettings FastSettings() => new()
    {
        MinimumPlayerCount = 2,
        PlayerWaitingTicks = 1,
        StrategySelectionTicks = 1,
        TradingDayTicks = 1,
        TradingDayCount = 1,
        InitialGoldPrice = 1000,
        NewsIntervalMin = 1,
        NewsIntervalMax = 1,
        ResearchWindowTicks = 2,
        ResearchSettlementDelay = 3,
        BaseResearchReward = 100,
        NpcOrdersPerTick = 0
    };
}

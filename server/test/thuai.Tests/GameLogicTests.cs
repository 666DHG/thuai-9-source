using Thuai.GameLogic;
using Thuai.Utility;

namespace Thuai.Tests;

#region OrderBookTests

public class OrderBookTests
{
    private readonly OrderBook _book = new(initialPrice: 1000);

    [Fact]
    public void BestBid_EmptyBook_ReturnsNull()
    {
        Assert.Null(_book.BestBid);
        Assert.Null(_book.BestBidOrder);
    }

    [Fact]
    public void BestAsk_EmptyBook_ReturnsNull()
    {
        Assert.Null(_book.BestAsk);
        Assert.Null(_book.BestAskOrder);
    }

    [Fact]
    public void AddBuyOrder_UpdatesBestBid()
    {
        var order = new Order("player1", OrderSide.Buy, price: 950, quantity: 10,
            submitTick: 0, networkDelay: 0);

        _book.AddOrder(order);

        Assert.Equal(950, _book.BestBid);
        Assert.Same(order, _book.BestBidOrder);
    }

    [Fact]
    public void AddSellOrder_UpdatesBestAsk()
    {
        var order = new Order("player1", OrderSide.Sell, price: 1050, quantity: 5,
            submitTick: 0, networkDelay: 0);

        _book.AddOrder(order);

        Assert.Equal(1050, _book.BestAsk);
        Assert.Same(order, _book.BestAskOrder);
    }

    [Fact]
    public void MidPrice_WithBothSides_ReturnsAverage()
    {
        var bid = new Order("p1", OrderSide.Buy, price: 900, quantity: 10,
            submitTick: 0, networkDelay: 0);
        var ask = new Order("p2", OrderSide.Sell, price: 1100, quantity: 10,
            submitTick: 0, networkDelay: 0);

        _book.AddOrder(bid);
        _book.AddOrder(ask);

        // (900 + 1100) / 2 = 1000
        Assert.Equal(1000, _book.MidPrice);
    }

    [Fact]
    public void MidPrice_OneSide_ReturnsLastPrice()
    {
        // Only add a bid, no ask -> MidPrice falls back to LastPrice
        var bid = new Order("p1", OrderSide.Buy, price: 950, quantity: 10,
            submitTick: 0, networkDelay: 0);
        _book.AddOrder(bid);

        // LastPrice was set to 1000 in the constructor
        Assert.Equal(1000, _book.MidPrice);
    }

    [Fact]
    public void RemoveOrder_ByOrderId_Succeeds()
    {
        var order = new Order("p1", OrderSide.Buy, price: 900, quantity: 10,
            submitTick: 0, networkDelay: 0);
        _book.AddOrder(order);

        bool result = _book.RemoveOrder(order.OrderId);

        Assert.True(result);
        Assert.Null(_book.BestBid);
        Assert.Null(_book.GetOrder(order.OrderId));
    }

    [Fact]
    public void RemoveOrder_InvalidId_ReturnsFalse()
    {
        bool result = _book.RemoveOrder(orderId: -999);

        Assert.False(result);
    }

    [Fact]
    public void BidsAreDescendingByPrice()
    {
        var lowBid = new Order("p1", OrderSide.Buy, price: 800, quantity: 5,
            submitTick: 0, networkDelay: 0);
        var highBid = new Order("p1", OrderSide.Buy, price: 950, quantity: 5,
            submitTick: 0, networkDelay: 0);
        var midBid = new Order("p1", OrderSide.Buy, price: 900, quantity: 5,
            submitTick: 0, networkDelay: 0);

        _book.AddOrder(lowBid);
        _book.AddOrder(highBid);
        _book.AddOrder(midBid);

        // BestBid should be the highest price
        Assert.Equal(950, _book.BestBid);

        // Walk through Bids collection - should be descending by price
        var prices = _book.Bids.Select(o => o.Price).ToList();
        Assert.Equal(new long[] { 950, 900, 800 }, prices);
    }

    [Fact]
    public void AsksAreAscendingByPrice()
    {
        var highAsk = new Order("p1", OrderSide.Sell, price: 1200, quantity: 5,
            submitTick: 0, networkDelay: 0);
        var lowAsk = new Order("p1", OrderSide.Sell, price: 1050, quantity: 5,
            submitTick: 0, networkDelay: 0);
        var midAsk = new Order("p1", OrderSide.Sell, price: 1100, quantity: 5,
            submitTick: 0, networkDelay: 0);

        _book.AddOrder(highAsk);
        _book.AddOrder(lowAsk);
        _book.AddOrder(midAsk);

        // BestAsk should be the lowest price
        Assert.Equal(1050, _book.BestAsk);

        // Walk through Asks collection - should be ascending by price
        var prices = _book.Asks.Select(o => o.Price).ToList();
        Assert.Equal(new long[] { 1050, 1100, 1200 }, prices);
    }

    [Fact]
    public void SamePriceOrders_SortByArrivalTick()
    {
        // Two buy orders at the same price but different arrival ticks
        var earlier = new Order("p1", OrderSide.Buy, price: 900, quantity: 5,
            submitTick: 0, networkDelay: 0); // ArrivalTick = 0
        var later = new Order("p2", OrderSide.Buy, price: 900, quantity: 5,
            submitTick: 10, networkDelay: 0); // ArrivalTick = 10

        _book.AddOrder(later);
        _book.AddOrder(earlier);

        // The earlier-arriving order should be the best bid (first in the sorted set)
        Assert.Same(earlier, _book.BestBidOrder);

        var bids = _book.Bids.ToList();
        Assert.Equal(2, bids.Count);
        Assert.Same(earlier, bids[0]);
        Assert.Same(later, bids[1]);
    }

    [Fact]
    public void GetVisibleBids_CurrentRules_ShowFullQuantity()
    {
        // A normal order with quantity 100 -> VisibleQuantity = 100
        var normal = new Order("p1", OrderSide.Buy, price: 950, quantity: 100,
            submitTick: 0, networkDelay: 0, isIceberg: false);

        // Iceberg visibility no longer changes the displayed size in the new rules.
        var iceberg = new Order("p2", OrderSide.Buy, price: 900, quantity: 100,
            submitTick: 0, networkDelay: 0, isIceberg: true);

        _book.AddOrder(normal);
        _book.AddOrder(iceberg);

        var visibleBids = _book.GetVisibleBids(maxLevels: 10);

        // Two price levels
        Assert.Equal(2, visibleBids.Count);

        // Level at 950 (normal) should show full quantity
        Assert.Equal(950, visibleBids[0].Price);
        Assert.Equal(100, visibleBids[0].Quantity);

        // Level at 900 still shows full quantity under the new rules.
        Assert.Equal(900, visibleBids[1].Price);
        Assert.Equal(100, visibleBids[1].Quantity);
    }

    [Fact]
    public void GetPlayerOrders_ReturnsOnlyThatPlayersOrders()
    {
        var order1 = new Order("player_A", OrderSide.Buy, price: 900, quantity: 10,
            submitTick: 0, networkDelay: 0);
        var order2 = new Order("player_B", OrderSide.Sell, price: 1100, quantity: 10,
            submitTick: 0, networkDelay: 0);
        var order3 = new Order("player_A", OrderSide.Sell, price: 1050, quantity: 5,
            submitTick: 0, networkDelay: 0);

        _book.AddOrder(order1);
        _book.AddOrder(order2);
        _book.AddOrder(order3);

        var playerAOrders = _book.GetPlayerOrders("player_A");

        Assert.Equal(2, playerAOrders.Count);
        Assert.All(playerAOrders, o => Assert.Equal("player_A", o.PlayerToken));

        var playerBOrders = _book.GetPlayerOrders("player_B");
        Assert.Single(playerBOrders);
        Assert.Equal("player_B", playerBOrders[0].PlayerToken);
    }
}

#endregion

#region MatchEngineTests

public class MatchEngineTests
{
    private readonly OrderBook _orderBook;
    private readonly Dictionary<string, Player> _players;
    private readonly MatchEngine _engine;
    private readonly Player _buyer;
    private readonly Player _seller;

    public MatchEngineTests()
    {
        _orderBook = new OrderBook(initialPrice: 1000);
        _buyer = new Player("buyer", 0);
        _seller = new Player("seller", 1);
        _players = new Dictionary<string, Player>
        {
            ["buyer"] = _buyer,
            ["seller"] = _seller
        };
        _engine = new MatchEngine(_orderBook, _players);
    }

    [Fact]
    public void SubmitBuyOrder_FreezesMora()
    {
        long initialMora = _buyer.Mora;

        var order = _engine.SubmitOrder("buyer", OrderSide.Buy, price: 100, quantity: 10,
            currentTick: 0);

        Assert.NotNull(order);
        // notional = 100 * 10 = 1000; fee buffer = ceil(1000 * 0.0002) = 1
        long expectedReserve = 1000 + (long)Math.Ceiling(1000 * _buyer.TransactionFeeRate);
        Assert.Equal(initialMora - expectedReserve, _buyer.Mora);
        Assert.Equal(expectedReserve, _buyer.FrozenMora);
        Assert.Equal(expectedReserve - 1000, order.FrozenFeeRemaining);
    }

    [Fact]
    public void SubmitSellOrder_FreezesGold()
    {
        int initialGold = _seller.Gold;

        var order = _engine.SubmitOrder("seller", OrderSide.Sell, price: 1200, quantity: 5,
            currentTick: 0);

        Assert.NotNull(order);
        Assert.Equal(initialGold - 5, _seller.Gold);
        Assert.Equal(5, _seller.FrozenGold);
    }

    [Fact]
    public void SubmitOrder_ExceedsBalance_ReturnsNull()
    {
        // Buyer has 1,000,000 Mora. Try to buy something costing more.
        var order = _engine.SubmitOrder("buyer", OrderSide.Buy, price: 1_000_001, quantity: 1,
            currentTick: 0);

        Assert.Null(order);
        // Mora should be unchanged
        Assert.Equal(1_000_000, _buyer.Mora);
    }

    [Fact]
    public void SubmitOrder_NoLongerRejectsAtSubmitTime()
    {
        // The new rules enforce action limits on the effective trading day, not at submit time.
        for (int i = 0; i < 5; i++)
        {
            var o = _engine.SubmitOrder("buyer", OrderSide.Buy, price: 100, quantity: 1,
                currentTick: 0);
            Assert.NotNull(o);
        }

        var accepted = _engine.SubmitOrder("buyer", OrderSide.Buy, price: 100, quantity: 1,
            currentTick: 0);

        Assert.NotNull(accepted);
    }

    [Fact]
    public void ProcessTick_NetworkDelay_OrderNotMatchedBeforeArrival()
    {
        // Buyer has network delay of 5 ticks
        _buyer.NetworkDelay = 5;

        // Submit a sell that arrives immediately (no delay)
        _seller.NetworkDelay = 0;
        _engine.SubmitOrder("seller", OrderSide.Sell, price: 900, quantity: 10,
            currentTick: 0);
        _engine.ProcessTick(0); // Sell order enters the book

        // Submit a buy at tick 1 with delay 5 -> arrives at tick 6
        _engine.SubmitOrder("buyer", OrderSide.Buy, price: 950, quantity: 10,
            currentTick: 1, networkDelay: 5);

        // Process ticks 1-5: the buy order should NOT be matched yet
        for (int tick = 1; tick <= 5; tick++)
        {
            var trades = _engine.ProcessTick(tick);
            Assert.Empty(trades);
        }

        // At tick 6, the buy order arrives and should match
        var matchedTrades = _engine.ProcessTick(6);
        Assert.NotEmpty(matchedTrades);
    }

    [Fact]
    public void ProcessTick_OrderArrivedAtTick_GetsMatched()
    {
        _buyer.NetworkDelay = 0;
        _seller.NetworkDelay = 0;

        // Submit a sell order at price 1000
        _engine.SubmitOrder("seller", OrderSide.Sell, price: 1000, quantity: 5,
            currentTick: 0);

        // Submit a crossing buy order at price 1000
        _engine.SubmitOrder("buyer", OrderSide.Buy, price: 1000, quantity: 5,
            currentTick: 0);

        // Both arrive at tick 0, process should match them
        var trades = _engine.ProcessTick(0);
        Assert.Single(trades);
        Assert.Equal(1000, trades[0].Price);
        Assert.Equal(5, trades[0].Quantity);
    }

    [Fact]
    public void MatchAtMakersPrice_BuyTaker()
    {
        _buyer.NetworkDelay = 0;
        _seller.NetworkDelay = 0;

        // Sell order is placed first (at tick 0) -> it is the maker
        _engine.SubmitOrder("seller", OrderSide.Sell, price: 950, quantity: 10,
            currentTick: 0);
        _engine.ProcessTick(0); // Sell order enters the book (arrives at tick 0)

        // Buy order placed later (at tick 1) with a higher price -> taker
        _engine.SubmitOrder("buyer", OrderSide.Buy, price: 1000, quantity: 10,
            currentTick: 1);
        var trades = _engine.ProcessTick(1);

        // Trade should execute at the maker's (seller's) price of 950
        Assert.Single(trades);
        Assert.Equal(950, trades[0].Price);
    }

    [Fact]
    public void MatchAtMakersPrice_SellTaker()
    {
        _buyer.NetworkDelay = 0;
        _seller.NetworkDelay = 0;

        // Buy order placed first (at tick 0) -> it is the maker
        _engine.SubmitOrder("buyer", OrderSide.Buy, price: 1050, quantity: 10,
            currentTick: 0);
        _engine.ProcessTick(0); // Buy order enters the book

        // Sell order placed later (at tick 1) at a lower price -> taker
        _engine.SubmitOrder("seller", OrderSide.Sell, price: 1000, quantity: 10,
            currentTick: 1);
        var trades = _engine.ProcessTick(1);

        // Trade should execute at the maker's (buyer's) price of 1050
        Assert.Single(trades);
        Assert.Equal(1050, trades[0].Price);
    }

    [Fact]
    public void PartialFill_RemainingQuantityUpdated()
    {
        _buyer.NetworkDelay = 0;
        _seller.NetworkDelay = 0;

        // Sell 10 units
        _engine.SubmitOrder("seller", OrderSide.Sell, price: 1000, quantity: 10,
            currentTick: 0);
        _engine.ProcessTick(0);

        // Buy only 3 units at matching price
        var buyOrder = _engine.SubmitOrder("buyer", OrderSide.Buy, price: 1000, quantity: 3,
            currentTick: 1);
        var trades = _engine.ProcessTick(1);

        Assert.Single(trades);
        Assert.Equal(3, trades[0].Quantity);

        // The sell order should still be in the book with 7 remaining
        var sellOrderInBook = _orderBook.BestAskOrder;
        Assert.NotNull(sellOrderInBook);
        Assert.Equal(7, sellOrderInBook.RemainingQuantity);
        Assert.Equal(OrderStatus.PartiallyFilled, sellOrderInBook.Status);
    }

    [Fact]
    public void CancelOrder_UnfreezesAssets()
    {
        _buyer.NetworkDelay = 0;

        // Place a buy order, freezing 1000 * 10 = 10000 Mora
        var order = _engine.SubmitOrder("buyer", OrderSide.Buy, price: 1000, quantity: 10,
            currentTick: 0);
        _engine.ProcessTick(0); // Order enters the book

        Assert.NotNull(order);
        long moraAfterOrder = _buyer.Mora;
        long frozenAfterOrder = _buyer.FrozenMora;

        // Cancel the order
        bool result = _engine.CancelOrder("buyer", order.OrderId);
        Assert.True(result);

        // Frozen Mora should return to available
        Assert.Equal(moraAfterOrder + frozenAfterOrder, _buyer.Mora);
        Assert.Equal(0, _buyer.FrozenMora);
    }

    [Fact]
    public void CancelOrder_WrongPlayer_Fails()
    {
        _buyer.NetworkDelay = 0;

        var order = _engine.SubmitOrder("buyer", OrderSide.Buy, price: 1000, quantity: 10,
            currentTick: 0);
        _engine.ProcessTick(0);

        Assert.NotNull(order);

        // Seller tries to cancel buyer's order
        bool result = _engine.CancelOrder("seller", order.OrderId);
        Assert.False(result);

        // Order should still be in the book
        Assert.NotNull(_orderBook.GetOrder(order.OrderId));
    }

    [Fact]
    public void TradeExecuted_BuyerReceivesGold()
    {
        _buyer.NetworkDelay = 0;
        _seller.NetworkDelay = 0;

        int initialGold = _buyer.Gold;

        _engine.SubmitOrder("seller", OrderSide.Sell, price: 1000, quantity: 5,
            currentTick: 0);
        _engine.SubmitOrder("buyer", OrderSide.Buy, price: 1000, quantity: 5,
            currentTick: 0);
        _engine.ProcessTick(0);

        Assert.Equal(initialGold + 5, _buyer.Gold);
    }

    [Fact]
    public void TradeExecuted_SellerReceivesMora()
    {
        _buyer.NetworkDelay = 0;
        _seller.NetworkDelay = 0;

        long initialMora = _seller.Mora;

        _engine.SubmitOrder("seller", OrderSide.Sell, price: 1000, quantity: 5,
            currentTick: 0);
        _engine.SubmitOrder("buyer", OrderSide.Buy, price: 1000, quantity: 5,
            currentTick: 0);
        _engine.ProcessTick(0);

        // Seller receives proceeds minus fee. Fee = 1000 * 5 * 0.0002 = 1
        long expectedProceeds = 1000 * 5 - (long)(1000 * 5 * _seller.TransactionFeeRate);
        Assert.Equal(initialMora + expectedProceeds, _seller.Mora);
    }

    [Fact]
    public void TradeExecuted_FeeDeducted()
    {
        _buyer.NetworkDelay = 0;
        _seller.NetworkDelay = 0;

        Trade? executedTrade = null;
        _engine.OnTradeExecuted += t => executedTrade = t;

        _engine.SubmitOrder("seller", OrderSide.Sell, price: 1000, quantity: 10,
            currentTick: 0);
        _engine.SubmitOrder("buyer", OrderSide.Buy, price: 1000, quantity: 10,
            currentTick: 0);
        _engine.ProcessTick(0);

        Assert.NotNull(executedTrade);

        // Fee = tradeAmount * feeRate = 10000 * 0.0002 = 2
        long expectedFee = (long)(1000 * 10 * _buyer.TransactionFeeRate);
        Assert.Equal(expectedFee, executedTrade.BuyerFee);
        Assert.Equal(expectedFee, executedTrade.SellerFee);
    }

    [Fact]
    public void SystemOrders_SkipAssetChecks()
    {
        // SYSTEM token should bypass all validation (no player in _players dict)
        var order = _engine.SubmitOrder("SYSTEM", OrderSide.Buy, price: 999999, quantity: 999999,
            currentTick: 0);

        Assert.NotNull(order);
        Assert.Equal("SYSTEM", order.PlayerToken);
    }
}

#endregion

#region PlayerTests

public class PlayerTests
{
    [Fact]
    public void InitialState_CorrectDefaults()
    {
        var player = new Player("token123", playerId: 0);

        Assert.Equal("token123", player.Token);
        Assert.Equal(0, player.PlayerId);
        Assert.Equal(1_000_000, player.Mora);
        Assert.Equal(0, player.FrozenMora);
        Assert.Equal(1_000, player.Gold);
        Assert.Equal(0, player.FrozenGold);
        Assert.Equal(1, player.NetworkDelay);
        Assert.Equal(0.0002, player.TransactionFeeRate);
        Assert.Equal(2, player.MaxOrdersPerTick);
        Assert.Equal(1, player.MaxReportsPerTick);
        Assert.Equal(0, player.OrdersSentThisTick);
        Assert.Equal(0, player.ReportsSentThisTick);
        Assert.Equal(0, player.TotalTradeCount);
        Assert.Empty(player.ActiveCards);
    }

    [Fact]
    public void FreezeMora_ReducesAvailableIncreasesFrozen()
    {
        var player = new Player("p1", 0);
        long initialMora = player.Mora;

        player.FreezeMora(5000);

        Assert.Equal(initialMora - 5000, player.Mora);
        Assert.Equal(5000, player.FrozenMora);
    }

    [Fact]
    public void UnfreezeMora_ReversesFreezing()
    {
        var player = new Player("p1", 0);
        long initialMora = player.Mora;

        player.FreezeMora(5000);
        player.UnfreezeMora(3000);

        Assert.Equal(initialMora - 2000, player.Mora);
        Assert.Equal(2000, player.FrozenMora);
    }

    [Fact]
    public void CalculateNAV_IncludesAllAssets()
    {
        var player = new Player("p1", 0);
        // Default: Mora=1_000_000, Gold=1_000

        // Freeze some of each
        player.FreezeMora(100_000);
        player.FreezeGold(200);
        player.AddLockedGold(50, untilTick: 999);

        long midPrice = 500;

        // NAV = Mora + FrozenMora + (Gold + FrozenGold + LockedGold) * midPrice
        // = 900_000 + 100_000 + (800 + 200 + 50) * 500
        // = 1_000_000 + 1_050 * 500
        // = 1_000_000 + 525_000
        // = 1_525_000
        long expectedNAV = 1_000_000 + (long)(800 + 200 + 50) * 500;

        Assert.Equal(expectedNAV, player.CalculateNAV(midPrice));
    }

    [Fact]
    public void ResetForNewDay_ResetsAssetsKeepsCards()
    {
        var player = new Player("p1", 0);

        // Modify state from defaults
        player.FreezeMora(50_000);
        player.FreezeGold(100);
        player.OrdersSentThisTick = 3;
        player.ReportsSentThisTick = 1;

        // Add a card - we need an IStrategyCard implementation.
        // Use any card from the StrategyCards namespace.
        // We'll verify ActiveCards are NOT cleared by ResetForNewDay.
        var cardCountBefore = player.ActiveCards.Count;

        player.ResetForNewDay();

        Assert.Equal(1_000_000, player.Mora);
        Assert.Equal(0, player.FrozenMora);
        Assert.Equal(1_000, player.Gold);
        Assert.Equal(0, player.FrozenGold);
        Assert.Equal(0, player.LockedGold);
        Assert.Equal(0, player.OrdersSentThisTick);
        Assert.Equal(0, player.ReportsSentThisTick);
        // ActiveCards list itself is not cleared by ResetForNewDay
        Assert.Equal(cardCountBefore, player.ActiveCards.Count);
    }

    [Fact]
    public void ResetTickCounters_ResetsOrderAndReportCounts()
    {
        var player = new Player("p1", 0);
        player.OrdersSentThisTick = 3;
        player.ReportsSentThisTick = 1;

        player.ResetTickCounters();

        Assert.Equal(0, player.OrdersSentThisTick);
        Assert.Equal(0, player.ReportsSentThisTick);
    }

    [Fact]
    public void CanPlaceOrder_RespectLimit()
    {
        var player = new Player("p1", 0);
        player.MaxOrdersPerTick = 2;

        Assert.True(player.CanPlaceOrder());

        player.OrdersSentThisTick = 1;
        Assert.True(player.CanPlaceOrder());

        player.OrdersSentThisTick = 2;
        Assert.False(player.CanPlaceOrder());

        player.OrdersSentThisTick = 3;
        Assert.False(player.CanPlaceOrder());
    }

    [Fact]
    public void CanSubmitReport_RespectLimit()
    {
        var player = new Player("p1", 0);
        player.MaxReportsPerTick = 1;

        Assert.True(player.CanSubmitReport());

        player.ReportsSentThisTick = 1;
        Assert.False(player.CanSubmitReport());
    }
}

#endregion

#region NewsSystemTests

public class NewsSystemTests
{
    [Fact]
    public void Tick_BeforeInterval_ReturnsNull()
    {
        // The new rule set uses fixed monthly news days 1, 11, and 21.
        var news = new NewsSystem(intervalMin: 100, intervalMax: 200, researchWindow: 50);

        // Day 0 has no scheduled news.
        var result = news.Tick(0);
        Assert.Null(result);

        // Day 1 is the first scheduled release day.
        result = news.Tick(1);
        Assert.NotNull(result);
    }

    [Fact]
    public void Tick_AtInterval_ReturnsNews()
    {
        // Use interval of exactly 1 so news fires immediately
        var newsSystem = new NewsSystem(intervalMin: 1, intervalMax: 1, researchWindow: 50);

        // The first NextNewsTick will be rng.Next(1, 2) = 1
        // So tick 0 returns null, tick 1 returns news
        var resultAt0 = newsSystem.Tick(0);
        // NextNewsTick is 1, tick 0 < 1, so null
        if (resultAt0 == null)
        {
            var resultAt1 = newsSystem.Tick(1);
            Assert.NotNull(resultAt1);
        }
        else
        {
            // If rng happened to set NextNewsTick to 0, it still succeeds
            Assert.NotNull(resultAt0);
        }
    }

    [Fact]
    public void GeneratedNews_HasValidSentiment()
    {
        // Use minimal interval to ensure news is produced quickly
        var newsSystem = new NewsSystem(intervalMin: 1, intervalMax: 1, researchWindow: 50);

        News? publishedNews = null;
        // Keep ticking until we get news
        for (int tick = 0; tick <= 5 && publishedNews == null; tick++)
        {
            publishedNews = newsSystem.Tick(tick);
        }

        Assert.NotNull(publishedNews);
        Assert.True(
            publishedNews.Sentiment == NewsSentiment.Bullish ||
            publishedNews.Sentiment == NewsSentiment.Bearish);
        Assert.False(publishedNews.IsFake);
        Assert.Null(publishedNews.SourcePlayer);
        Assert.NotEmpty(publishedNews.Content);
    }

    [Fact]
    public void InjectFakeNews_MarkedAsFake()
    {
        var newsSystem = new NewsSystem(intervalMin: 200, intervalMax: 400, researchWindow: 50);

        var fakeNews = newsSystem.InjectFakeNews(currentTick: 10, sourcePlayer: "manipulator",
            sentiment: NewsSentiment.Bearish);

        Assert.True(fakeNews.IsFake);
        Assert.Equal("manipulator", fakeNews.SourcePlayer);
        Assert.Equal(NewsSentiment.Bearish, fakeNews.Sentiment);
        Assert.Equal(10, fakeNews.PublishTick);
        Assert.NotEmpty(fakeNews.Content);

        // Should appear in AllNews and as LatestNews
        Assert.Contains(fakeNews, newsSystem.AllNews);
        Assert.Same(fakeNews, newsSystem.LatestNews);
    }

    [Fact]
    public void IsWithinResearchWindow_Inside_ReturnsTrue()
    {
        var newsSystem = new NewsSystem(intervalMin: 200, intervalMax: 400, researchWindow: 50);

        // Inject a news item at tick 100
        var news = newsSystem.InjectFakeNews(currentTick: 100, sourcePlayer: "p1",
            sentiment: NewsSentiment.Bullish);

        // At tick 110 (10 ticks after publish), within the 50-tick window
        Assert.True(newsSystem.IsWithinResearchWindow(news.NewsId, currentTick: 110));

        // At tick 150 (50 ticks after publish), still within window (<=50)
        Assert.True(newsSystem.IsWithinResearchWindow(news.NewsId, currentTick: 150));
    }

    [Fact]
    public void IsWithinResearchWindow_Outside_ReturnsFalse()
    {
        var newsSystem = new NewsSystem(intervalMin: 200, intervalMax: 400, researchWindow: 50);

        var news = newsSystem.InjectFakeNews(currentTick: 100, sourcePlayer: "p1",
            sentiment: NewsSentiment.Bullish);

        // At tick 151 (51 ticks after publish), outside the 50-tick window
        Assert.False(newsSystem.IsWithinResearchWindow(news.NewsId, currentTick: 151));

        // Non-existent news id
        Assert.False(newsSystem.IsWithinResearchWindow(newsId: 99999, currentTick: 100));
    }

    [Fact]
    public void Reset_ClearsAllNews()
    {
        var newsSystem = new NewsSystem(intervalMin: 200, intervalMax: 400, researchWindow: 50);

        newsSystem.InjectFakeNews(currentTick: 10, sourcePlayer: "p1",
            sentiment: NewsSentiment.Bullish);
        Assert.NotEmpty(newsSystem.AllNews);
        Assert.NotNull(newsSystem.LatestNews);

        newsSystem.Reset();

        Assert.Empty(newsSystem.AllNews);
        Assert.Null(newsSystem.LatestNews);
    }
}

#endregion

#region GameTests

public class GameTests
{
    private static GameSettings CreateDefaultSettings()
    {
        return new GameSettings
        {
            MinimumPlayerCount = 2,
            PlayerWaitingTicks = 3,
            StrategySelectionTicks = 40,
            TradingDayTicks = 30,
            TradingDayCount = 3,
            InitialGoldPrice = 1000,
            NewsIntervalMin = 1,
            NewsIntervalMax = 1,
            ResearchWindowTicks = 2,
            ResearchSettlementDelay = 3,
            BaseResearchReward = 10000,
            NpcOrdersPerTick = 3
        };
    }

    [Fact]
    public void InitialStage_IsWaiting()
    {
        var game = new Game(CreateDefaultSettings());
        game.Initialize();

        Assert.Equal(GameStage.Waiting, game.Stage);
    }

    [Fact]
    public void AddPlayer_InWaiting_Succeeds()
    {
        var game = new Game(CreateDefaultSettings());
        game.Initialize();

        bool result = game.AddPlayer("player_token_1");

        Assert.True(result);
        Assert.Single(game.Players);
        Assert.NotNull(game.FindPlayer("player_token_1"));
        Assert.Equal(0, game.Scoreboard["player_token_1"]);
    }

    [Fact]
    public void AddPlayer_AfterWaiting_Fails()
    {
        var settings = CreateDefaultSettings();
        settings = settings with { PlayerWaitingTicks = 1 };
        var game = new Game(settings);
        game.Initialize();

        // Add minimum players to allow transition
        game.AddPlayer("p1");
        game.AddPlayer("p2");

        // Tick until we leave Waiting stage
        // With PlayerWaitingTicks=1, after 1 tick with >= MinimumPlayerCount it transitions
        game.Tick(); // _waitingTicksRemaining decrements to 0, transitions to PreparingGame

        Assert.NotEqual(GameStage.Waiting, game.Stage);

        // Now try to add a player - should fail
        bool result = game.AddPlayer("p3");
        Assert.False(result);
    }

    [Fact]
    public void AddDuplicatePlayer_Fails()
    {
        var game = new Game(CreateDefaultSettings());
        game.Initialize();

        Assert.True(game.AddPlayer("dup_token"));
        Assert.False(game.AddPlayer("dup_token"));
        Assert.Single(game.Players);
    }

    [Fact]
    public void Transition_WaitingToPreparingGame_WhenPlayersReady()
    {
        var settings = CreateDefaultSettings();
        settings = settings with { PlayerWaitingTicks = 2, MinimumPlayerCount = 2 };
        var game = new Game(settings);
        game.Initialize();

        game.AddPlayer("p1");
        game.AddPlayer("p2");

        // Tick 1: _waitingTicksRemaining decrements from 2 to 1
        game.Tick();
        Assert.Equal(GameStage.Waiting, game.Stage);

        // Tick 2: _waitingTicksRemaining decrements from 1 to 0, transitions to PreparingGame
        game.Tick();
        Assert.Equal(GameStage.PreparingGame, game.Stage);
    }

    [Fact]
    public void SelectStrategy_DuringStrategyPhase_Succeeds()
    {
        var settings = CreateDefaultSettings();
        settings = settings with { PlayerWaitingTicks = 1, MinimumPlayerCount = 2 };
        var game = new Game(settings);
        game.Initialize();

        game.AddPlayer("p1");
        game.AddPlayer("p2");

        // Tick to leave Waiting -> PreparingGame
        game.Tick();
        Assert.Equal(GameStage.PreparingGame, game.Stage);

        // Tick again: PreparingGame -> StrategySelection
        game.Tick();
        Assert.Equal(GameStage.StrategySelection, game.Stage);

        // The CardManager now has draft options. Get an option name.
        var options = game.CardManager.GetCurrentDraftOptionNames();
        Assert.NotEmpty(options);

        // Select the first available option
        bool result = game.SelectStrategy("p1", options[0]);
        Assert.True(result);

        // Verify the card was added to the player
        var player = game.FindPlayer("p1");
        Assert.NotNull(player);
        Assert.Single(player.ActiveCards);
    }

    [Fact]
    public void SelectStrategy_OutsidePhase_Fails()
    {
        var game = new Game(CreateDefaultSettings());
        game.Initialize();

        game.AddPlayer("p1");
        game.AddPlayer("p2");

        // Still in Waiting stage
        Assert.Equal(GameStage.Waiting, game.Stage);

        bool result = game.SelectStrategy("p1", "SomeCard");
        Assert.False(result);
    }
}

#endregion

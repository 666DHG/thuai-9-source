namespace Thuai.GameLogic;

using Thuai.GameLogic.StrategyCards;

public record SkillActivation(int SourcePlayerId, string SkillName, string Description, int? TargetPlayerId = null);

public class TradingDay
{
    private readonly object _lock = new();
    private readonly int _maxTicks;
    private readonly long _initialGoldPrice;
    private readonly Dictionary<string, Player> _players;
    private readonly OrderBook _orderBook;
    private readonly MatchEngine _matchEngine;
    private readonly NewsSystem _newsSystem;
    private readonly NPCTrader _npcTrader;
    private readonly ResearchSystem _researchSystem;
    private readonly Dictionary<int, long> _midPriceHistory = new();
    private readonly List<Trade> _tradesThisDay = new();
    private readonly List<ResearchReport> _settledReportsThisDay = new();
    private readonly List<News> _publishedNewsThisDay = new();
    private readonly List<SkillActivation> _skillEffectsThisDay = new();
    private readonly List<(string PlayerToken, News Preview)> _pendingInsiderPreviews = new();

    private int _currentTick;
    private bool _isFinished;
    private bool _hasPendingNotifications;

    public int MonthNumber { get; }
    public int CurrentTick => _currentTick;
    public bool IsFinished => _isFinished;
    public bool HasPendingNotifications => _hasPendingNotifications;
    public OrderBook OrderBook => _orderBook;
    public MatchEngine MatchEngine => _matchEngine;
    public NewsSystem NewsSystem => _newsSystem;
    public ResearchSystem ResearchSystem => _researchSystem;
    public IReadOnlyList<Trade> TradesThisDay => _tradesThisDay;
    public IReadOnlyList<ResearchReport> SettledReportsThisDay => _settledReportsThisDay;
    public IReadOnlyList<News> PublishedNewsThisDay => _publishedNewsThisDay;
    public IReadOnlyList<SkillActivation> SkillEffectsThisDay => _skillEffectsThisDay;
    public IReadOnlyList<(string PlayerToken, News Preview)> PendingInsiderPreviews => _pendingInsiderPreviews;

    public TradingDay(
        Dictionary<string, Player> players,
        int maxTicks,
        long initialGoldPrice,
        int newsIntervalMin,
        int newsIntervalMax,
        int researchWindow,
        int researchSettlementDelay,
        long baseResearchReward,
        int npcOrdersPerTick,
        int monthNumber = 1)
    {
        MonthNumber = monthNumber;
        _maxTicks = maxTicks;
        _initialGoldPrice = initialGoldPrice;
        _players = players;
        _orderBook = new OrderBook(initialGoldPrice);
        _matchEngine = new MatchEngine(_orderBook, players);
        _newsSystem = new NewsSystem(newsIntervalMin, newsIntervalMax, researchWindow);
        _npcTrader = new NPCTrader(npcOrdersPerTick);
        _researchSystem = new ResearchSystem(_newsSystem, baseResearchReward, researchWindow, researchSettlementDelay);
    }

    public void Initialize()
    {
        _currentTick = 0;
        _isFinished = false;
        _hasPendingNotifications = false;
        _midPriceHistory.Clear();
        _tradesThisDay.Clear();
        _settledReportsThisDay.Clear();
        _publishedNewsThisDay.Clear();
        _skillEffectsThisDay.Clear();
        _pendingInsiderPreviews.Clear();

        foreach (var player in _players.Values)
        {
            player.ResetDailyActionCounters();
        }

        SeedInitialLiquidity();
        RecordMidPrice(0);
    }

    public void Tick()
    {
        lock (_lock)
        {
            if (_isFinished)
                return;

            _currentTick++;
            if (_currentTick > _maxTicks)
            {
                _isFinished = true;
                return;
            }

            foreach (var player in _players.Values)
            {
                player.ResetDailyActionCounters();
                player.UpdateLockedGold(_currentTick);
                StrategyCardManager.TickCards(player, _currentTick);
            }

            var publishedNews = _newsSystem.Tick(_currentTick);
            if (publishedNews != null)
            {
                _publishedNewsThisDay.Add(publishedNews);
            }

            CheckInsiderNewsPreview();

            _npcTrader.GenerateOrders(_matchEngine, _orderBook, _newsSystem.CurrentSentiment, _currentTick);
            _tradesThisDay.AddRange(_matchEngine.ProcessDay(_currentTick));
            RecordMidPrice(_currentTick);
            SettlePendingReports();

            _hasPendingNotifications = _publishedNewsThisDay.Count > 0
                || _pendingInsiderPreviews.Count > 0
                || _skillEffectsThisDay.Count > 0
                || _settledReportsThisDay.Count > 0
                || _tradesThisDay.Count > 0;

            if (_currentTick >= _maxTicks)
            {
                _isFinished = true;
            }
        }
    }

    public void MarkNotificationsPublished()
    {
        _tradesThisDay.Clear();
        _settledReportsThisDay.Clear();
        _publishedNewsThisDay.Clear();
        _skillEffectsThisDay.Clear();
        _pendingInsiderPreviews.Clear();
        _hasPendingNotifications = false;
    }

    public bool HandleLimitBuy(string playerToken, long price, int quantity)
    {
        lock (_lock)
        {
            return HandleLimitOrder(playerToken, OrderSide.Buy, price, quantity);
        }
    }

    public bool HandleLimitSell(string playerToken, long price, int quantity)
    {
        lock (_lock)
        {
            return HandleLimitOrder(playerToken, OrderSide.Sell, price, quantity);
        }
    }

    public bool HandleCancelOrder(string playerToken, long orderId)
    {
        lock (_lock)
        {
            if (_isFinished) return false;
            return _matchEngine.CancelOrder(playerToken, orderId);
        }
    }

    public bool HandleSubmitReport(string playerToken, int newsId, Prediction prediction)
    {
        lock (_lock)
        {
            if (_isFinished) return false;
            if (!_players.TryGetValue(playerToken, out var player)) return false;
            if (!player.CanSubmitReport(newsId)) return false;

            var report = _researchSystem.SubmitReport(playerToken, newsId, prediction, _currentTick);
            if (report == null)
                return false;

            player.MarkReportSubmitted(newsId);
            return true;
        }
    }

    public bool HandleActivateSkill(string playerToken, string skillName, int? targetPlayerId = null, string? variant = null)
    {
        lock (_lock)
        {
            if (_isFinished) return false;
            if (!_players.TryGetValue(playerToken, out var player)) return false;

            string? targetToken = null;
            if (targetPlayerId.HasValue)
            {
                var targetPlayer = _players.Values.FirstOrDefault(p => p.PlayerId == targetPlayerId.Value);
                if (targetPlayer == null) return false;
                targetToken = targetPlayer.Token;
            }

            var card = StrategyCardManager.FindActiveCard(player, skillName);
            if (card == null) return false;

            return card switch
            {
                InsiderInfo insider => ActivateInsiderInfo(player, insider, variant),
                FlashTrading flash => ActivateFlashTrading(player, flash),
                StopLossBlade blade => ActivateStopLossBlade(player, blade),
                TargetedPurchase targeted => ActivateTargetedPurchase(player, targeted),
                NetworkStorm storm => ActivateNetworkStorm(player, storm, targetToken),
                PublicOpinionAttack attack => ActivatePublicOpinionAttack(player, attack),
                _ => false
            };
        }
    }

    public Dictionary<string, long> CalculateSettlement()
    {
        long midPrice = _orderBook.MidPrice;
        return _players.Values.ToDictionary(player => player.Token, player => player.CalculateNAV(midPrice));
    }

    public List<Order> GetPlayerPendingOrders(string playerToken)
    {
        return _matchEngine.GetPendingOrders(playerToken)
            .Concat(_orderBook.GetPlayerOrders(playerToken))
            .OrderBy(order => order.ArrivalTick)
            .ThenBy(order => order.OrderId)
            .ToList();
    }

    public void CancelPlayerOrders(string playerToken)
    {
        lock (_lock)
        {
            foreach (var order in GetPlayerPendingOrders(playerToken))
            {
                _matchEngine.CancelOrder(playerToken, order.OrderId);
            }
        }
    }

    public int GetPlayerTradeCount(string playerToken)
    {
        return _players.TryGetValue(playerToken, out var player)
            ? player.MonthlyTradeCount
            : 0;
    }

    public long GetMidPriceAtTick(int tick)
    {
        if (_midPriceHistory.TryGetValue(tick, out var price))
            return price;

        if (_midPriceHistory.Count == 0)
            return _initialGoldPrice;

        int nearestTick = _midPriceHistory.Keys.MinBy(key => Math.Abs(key - tick));
        return _midPriceHistory[nearestTick];
    }

    private bool HandleLimitOrder(string playerToken, OrderSide side, long price, int quantity)
    {
        if (_isFinished) return false;
        if (!_players.TryGetValue(playerToken, out var player)) return false;

        int extraDelay = player.ConsumeNextOrderExtraDelayDays();
        int effectiveDelay = player.NetworkDelay + extraDelay;
        int arrivalTick = _currentTick + effectiveDelay;
        if (arrivalTick > _maxTicks)
            return false;

        int priorityRank = player.HasInsiderPriorityDay(arrivalTick) ? 0 : 1;
        var order = _matchEngine.SubmitOrder(playerToken, side, price, quantity, _currentTick, effectiveDelay, priorityRank);
        return order != null;
    }

    private void SeedInitialLiquidity()
    {
        for (int offset = 1; offset <= 5; offset++)
        {
            var buy = new Order("SYSTEM", OrderSide.Buy, Math.Max(1, _initialGoldPrice - offset), 30 + offset * 5, 0, 0, 0)
            {
                Intent = OrderIntent.Resting,
                Status = OrderStatus.Pending
            };
            var sell = new Order("SYSTEM", OrderSide.Sell, _initialGoldPrice + offset, 30 + offset * 5, 0, 0, 0)
            {
                Intent = OrderIntent.Resting,
                Status = OrderStatus.Pending
            };
            _orderBook.AddOrder(buy);
            _orderBook.AddOrder(sell);
        }
    }

    private void CheckInsiderNewsPreview()
    {
        var previewNews = _newsSystem.PreGenerateNextNews();
        if (previewNews == null)
            return;

        foreach (var player in _players.Values)
        {
            var insider = player.ActiveCards.OfType<InsiderInfo>().FirstOrDefault();
            if (insider == null)
                continue;

            if (!insider.TryConsumePreview(_currentTick, out var isFake))
                continue;

            var delivered = isFake ? _newsSystem.CreateSpoofedView(previewNews) : previewNews;
            _pendingInsiderPreviews.Add((player.Token, delivered));
        }
    }

    private void SettlePendingReports()
    {
        foreach (var report in _researchSystem.SettleReports(_currentTick, GetMidPriceAtTick))
        {
            if (!_players.TryGetValue(report.PlayerToken, out var player))
                continue;

            if (report.Reward > 0)
            {
                player.AddMora(report.Reward);
            }
            else if (report.Reward < 0)
            {
                long penalty = Math.Min(Math.Abs(report.Reward), player.Mora);
                player.AddMora(-penalty);
                report.Reward = -penalty;
            }

            _settledReportsThisDay.Add(report);
        }
    }

    private void RecordMidPrice(int tick)
    {
        _midPriceHistory[tick] = _orderBook.MidPrice;
    }

    private bool ActivateInsiderInfo(Player player, InsiderInfo insider, string? variant)
    {
        int nextNewsDay = _newsSystem.NextNewsTick;
        if (nextNewsDay == int.MaxValue || !insider.CanActivate(_currentTick, nextNewsDay))
            return false;

        bool cheapMode = string.Equals(variant, "cheap", StringComparison.OrdinalIgnoreCase);
        long cost = cheapMode ? insider.CheapCost : insider.PremiumCost;
        if (player.Mora < cost)
            return false;

        bool previewIsFake = false;
        if (cheapMode)
        {
            previewIsFake = player.ConsumePendingCheapInsiderCorruption() || Random.Shared.NextDouble() < 0.5;
        }

        player.AddMora(-cost);
        insider.Activate(player, _currentTick, nextNewsDay, cheapMode, previewIsFake);
        _skillEffectsThisDay.Add(new SkillActivation(
            player.PlayerId,
            insider.Name,
            cheapMode ? $"cheap preview for day {nextNewsDay}" : $"premium preview for day {nextNewsDay}"));
        return true;
    }

    private bool ActivateFlashTrading(Player player, FlashTrading flash)
    {
        if (!flash.CanActivateThisMonth || player.Mora < flash.ActivationCost)
            return false;

        player.AddMora(-flash.ActivationCost);
        flash.OnActivate(player, _currentTick);
        _skillEffectsThisDay.Add(new SkillActivation(player.PlayerId, flash.Name, "gains one extra immediate trade for the next 3 days"));
        return true;
    }

    private bool ActivateStopLossBlade(Player player, StopLossBlade blade)
    {
        if (blade.UsedThisMonth || player.Mora < blade.ActivationCost)
            return false;

        foreach (var order in GetPlayerPendingOrders(player.Token))
        {
            _matchEngine.CancelOrder(player.Token, order.OrderId);
        }

        player.AddMora(-blade.ActivationCost);
        blade.Activate(player, _currentTick, _orderBook.MidPrice);
        _skillEffectsThisDay.Add(new SkillActivation(player.PlayerId, blade.Name, "all open orders cancelled and downside protection enabled"));
        return true;
    }

    private bool ActivateTargetedPurchase(Player player, TargetedPurchase targeted)
    {
        if (targeted.IsUsed)
            return false;

        long bestBid = _orderBook.BestBid ?? _orderBook.MidPrice;
        long discountPrice = Math.Max(1, (long)Math.Floor(bestBid * (1.0 - targeted.DiscountRate)));
        long cost = discountPrice * targeted.PurchaseQuantity;
        if (player.Mora < cost)
            return false;

        player.AddMora(-cost);
        player.AddLockedGold(targeted.PurchaseQuantity, _currentTick + targeted.LockDuration);
        targeted.MarkUsed();
        _skillEffectsThisDay.Add(new SkillActivation(player.PlayerId, targeted.Name, $"bought {targeted.PurchaseQuantity} locked gold at {discountPrice}"));
        return true;
    }

    private bool ActivateNetworkStorm(Player player, NetworkStorm storm, string? targetToken)
    {
        if (!storm.CanUse || string.IsNullOrWhiteSpace(targetToken))
            return false;
        if (!_players.TryGetValue(targetToken, out var targetPlayer))
            return false;
        if (player.Mora < storm.ActivationCost)
            return false;

        player.AddMora(-storm.ActivationCost);
        targetPlayer.AddNextOrderExtraDelayDays(1);
        storm.MarkUsed();
        _skillEffectsThisDay.Add(new SkillActivation(player.PlayerId, storm.Name, "next order delayed by 1 day", targetPlayer.PlayerId));
        return true;
    }

    private bool ActivatePublicOpinionAttack(Player player, PublicOpinionAttack attack)
    {
        if (!attack.CanUse || player.Mora < attack.ActivationCost)
            return false;

        player.AddMora(-attack.ActivationCost);
        attack.MarkUsed();

        var sentiment = Random.Shared.Next(2) == 0
            ? NewsSentiment.Bullish
            : NewsSentiment.Bearish;
        var fakeNews = _newsSystem.InjectFakeNews(_currentTick, player.Token, sentiment);
        _publishedNewsThisDay.Add(fakeNews);

        foreach (var other in _players.Values.Where(other => other.Token != player.Token))
        {
            other.PendingFakeBroadcast = true;
            other.PendingCheapInsiderCorruption = true;
        }

        _skillEffectsThisDay.Add(new SkillActivation(player.PlayerId, attack.Name, "broadcasted a fake market news item"));
        return true;
    }
}

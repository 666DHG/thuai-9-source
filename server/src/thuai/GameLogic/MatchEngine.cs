using Thuai.GameLogic.StrategyCards;

namespace Thuai.GameLogic;

public class MatchEngine
{
    private const string SystemToken = "SYSTEM";

    private readonly OrderBook _orderBook;
    private readonly Dictionary<string, Player> _players;
    private readonly List<Order> _pendingOrders = new();
    private readonly List<Trade> _tradesThisDay = new();

    public event Action<Trade>? OnTradeExecuted;

    public OrderBook OrderBook => _orderBook;
    public IReadOnlyList<Order> PendingOrders => _pendingOrders;

    public MatchEngine(OrderBook orderBook, Dictionary<string, Player> players)
    {
        _orderBook = orderBook;
        _players = players;
    }

    public Order? SubmitOrder(string playerToken, OrderSide side, long price, int quantity,
        int currentTick, int networkDelay = 0, int priorityRank = 1, bool isIceberg = false)
    {
        if (price <= 0 || quantity <= 0)
            return null;

        Player? player = null;
        if (playerToken != SystemToken)
        {
            if (!_players.TryGetValue(playerToken, out player))
                return null;
        }

        long feeReserve = 0;
        if (playerToken != SystemToken)
        {
            if (side == OrderSide.Buy)
            {
                long notional = price * quantity;
                feeReserve = (long)Math.Ceiling(notional * player!.TransactionFeeRate);
                long totalReserve = notional + feeReserve;
                if (player.Mora < totalReserve)
                    return null;
                player.FreezeMora(totalReserve);
            }
            else
            {
                if (player!.Gold < quantity)
                    return null;
                player.FreezeGold(quantity);
            }
        }

        var order = new Order(playerToken, side, price, quantity, currentTick, networkDelay, priorityRank, isIceberg)
        {
            FrozenFeeRemaining = feeReserve
        };
        _pendingOrders.Add(order);
        return order;
    }

    public bool CancelOrder(string playerToken, long orderId)
    {
        var pending = _pendingOrders.FirstOrDefault(o => o.OrderId == orderId);
        if (pending != null)
        {
            if (pending.PlayerToken != playerToken)
                return false;

            RefundPendingOrder(pending);
            pending.Status = OrderStatus.Cancelled;
            _pendingOrders.Remove(pending);
            return true;
        }

        var order = _orderBook.GetOrder(orderId);
        if (order == null || order.PlayerToken != playerToken)
            return false;
        if (order.Status is OrderStatus.Filled or OrderStatus.Cancelled)
            return false;

        RefundActiveOrder(order);
        order.Status = OrderStatus.Cancelled;
        _orderBook.RemoveOrder(orderId);
        return true;
    }

    public List<Trade> ProcessDay(int currentDay)
    {
        _tradesThisDay.Clear();

        var arrived = _pendingOrders
            .Where(order => order.ArrivalTick <= currentDay)
            .OrderBy(order => order.PriorityRank)
            .ThenBy(order => order.ArrivalTick)
            .ThenBy(order => order.SubmitSequence)
            .ThenBy(order => order.OrderId)
            .ToList();

        _pendingOrders.RemoveAll(order => order.ArrivalTick <= currentDay);

        foreach (var order in arrived)
        {
            ProcessOrder(order, currentDay);
        }

        return new List<Trade>(_tradesThisDay);
    }

    public List<Trade> ProcessTick(int currentTick) => ProcessDay(currentTick);

    public List<Order> GetPendingOrders(string playerToken)
    {
        return _pendingOrders.Where(o => o.PlayerToken == playerToken).ToList();
    }

    private void ProcessOrder(Order order, int currentDay)
    {
        bool marketable = IsMarketable(order);

        if (_players.TryGetValue(order.PlayerToken, out var player))
        {
            bool accepted = marketable ? player.CanPlaceImmediateOrder() : player.CanPlaceRestingOrder();
            if (!accepted)
            {
                RefundPendingOrder(order);
                order.Status = OrderStatus.Cancelled;
                return;
            }

            if (marketable)
                player.MarkImmediateOrder();
            else
                player.MarkRestingOrder();
        }

        order.Intent = marketable ? OrderIntent.Immediate : OrderIntent.Resting;

        while (order.RemainingQuantity > 0)
        {
            var opposite = order.Side == OrderSide.Buy
                ? _orderBook.BestAskOrder
                : _orderBook.BestBidOrder;

            if (opposite == null)
                break;

            bool crosses = order.Side == OrderSide.Buy
                ? order.Price >= opposite.Price
                : order.Price <= opposite.Price;
            if (!crosses)
                break;

            long tradePrice = opposite.Price;
            int tradeQuantity = Math.Min(order.RemainingQuantity, opposite.RemainingQuantity);
            ExecuteTrade(order, opposite, tradePrice, tradeQuantity, currentDay);
        }

        if (order.RemainingQuantity <= 0)
        {
            order.Status = OrderStatus.Filled;
            return;
        }

        if (order.Intent == OrderIntent.Immediate)
        {
            RefundUnfilledImmediate(order);
            order.Status = order.Status == OrderStatus.PartiallyFilled
                ? OrderStatus.PartiallyFilled
                : OrderStatus.Cancelled;
            return;
        }

        order.Status = order.Status == OrderStatus.PartiallyFilled
            ? OrderStatus.PartiallyFilled
            : OrderStatus.Pending;
        _orderBook.AddOrder(order);
    }

    private bool IsMarketable(Order order)
    {
        return order.Side == OrderSide.Buy
            ? _orderBook.BestAsk is long ask && order.Price >= ask
            : _orderBook.BestBid is long bid && order.Price <= bid;
    }

    private void ExecuteTrade(Order taker, Order maker, long price, int quantity, int currentDay)
    {
        taker.RemainingQuantity -= quantity;
        maker.RemainingQuantity -= quantity;

        taker.Status = taker.RemainingQuantity == 0
            ? OrderStatus.Filled
            : OrderStatus.PartiallyFilled;
        maker.Status = maker.RemainingQuantity == 0
            ? OrderStatus.Filled
            : OrderStatus.PartiallyFilled;

        if (maker.Status == OrderStatus.Filled)
            _orderBook.RemoveOrder(maker.OrderId);

        long tradeAmount = price * quantity;
        long buyerFee = CalculateFee(taker.Side == OrderSide.Buy ? taker.PlayerToken : maker.PlayerToken, tradeAmount);
        long sellerFee = CalculateFee(taker.Side == OrderSide.Sell ? taker.PlayerToken : maker.PlayerToken, tradeAmount);

        if (taker.Side == OrderSide.Buy)
            ApplyBuyerFill(taker, price, quantity, buyerFee);
        else
            ApplySellerFill(taker, price, quantity, sellerFee);

        if (maker.PlayerToken != SystemToken && _players.TryGetValue(maker.PlayerToken, out var makerPlayer))
        {
            if (maker.Side == OrderSide.Buy)
                ApplyBuyerFill(maker, price, quantity, buyerFee);
            else
                ApplySellerFill(maker, price, quantity, sellerFee);
        }

        if (taker.PlayerToken != maker.PlayerToken)
        {
            if (taker.PlayerToken != SystemToken && _players.TryGetValue(taker.PlayerToken, out var takerPlayer))
                takerPlayer.AddMonthlyTradeCount();
            if (maker.PlayerToken != SystemToken && _players.TryGetValue(maker.PlayerToken, out var makerTradePlayer))
                makerTradePlayer.AddMonthlyTradeCount();
        }

        _orderBook.UpdateLastPrice(price);
        _orderBook.IncrementVolume(quantity);

        var trade = new Trade
        {
            BuyOrderId = taker.Side == OrderSide.Buy ? taker.OrderId : maker.OrderId,
            SellOrderId = taker.Side == OrderSide.Sell ? taker.OrderId : maker.OrderId,
            BuyerToken = taker.Side == OrderSide.Buy ? taker.PlayerToken : maker.PlayerToken,
            SellerToken = taker.Side == OrderSide.Sell ? taker.PlayerToken : maker.PlayerToken,
            Price = price,
            Quantity = quantity,
            Tick = currentDay,
            BuyerFee = buyerFee,
            SellerFee = sellerFee
        };

        _tradesThisDay.Add(trade);
        OnTradeExecuted?.Invoke(trade);
    }

    private long CalculateFee(string playerToken, long tradeAmount)
    {
        if (playerToken == SystemToken)
            return 0;

        if (!_players.TryGetValue(playerToken, out var player))
            return 0;

        return (long)(tradeAmount * player.TransactionFeeRate);
    }

    private void ApplyBuyerFill(Order order, long price, int quantity, long fee)
    {
        if (order.PlayerToken == SystemToken)
            return;

        if (!_players.TryGetValue(order.PlayerToken, out var player))
            return;

        long pricePortion = order.Price * quantity;
        long tradeAmount = price * quantity;
        long feeFromBuffer = Math.Min(fee, order.FrozenFeeRemaining);

        player.SpendFrozenMora(tradeAmount + feeFromBuffer);
        order.FrozenFeeRemaining -= feeFromBuffer;

        long priceRefund = pricePortion - tradeAmount;
        if (priceRefund > 0)
            player.UnfreezeMora(priceRefund);

        long feeShortfall = fee - feeFromBuffer;
        if (feeShortfall > 0)
        {
            long shortfallFromAvailable = Math.Min(feeShortfall, player.Mora);
            if (shortfallFromAvailable > 0)
                player.AddMora(-shortfallFromAvailable);
        }

        if (order.RemainingQuantity == 0 && order.FrozenFeeRemaining > 0)
        {
            player.UnfreezeMora(order.FrozenFeeRemaining);
            order.FrozenFeeRemaining = 0;
        }

        player.AddGold(quantity);
    }

    private void ApplySellerFill(Order order, long price, int quantity, long fee)
    {
        if (order.PlayerToken == SystemToken)
            return;

        if (!_players.TryGetValue(order.PlayerToken, out var player))
            return;

        player.SpendFrozenGold(quantity);
        long proceeds = price * quantity - fee;
        if (proceeds != 0)
            player.AddMora(proceeds);
    }

    private void RefundPendingOrder(Order order)
    {
        if (order.PlayerToken == SystemToken)
            return;

        if (!_players.TryGetValue(order.PlayerToken, out var player))
            return;

        if (order.Side == OrderSide.Buy)
        {
            long refund = order.Price * order.RemainingQuantity + order.FrozenFeeRemaining;
            if (refund > 0)
                player.UnfreezeMora(refund);
            order.FrozenFeeRemaining = 0;
        }
        else
        {
            player.UnfreezeGold(order.RemainingQuantity);
        }
    }

    private void RefundActiveOrder(Order order)
    {
        RefundPendingOrder(order);
    }

    private void RefundUnfilledImmediate(Order order)
    {
        if (order.PlayerToken == SystemToken || order.RemainingQuantity <= 0)
            return;

        RefundPendingOrder(order);
    }
}

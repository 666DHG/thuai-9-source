namespace Thuai.GameLogic;

public class Order
{
    private static long _nextId = 1;
    private static long _nextSequence = 0;

    public long OrderId { get; }
    public long SubmitSequence { get; }
    public string PlayerToken { get; }
    public OrderSide Side { get; }
    public long Price { get; }
    public int Quantity { get; }
    public int RemainingQuantity { get; set; }
    public int SubmitTick { get; }
    public int ArrivalTick { get; }
    public int PriorityRank { get; }
    public OrderIntent? Intent { get; set; }
    public OrderStatus Status { get; set; }
    public bool IsIceberg { get; }
    public int VisibleQuantity => RemainingQuantity;

    public long FrozenFeeRemaining { get; set; }

    public Order(string playerToken, OrderSide side, long price, int quantity,
        int submitTick, int networkDelay, int priorityRank = 0, bool isIceberg = false)
    {
        OrderId = Interlocked.Increment(ref _nextId);
        SubmitSequence = Interlocked.Increment(ref _nextSequence);
        PlayerToken = playerToken;
        Side = side;
        Price = price;
        Quantity = quantity;
        RemainingQuantity = quantity;
        SubmitTick = submitTick;
        ArrivalTick = submitTick + networkDelay;
        PriorityRank = priorityRank;
        Status = OrderStatus.Pending;
        IsIceberg = isIceberg;
    }
}

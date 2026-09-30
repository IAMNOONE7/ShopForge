using ShopForge.Shared.Tenancy;

namespace ShopForge.Orders.Domain;

// What a customer sends back, and how far along it is. Goods and money move when the parcel arrives, not when the
// store agrees to take it (D-095).
internal sealed class OrderReturn : IStoreOwned
{
    public const int MaxReasonLength = 1000;

    private readonly List<OrderReturnLine> _lines = [];

    private OrderReturn()
    {
    }

    public OrderReturn(Guid storeId, string number, string orderNumber, Guid? storeCustomerId, string? reason, DateTimeOffset requestedAt)
    {
        Id = Guid.CreateVersion7();
        StoreId = storeId;
        Number = number;
        OrderNumber = orderNumber;
        StoreCustomerId = storeCustomerId;
        Reason = reason;
        Status = ReturnStatus.Requested;
        RequestedAt = requestedAt;
    }

    public Guid Id { get; private set; }

    public Guid StoreId { get; private set; }

    public string Number { get; private set; } = null!;

    public string OrderNumber { get; private set; } = null!;

    // Null when the store made the return itself, for example when it refunds an order outright (D-098).
    public Guid? StoreCustomerId { get; private set; }

    public string? Reason { get; private set; }

    public ReturnStatus Status { get; private set; }

    public DateTimeOffset RequestedAt { get; private set; }

    public DateTimeOffset? DecidedAt { get; private set; }

    public DateTimeOffset? ReceivedAt { get; private set; }

    // What was actually paid back for it, which is only known once the parcel is here.
    public decimal RefundedAmount { get; private set; }

    public IReadOnlyList<OrderReturnLine> Lines => _lines;

    // A return still waiting for a decision, or waiting for the parcel, keeps its goods out of another return.
    public bool HoldsGoods => Status is ReturnStatus.Requested or ReturnStatus.Accepted or ReturnStatus.Received;

    public void AddLine(Guid storeProductId, Guid variantId, string productName, int quantity)
    {
        if (quantity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "A return line needs at least one item.");
        }

        _lines.Add(new OrderReturnLine(storeProductId, variantId, productName, quantity));
    }

    public bool Accept(DateTimeOffset decidedAt)
    {
        if (Status != ReturnStatus.Requested)
        {
            return false;
        }

        Status = ReturnStatus.Accepted;
        DecidedAt = decidedAt;

        return true;
    }

    public bool Refuse(DateTimeOffset decidedAt)
    {
        if (Status is not (ReturnStatus.Requested or ReturnStatus.Accepted))
        {
            return false;
        }

        Status = ReturnStatus.Refused;
        DecidedAt = decidedAt;

        return true;
    }

    public bool Receive(decimal refunded, DateTimeOffset receivedAt)
    {
        if (Status != ReturnStatus.Accepted)
        {
            return false;
        }

        Status = ReturnStatus.Received;
        ReceivedAt = receivedAt;
        RefundedAmount = refunded;

        return true;
    }

    // The goods coming back are the store's record; who sent them, and what they wrote about themselves, are not.
    public void Anonymise()
    {
        StoreCustomerId = null;
        Reason = null;
    }
}



internal sealed class OrderReturnLine
{
    private OrderReturnLine()
    {
    }

    internal OrderReturnLine(Guid storeProductId, Guid variantId, string productName, int quantity)
    {
        StoreProductId = storeProductId;
        VariantId = variantId;
        ProductName = productName;
        Quantity = quantity;
    }

    public Guid StoreProductId { get; private set; }

    // Which form of it came back: two sizes of one shirt are two lines of one order and two lines of its return.
    public Guid VariantId { get; private set; }

    // The name as the order has it, so a renamed product does not rewrite the paperwork.
    public string ProductName { get; private set; } = null!;

    public int Quantity { get; private set; }
}

internal enum ReturnStatus
{
    Requested,
    Accepted,
    Refused,
    Received,
}

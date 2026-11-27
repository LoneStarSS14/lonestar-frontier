namespace Content.Server._LoneStar.Shipyard.Components;

/// <summary>
/// Stores the purchase state while a spawned shuttle is waiting for asynchronous docking.
/// </summary>
[RegisterComponent]
public sealed partial class PendingShipyardPurchaseComponent : Component
{
    public EntityUid StationUid;
    public EntityUid Purchaser;
    public EntityUid PaymentEntity;
    public EntityUid ConsoleUid;
    public int Price;
    public bool VoucherUsed;
}

namespace FakutoriArchipelago;

/// <summary>
/// Centralized magic numbers. When the full game ships, update block IDs here first.
/// </summary>
public static class Constants
{
    // Base element block IDs — given to the player from the start; not AP check locations.
    public const int BaseElement1BlockId = 11;
    public const int BaseElement2BlockId = 14;
    public const int BaseElement3BlockId = 15;
    public const int BaseElement4BlockId = 16;

    // The Quasar legendary block; acts as a check location and a possible victory condition.
    public const int QuasarBlockId = 50;

    // One machine block ships with blockId == 0, which Archipelago can't handle.
    // We remap it to this value at runtime.
    public const int MachinePlaceholderBlockId = 100;

    // Filler item IDs — give currency/resources instead of unlocking a block.
    public const int Filler500GoldItemId   = 1000;
    public const int Filler1000GoldItemId  = 1001;
    public const int Filler500ManaItemId   = 1002;
    public const int FillerFullStarpowerItemId = 1003;
}

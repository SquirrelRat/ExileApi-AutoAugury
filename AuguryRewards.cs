namespace AutoAugury;

/// <summary>
/// Every reward outcome the plugin knows: the catalogue's export, then the rows read from later clients
/// (AuguryCatalogueSupplement). One list, so the lookups, the families and their weights cannot drift apart: a
/// supplemented row whose family the weight table does not know would match a card and then count nothing.
/// </summary>
internal static class AuguryRewards
{
    public static readonly AuguryReward[] All =
        [.. AuguryCatalogue.Rewards, .. AuguryCatalogueSupplement.Rewards];
}

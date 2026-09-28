namespace ShopForge.Orders.Domain;

// What is left where a person used to be. Deliberately not a blank: somebody reading an old order should see that
// a buyer was erased, not wonder whether the field was ever filled in (D-117).
internal static class PersonalData
{
    public const string Erased = "(erased)";

    // ZZ is the country code kept aside for private use, so it can never collide with a real one.
    public static Address ErasedAddress { get; } = new(Erased, Erased, null, Erased, Erased, "ZZ");
}

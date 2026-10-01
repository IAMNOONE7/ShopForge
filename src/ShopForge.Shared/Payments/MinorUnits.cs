namespace ShopForge.Shared.Payments;

// Gateways take amounts in the currency's smallest unit, and getting it wrong moves the decimal point on
// somebody's money. One place, in decimal arithmetic, so that a float never comes near it.
public static class MinorUnits
{
    // Every currency ShopForge sells in has two decimals. The ones that do not — JPY, HUF, KRW — would be
    // charged a hundred times over by this, so they are refused rather than quietly converted; selling in one
    // means teaching this method about it first (Stage 29).
    private static readonly string[] WithoutDecimals = ["BIF", "CLP", "DJF", "GNF", "ISK", "JPY", "KMF", "KRW", "PYG", "RWF", "UGX", "VND", "VUV", "XAF", "XOF", "XPF"];

    public static long Of(decimal amount, string currency)
    {
        if (WithoutDecimals.Contains(currency.Trim().ToUpperInvariant()))
        {
            throw new NotSupportedException($"{currency} has no minor unit, and ShopForge has not been taught how to charge in it.");
        }

        return (long)decimal.Round(amount * 100, 0, MidpointRounding.AwayFromZero);
    }
}

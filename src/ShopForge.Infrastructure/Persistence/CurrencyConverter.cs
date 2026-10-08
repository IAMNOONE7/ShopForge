using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using ShopForge.Shared.Payments;

namespace ShopForge.Infrastructure.Persistence;

// The column holds the ISO code, because that is what an auditor, an export and another system all read. The
// domain holds the currency, because the code alone does not say how its money rounds (D-186).
internal sealed class CurrencyConverter() : ValueConverter<Currency, string>(
    currency => currency.Code,
    code => Currency.Of(code));

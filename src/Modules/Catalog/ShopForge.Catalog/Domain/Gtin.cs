namespace ShopForge.Catalog.Domain;

// A barcode is a number that checks itself. Its last digit is computed from the others, so a mistyped one is
// almost always detectable — and a shopping feed rejects the whole product for an invalid barcode rather than
// dropping the field, which is why this stops at "8 to 14 digits" no longer (D-163).
internal static class Gtin
{
    // The four lengths in use: EAN-8, UPC-A, EAN-13 and the GTIN-14 of a shipping case.
    private static readonly int[] Lengths = [8, 12, 13, 14];

    public static bool IsValid(string? barcode)
    {
        if (barcode is null)
        {
            return true;
        }

        var digits = barcode.Trim();

        if (!Lengths.Contains(digits.Length) || !digits.All(char.IsAsciiDigit))
        {
            return false;
        }

        return CheckDigitOf(digits) == digits[^1] - '0';
    }

    // Every GTIN length uses the same rule: weight the digits 3 and 1 alternately from the right of the body,
    // and the check digit is what takes the total up to a multiple of ten.
    private static int CheckDigitOf(string digits)
    {
        var body = digits[..^1];
        var total = 0;

        for (var position = 0; position < body.Length; position++)
        {
            var weight = (body.Length - position) % 2 == 1 ? 3 : 1;
            total += (body[position] - '0') * weight;
        }

        return (10 - (total % 10)) % 10;
    }
}

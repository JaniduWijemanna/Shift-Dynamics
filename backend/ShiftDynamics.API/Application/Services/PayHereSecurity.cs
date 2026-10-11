using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace ShiftDynamics.API.Application.Services;

public static class PayHereSecurity
{
    // PayHere specifies MD5 for its checkout and callback protocol; this is not password hashing.
    public static string Md5(string value) => Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(value)));
    public static string Amount(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);
    public static string CheckoutHash(string merchant, string order, decimal amount, string secret) =>
        Md5(merchant + order + Amount(amount) + "LKR" + Md5(secret));
    public static bool Verify(string merchant, string order, string amount, string currency,
        string status, string signature, string secret)
    {
        if (!Regex.IsMatch(signature, "\\A[0-9a-fA-F]{32}\\z")) return false;
        var expected = Md5(merchant + order + amount + currency + status + Md5(secret));
        return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(expected), Convert.FromHexString(signature));
    }
    public static bool TryAmount(string raw, out decimal amount)
    {
        amount = 0;
        return Regex.IsMatch(raw, "\\A[0-9]{1,10}\\.[0-9]{2}\\z") &&
            decimal.TryParse(raw, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount) && amount > 0;
    }
    public static bool TryOrder(string order, out Guid id)
    {
        id = Guid.Empty;
        return order.StartsWith("SD-SBX-", StringComparison.Ordinal) && order.Length == 39 &&
            Guid.TryParseExact(order[7..], "N", out id);
    }
}

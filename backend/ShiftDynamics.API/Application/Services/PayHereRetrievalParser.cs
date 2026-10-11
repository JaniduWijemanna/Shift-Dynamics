using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ShiftDynamics.API.Application.Services;

public sealed record RetrievedPayHerePayment(string PaymentId, decimal Amount, string Status);
public static class PayHereRetrievalParser
{
    // A successful API request is not proof of a successful payment: inspect each data record.
    public static IReadOnlyList<RetrievedPayHerePayment> Parse(string json, string expectedOrder)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("status", out var top) || !top.TryGetInt32(out var code))
            throw new InvalidDataException("Unexpected PayHere response.");
        if (code is 0 or -1)
        {
            var message = root.TryGetProperty("msg", out var m) ? m.GetString() ?? "" : "";
            if (message.Contains("access", StringComparison.OrdinalIgnoreCase) || message.Contains("auth", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("PayHere API credentials or allowed domain are invalid.");
            return Array.Empty<RetrievedPayHerePayment>();
        }
        if (code != 1 || !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("PayHere did not return a successful payment lookup.");
        var results = new List<RetrievedPayHerePayment>();
        foreach (var row in data.EnumerateArray())
        {
            if (!row.TryGetProperty("order_id", out var order) || order.GetString() != expectedOrder ||
                !row.TryGetProperty("currency", out var currency) || currency.GetString() != "LKR")
                throw new InvalidDataException("PayHere returned a different order or currency.");
            if (!row.TryGetProperty("payment_id", out var pid)) throw new InvalidDataException("Missing provider payment ID.");
            var id = pid.ValueKind == JsonValueKind.String ? pid.GetString() ?? "" : pid.GetRawText();
            if (!Regex.IsMatch(id, "\\A[0-9]{1,64}\\z")) throw new InvalidDataException("Invalid provider payment ID.");
            if (!row.TryGetProperty("amount", out var raw)) throw new InvalidDataException("Missing payment amount.");
            var text = raw.ValueKind == JsonValueKind.String ? raw.GetString() ?? "" : raw.GetRawText();
            if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount) ||
                amount <= 0 || amount != decimal.Round(amount, 2)) throw new InvalidDataException("Invalid payment amount.");
            var status = row.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "";
            if (status is not ("RECEIVED" or "REFUND REQUESTED" or "REFUND PROCESSING" or "REFUNDED" or "CHARGEBACKED"))
                throw new InvalidDataException("Unknown provider payment state.");
            if (results.Any(p => p.PaymentId == id)) throw new InvalidDataException("Duplicate provider payment ID in response.");
            results.Add(new RetrievedPayHerePayment(id, amount, status));
        }
        return results;
    }
}

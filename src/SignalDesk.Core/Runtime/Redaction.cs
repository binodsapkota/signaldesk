using System.Globalization;
using System.Text.Json;
using SignalDesk.Core.Domain;

namespace SignalDesk.Core.Runtime;

/// <summary>Masks sensitive values before tool traffic leaves the server and builds human-readable approval prompts.</summary>
public static class Redaction
{
    public static Dictionary<string, object?> MaskArgs(IDictionary<string, object?>? args)
    {
        var masked = new Dictionary<string, object?>();
        if (args is null) return masked;

        foreach ((string key, object? value) in args)
        {
            masked[key] = key.Contains("pin", StringComparison.OrdinalIgnoreCase) ? "****"
                : key.Contains("phone", StringComparison.OrdinalIgnoreCase) ? MaskPhone(Str(value))
                : value;
        }

        return masked;
    }

    public static string MaskPhone(string phone)
    {
        string digits = TelcoData.Digits(phone);
        return digits.Length >= 4 ? $"***-***-{digits[^4..]}" : "****";
    }

    public static string DescribeApproval(string tool, IDictionary<string, object?>? args)
    {
        string A(string key) => args is not null && args.TryGetValue(key, out object? v) ? Str(v) : "";
        string Money(string key) => decimal.TryParse(A(key), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal d) ? d.ToString("C", CultureInfo.GetCultureInfo("en-US")) : A(key);
        string LineRef(string key) => string.IsNullOrWhiteSpace(A(key)) ? "the account holder's line" : $"line {A(key)}";

        return tool switch
        {
            "schedule_technician" => $"Book a technician visit in slot {A("slotId")}",
            "apply_courtesy_credit" => $"Apply a {Money("amount")} courtesy credit ({A("reason")})",
            "set_payment_arrangement" => $"Set up a payment arrangement: {Money("downPayment")} today, remainder over {A("installments")} monthly installment(s)",
            "report_lost_device" => $"Suspend {LineRef("line")} and block the device IMEI (lost/stolen)",
            "update_contact_email" => $"Change the account contact email to {A("newEmail")}",
            "add_international_pass" => $"Add travel pass '{A("passId")}' to {LineRef("lineNumber")}",
            "change_plan" => $"Change {LineRef("lineNumber")} to plan '{A("planId")}'",
            "apply_retention_offer" => $"Apply save offer '{A("offerId")}'",
            "schedule_cancellation" => $"Schedule cancellation of the account (reason: {A("reason")})",
            _ => $"Run {tool}",
        };
    }

    private static string Str(object? value) => value switch
    {
        null => "",
        JsonElement { ValueKind: JsonValueKind.String } e => e.GetString() ?? "",
        JsonElement e => e.ToString(),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };
}

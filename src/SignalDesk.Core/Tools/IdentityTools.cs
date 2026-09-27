using System.ComponentModel;
using SignalDesk.Core.Domain;

namespace SignalDesk.Core.Tools;

public sealed record VerificationResult(bool Verified, string? CustomerName, string? AccountId, int AttemptsRemaining, string Message);

public sealed class IdentityTools(DeskContext ctx)
{
    [Description("Verify the caller's identity using the phone number on the account and the 4-digit account PIN. Must succeed before any account information is shared.")]
    public object VerifyCustomer(
        [Description("Phone number on the account, any format")] string phoneNumber,
        [Description("4-digit account PIN")] string pin)
    {
        if (ctx.IsLocked)
        {
            return new ToolError("Verification is locked after 3 failed attempts. The customer must visit a store with photo ID.");
        }

        string digits = TelcoData.Digits(phoneNumber);
        if (digits.Length == 11 && digits[0] == '1')
        {
            digits = digits[1..];
        }

        Customer? match = ctx.Data.FindByPhone(digits);
        if (ctx.Customer is not null && match is not null && match != ctx.Customer)
        {
            return new ToolError("This conversation is already verified for a different account.");
        }

        if (match is null || match.Pin != TelcoData.Digits(pin))
        {
            ctx.RecordFailedVerification();
            int remaining = DeskContext.MaxVerificationAttempts - ctx.FailedVerifications;
            ctx.Record("verification_failed", $"Attempt {ctx.FailedVerifications} of {DeskContext.MaxVerificationAttempts}");
            return new VerificationResult(false, null, null, remaining,
                remaining > 0 ? "Phone number and PIN do not match our records." : "Verification is now locked.");
        }

        ctx.MarkVerified(match);
        ctx.Record("verified", $"{match.Name} ({match.AccountId})");
        return new VerificationResult(true, match.Name, match.AccountId, DeskContext.MaxVerificationAttempts - ctx.FailedVerifications, "Identity verified.");
    }
}

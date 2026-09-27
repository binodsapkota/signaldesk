using System.ComponentModel;
using System.Text.RegularExpressions;
using SignalDesk.Core.Domain;

namespace SignalDesk.Core.Tools;

public sealed record LineInfo(string Number, string User, string Relationship, string Plan, string Device, string Status);

public sealed record AccountProfile(
    string AccountId, string Name, string Email, string ServiceZip, int TenureYears, IReadOnlyList<LineInfo> Lines, string? HomeInternet);

public sealed record LostDeviceResult(bool Suspended, string Line, string User, string Device, IReadOnlyList<string> NextSteps);

public sealed record EmailChangeResult(bool Updated, string OldEmail, string NewEmail, string Message);

public sealed partial class AccountTools(DeskContext ctx)
{
    [Description("Get the account profile: holder, contact email, service address ZIP, tenure and every line with its user, plan, device and status.")]
    public object GetAccountProfile()
    {
        if (!ctx.TryGetCustomer(out Customer c, out ToolError err)) return err;

        return new AccountProfile(
            c.AccountId, c.Name, MaskEmail(c.Email), c.ServiceZip, c.TenureYears,
            c.Lines.Select(Describe).ToList(),
            c.Fiber is null ? null : Catalog.FindPlan(c.Fiber.PlanId)!.Name);
    }

    [Description("Report a device lost or stolen: immediately suspends the line and blocks the device IMEI on all networks. Requires approval.")]
    public object ReportLostDevice([Description("Line number, last 4 digits, or the line user's name")] string line)
    {
        if (!ctx.TryGetCustomer(out Customer c, out ToolError err)) return err;

        Line? target = ctx.ResolveLine(c, line);
        if (target is null) return new ToolError($"No line matching '{line}' on this account.");

        target.Status = LineStatus.Suspended;
        target.SuspendReason = "lost/stolen";
        target.DeviceBlocked = true;
        ctx.Record("line_suspended", $"{target.Number} ({target.UserName}) - device {target.Device} blocked");
        return new LostDeviceResult(true, target.Number, target.UserName, target.Device,
        [
            "File a police report and keep the report number",
            "Replacement SIM/eSIM available in store or by mail",
            "Line can be restored at no charge once a new device is active",
        ]);
    }

    [Description("Change the contact email address on the account. Requires approval.")]
    public object UpdateContactEmail([Description("New email address")] string newEmail)
    {
        if (!ctx.TryGetCustomer(out Customer c, out ToolError err)) return err;
        if (!EmailPattern().IsMatch(newEmail)) return new ToolError($"'{newEmail}' is not a valid email address.");

        string old = c.Email;
        c.Email = newEmail.Trim();
        ctx.Record("email_updated", $"{MaskEmail(old)} -> {MaskEmail(c.Email)}");
        return new EmailChangeResult(true, MaskEmail(old), c.Email, "Contact email updated. A confirmation was sent to both addresses.");
    }

    internal static LineInfo Describe(Line l) =>
        new(l.Number, l.UserName, l.Relationship, Catalog.FindPlan(l.PlanId)!.Name, l.Device,
            l.Status == LineStatus.Active ? "active" : $"suspended ({l.SuspendReason})");

    internal static string MaskEmail(string email)
    {
        int at = email.IndexOf('@');
        return at <= 1 ? email : $"{email[0]}{new string('*', Math.Min(at - 1, 5))}{email[at..]}";
    }

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[a-z]{2,}$", RegexOptions.IgnoreCase)]
    private static partial Regex EmailPattern();
}

using System.ComponentModel;
using SignalDesk.Core.Domain;

namespace SignalDesk.Core.Tools;

public sealed record OutageInfo(string Service, string Summary, string Eta);

public sealed record NetworkStatusResult(string Zip, IReadOnlyList<OutageInfo> Outages, string Summary);

public sealed record DiagnosticsResult(string Service, string Target, string Status, IReadOnlyList<string> Findings, string? SuspendReason);

public sealed record ResetResult(string Service, string Target, bool Resolved, IReadOnlyList<string> Actions, string Outcome);

public sealed record AppointmentResult(bool Booked, string? AppointmentId, string? Slot, string Message);

public sealed class TechSupportTools(DeskContext ctx)
{
    public const decimal MinHealthySignalDbm = -25m;
    public const string LatestFirmware = "4.3.0";

    [Description("Check for known network outages or maintenance at the customer's service address.")]
    public object CheckNetworkStatus()
    {
        if (!ctx.TryGetCustomer(out Customer c, out ToolError err)) return err;

        List<OutageInfo> outages = Catalog.Outages
            .Where(o => o.Zip == c.ServiceZip)
            .Select(o => new OutageInfo(o.Service, o.Summary, o.Eta))
            .ToList();
        return new NetworkStatusResult(c.ServiceZip, outages,
            outages.Count == 0 ? "No known outages or maintenance in the area." : $"{outages.Count} active incident(s) in the area.");
    }

    [Description("Run remote diagnostics on the customer's home fiber connection or on a mobile line.")]
    public object RunLineDiagnostics(
        [Description("'fiber' for home internet or 'mobile' for a phone line")] string service,
        [Description("Mobile line number or last 4 digits (mobile only; defaults to the account holder's line)")] string? lineNumber = null)
    {
        if (!ctx.TryGetCustomer(out Customer c, out ToolError err)) return err;

        if (IsFiber(service))
        {
            if (c.Fiber is not { } fiber) return new ToolError("This account has no home fiber service.");

            List<string> findings = [$"{fiber.DropsLast24h} connection drops in the last 24 hours"];
            findings.Add($"Optical signal {fiber.OpticalSignalDbm} dBm (healthy is above {MinHealthySignalDbm} dBm)");
            if (fiber.RouterFirmware != LatestFirmware)
            {
                findings.Add($"Router firmware {fiber.RouterFirmware} is out of date (latest {LatestFirmware})");
            }

            bool degraded = fiber.DropsLast24h > 3 || fiber.OpticalSignalDbm < MinHealthySignalDbm;
            return new DiagnosticsResult("fiber", Catalog.FindPlan(fiber.PlanId)!.Name, degraded ? "degraded" : "healthy", findings, null);
        }

        Line? line = ctx.ResolveLine(c, lineNumber);
        if (line is null) return new ToolError($"No line matching '{lineNumber}' on this account.");

        if (line.Status == LineStatus.Suspended)
        {
            return new DiagnosticsResult("mobile", line.Number, "suspended",
                [$"Line is suspended ({line.SuspendReason})", "Device is registered but blocked from the network"], line.SuspendReason);
        }

        return new DiagnosticsResult("mobile", line.Number, "healthy",
            ["Registered on 5G, signal -89 dBm (good)", "No dropped calls in the last 7 days", "Provisioning is current"], null);
    }

    [Description("Remotely reset a connection: re-provisions the fiber ONT and updates router firmware, or refreshes a mobile line's network provisioning.")]
    public object ResetConnection(
        [Description("'fiber' or 'mobile'")] string service,
        [Description("Mobile line number or last 4 digits (mobile only)")] string? lineNumber = null)
    {
        if (!ctx.TryGetCustomer(out Customer c, out ToolError err)) return err;

        if (IsFiber(service))
        {
            if (c.Fiber is not { } fiber) return new ToolError("This account has no home fiber service.");

            List<string> actions = ["Re-provisioned the optical network terminal (ONT)"];
            if (fiber.RouterFirmware != LatestFirmware)
            {
                fiber.RouterFirmware = LatestFirmware;
                actions.Add($"Updated router firmware to {LatestFirmware}");
            }

            ctx.Record("fiber_reset", string.Join("; ", actions));
            bool physical = fiber.OpticalSignalDbm < MinHealthySignalDbm;
            if (!physical) fiber.DropsLast24h = 0;
            return new ResetResult("fiber", "home fiber", !physical, actions,
                physical
                    ? $"Optical signal is still {fiber.OpticalSignalDbm} dBm, which points to a physical problem (damaged cable or dirty connector). A technician visit is needed."
                    : "Connection is stable after the reset.");
        }

        Line? line = ctx.ResolveLine(c, lineNumber);
        if (line is null) return new ToolError($"No line matching '{lineNumber}' on this account.");
        if (line.Status == LineStatus.Suspended) return new ToolError($"Line {line.Number} is suspended ({line.SuspendReason}); a reset cannot restore service.");

        ctx.Record("line_refresh", line.Number);
        return new ResetResult("mobile", line.Number, true, ["Refreshed network provisioning", "Sent updated carrier settings"],
            "Line re-registered successfully. Ask the customer to restart the phone.");
    }

    [Description("List available technician appointment windows for the customer's address.")]
    public object GetTechnicianSlots()
    {
        if (!ctx.TryGetCustomer(out _, out ToolError err)) return err;
        return Catalog.TechnicianSlots;
    }

    [Description("Book a technician visit. Requires approval because it dispatches a field technician.")]
    public object ScheduleTechnician(
        [Description("Slot id from get_technician_slots, e.g. S1")] string slotId,
        [Description("One-line summary of the problem for the technician")] string issueSummary)
    {
        if (!ctx.TryGetCustomer(out Customer c, out ToolError err)) return err;

        TechnicianSlot? slot = Catalog.TechnicianSlots.FirstOrDefault(s => s.Id.Equals(slotId, StringComparison.OrdinalIgnoreCase));
        if (slot is null) return new ToolError($"Unknown slot '{slotId}'. Call get_technician_slots first.");

        var appointment = new Appointment($"APT-{58210 + c.Appointments.Count + 1}", slot.Label, issueSummary);
        c.Appointments.Add(appointment);
        ctx.Record("technician_booked", $"{appointment.Id} {slot.Label}");
        return new AppointmentResult(true, appointment.Id, slot.Label, "Technician booked. The customer will get a text 30 minutes before arrival.");
    }

    private static bool IsFiber(string service) =>
        service.Contains("fiber", StringComparison.OrdinalIgnoreCase) ||
        service.Contains("internet", StringComparison.OrdinalIgnoreCase) ||
        service.Contains("home", StringComparison.OrdinalIgnoreCase);
}

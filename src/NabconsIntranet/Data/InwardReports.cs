using System.Globalization;
using System.Security.Claims;
using NabconsIntranet.Infrastructure;
using NabconsIntranet.Infrastructure.Reports;

namespace NabconsIntranet.Data;

/// <summary>Printed and exported outputs of the inward register (DD-08 §7), built as <see cref="ReportDocument"/>s. Masking per E8.</summary>
public sealed class InwardReports(IntranetOptions io, Fmt fmt)
{
    public const int MaxPdfDays = 31, MaxPdfRows = 5000;

    private DateTime Now => DateTime.SpecifyKind(new DateTime(DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond), DateTimeKind.Utc);
    private string Stamp => fmt.LocalNow.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
    private static string Rs(long? p) => p.HasValue ? Paise.Format(p).Replace("₹ ", "") : "";

    /// <summary>Acknowledgement slip (§7.1): A4 portrait, two columns. The subject is masked for a confidential item; the sender is shown (R2-14).</summary>
    public ReportDocument Acknowledgement(InwardItem it, ClaimsPrincipal u) => new()
    {
        OrganisationLine = io.OrganisationName,
        Title = "Acknowledgement of receipt",
        Subtitles = { it.OfficeName },
        Portrait = true,
        Sections =
        {
            new ReportSection
            {
                Columns = { new ReportColumn("Particulars", 30), new ReportColumn("Details", 70) },
                Rows =
                {
                    new(new[] { "Inward number", it.Number }, true),
                    new(new[] { "Received on", fmt.DateTime(it.ReceivedAt) }),
                    new(new[] { "Office", it.OfficeName }),
                    new(new[] { "From", it.SenderName + (string.IsNullOrEmpty(it.SenderOrg) ? "" : ", " + it.SenderOrg) }),
                    new(new[] { "Sender's reference", (it.SenderRef ?? "—") + (it.SenderRefDate == null ? "" : " dated " + fmt.DateDot(it.SenderRefDate)) }),
                    new(new[] { "Subject", it.IsConfidential ? "Confidential correspondence" : it.Subject }),
                    new(new[] { "Type / mode", InwardType.Label(it.ItemType) + " · " + InwardMode.Label(it.Mode) }),
                    new(new[] { "Pages", it.Pages?.ToString(CultureInfo.InvariantCulture) ?? "—" }),
                    new(new[] { "Received by", it.InwardedByName }),
                },
            },
        },
        Footer = $"System-generated acknowledgement — no signature required · {it.Number}",
        GeneratedAt = Now,
        FileBase = "Acknowledgement-" + it.Number.Replace('/', '-'),
    };

    private static readonly ReportColumn[] RegisterCols =
    {
        new("Inward No", 15), new("Received", 9), new("Type / Mode", 10), new("From", 16), new("Sender ref & date", 12), new("Subject", 22),
        new("Marked to", 10), new("Status / reply due", 10),
    };

    /// <summary>Inward register (§7.2), PDF landscape or XLSX.</summary>
    public ReportDocument Register(List<InwardItem> rows, string title, string range, bool forPdf) => new()
    {
        OrganisationLine = io.OrganisationName,
        Title = title,
        Subtitles = { range, $"{rows.Count:N0} item(s) · printed {Stamp}" },
        Sections =
        {
            new ReportSection
            {
                SheetName = "Inward register",
                Columns = RegisterCols.ToList(),
                Rows = rows.Select(i => new ReportRow(new[]
                {
                    i.Number, fmt.DateTime(i.ReceivedAt), InwardType.Label(i.ItemType) + " / " + InwardMode.Label(i.Mode), i.ShownSender,
                    i.Masked ? "" : (i.SenderRef ?? "") + (i.SenderRefDate == null ? "" : " · " + fmt.DateDot(i.SenderRefDate)),
                    i.ShownSubject, i.HolderName ?? (i.Status == ItemStatus.WithAccounts ? "Accounts" : ""),
                    (i.InvoiceStatus != null ? InvStatus.Label(i.InvoiceStatus) : ItemStatus.Label(i.Status)) + (i.IsOpen && i.ReplyDueOn != null ? " · due " + fmt.DateDot(i.ReplyDueOn) : ""),
                })).ToList(),
            },
        },
        Footer = $"{io.OrganisationName} · Inward register · confidential items show metadata only",
        GeneratedAt = Now,
        FileBase = "Inward-register-" + fmt.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
    };

    /// <summary>Invoice list (§7.3), XLSX.</summary>
    public ReportDocument InvoiceList(List<InwardRepository.InvoiceRow> rows, string filter)
    {
        var today = fmt.Today;
        string Days(string d) => (today.DayNumber - DateOnly.ParseExact(d, "yyyy-MM-dd", CultureInfo.InvariantCulture).DayNumber).ToString(CultureInfo.InvariantCulture);
        return new ReportDocument
        {
            OrganisationLine = io.OrganisationName,
            Title = "Invoices",
            Subtitles = { filter, $"{rows.Count:N0} invoice(s) · {Stamp}" },
            Sections =
            {
                new ReportSection
                {
                    SheetName = "Invoices",
                    Columns = new[] { "Inward No", "Vendor", "GSTIN", "MSME", "Class", "Invoice no.", "Invoice date", "Project", "PO", "Total", "Adjustments", "Settled",
                        "Outstanding", "Status", "Holder", "Certified on", "Payment due", "Days since invoice", "MSME past 45 days" }.Select(h => new ReportColumn(h, 6)).ToList(),
                    Rows = rows.Select(r =>
                    {
                        var masked = r.IsConfidential && !r.Full;
                        var open = !InvStatus.IsFinal(r.InvoiceStatus);
                        return new ReportRow(new[]
                        {
                            r.Number, masked ? "Confidential" : r.VendorName, masked || !r.Full ? "" : r.VendorGstin ?? "", r.IsMsme ? "Yes" : "No", r.MsmeClass ?? "",
                            r.InvoiceNo, fmt.DateDot(r.InvoiceDate), r.ProjectCode ?? "", masked ? "" : r.PoRef ?? "", Rs(r.TotalPaise), Rs(r.AdjustPaise), Rs(r.SettledPaise),
                            Rs(r.TotalPaise + r.AdjustPaise - r.SettledPaise), InvStatus.Label(r.InvoiceStatus),
                            r.Status == ItemStatus.WithAccounts ? "Accounts" : r.HolderName ?? "", fmt.DateDot(r.CertifiedOn), fmt.DateDot(r.PaymentDueOn), Days(r.InvoiceDate),
                            open && r.IsMsme && today.DayNumber - DateOnly.ParseExact(r.InvoiceDate, "yyyy-MM-dd", CultureInfo.InvariantCulture).DayNumber > 45 ? "Yes" : "",
                        });
                    }).ToList(),
                },
            },
            Footer = $"{io.OrganisationName} · invoice register",
            GeneratedAt = Now,
            FileBase = "Invoices-" + today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        };
    }

    /// <summary>MSME / 43B(h) report (§7.4): Micro/Small section and "class not recorded".</summary>
    public ReportDocument Msme(List<MsmeRow> rows, DateOnly asOn, DateOnly? enteredBy)
    {
        var cols = new[] { "Inward No", "Vendor", "GSTIN", "Udyam", "Class", "Invoice no.", "Invoice date", "Due date (MSME)", "Total (effective)",
            "Paid by the as-on date", "Outstanding on the as-on date", "Paid late on" }.Select(h => new ReportColumn(h, 8)).ToList();
        ReportRow Row(MsmeRow r)
        {
            var masked = r.IsConfidential && !r.Full;
            var late = r.LastPaidOn != null && string.CompareOrdinal(r.LastPaidOn, r.DueOn) > 0 ? fmt.DateDot(r.LastPaidOn) : "";
            return new ReportRow(new[]
            {
                r.Number, masked ? "Confidential" : r.VendorName, masked ? "" : r.VendorGstin ?? "", masked ? "" : r.UdyamNo ?? "", r.MsmeClass ?? "",
                r.InvoiceNo, fmt.DateDot(r.InvoiceDate), fmt.DateDot(r.DueOn), Rs(r.EffectivePaise), Rs(r.PaidByCutoff), Rs(r.OutstandingAtCutoff), late,
            });
        }
        var classified = rows.Where(r => r.MsmeClass != null).ToList();
        var unknown = rows.Where(r => r.MsmeClass == null).ToList();
        return new ReportDocument
        {
            OrganisationLine = io.OrganisationName,
            Title = "MSME dues — section 43B(h) / MSME Form 1",
            Subtitles =
            {
                $"As on {asOn:dd.MM.yyyy}" + (enteredBy is DateOnly e ? $" · entries up to {e:dd.MM.yyyy}" : ""),
                $"{classified.Count:N0} Micro/Small invoice(s), outstanding {Paise.Format(classified.Sum(r => Math.Max(0, r.OutstandingAtCutoff)))}; {unknown.Count:N0} with class not recorded",
            },
            Sections =
            {
                new ReportSection { Heading = "Micro and Small enterprises", SheetName = "Micro & Small", Columns = cols, Rows = classified.Select(Row).ToList() },
                new ReportSection { Heading = "MSME — class not recorded", SheetName = "Class not recorded", Columns = cols, Rows = unknown.Select(Row).ToList() },
            },
            Footer = $"{io.OrganisationName} · MSME dues as on {asOn:dd.MM.yyyy} · traders and Medium enterprises excluded · prepared {Stamp}",
            GeneratedAt = Now,
            FileBase = "MSME-43Bh-" + asOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        };
    }
}

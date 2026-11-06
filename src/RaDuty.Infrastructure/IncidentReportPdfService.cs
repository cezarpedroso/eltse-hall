using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using RaDuty.Application;

namespace RaDuty.Infrastructure;

public sealed class IncidentReportPdfService : IIncidentReportPdfService
{
    private const string Ink = "#1C2923";
    private const string Muted = "#647069";
    private const string Primary = "#1F5A43";
    private const string Line = "#D9DDD9";

    public IncidentReportPdfService() => QuestPDF.Settings.License = LicenseType.Community;

    public byte[] Render(IncidentReportDto report, string residenceHallName, DateTimeOffset generatedAt) => Document.Create(document =>
    {
        document.Page(page =>
        {
            page.Size(PageSizes.Letter);
            page.Margin(36);
            page.DefaultTextStyle(x => x.FontFamily(Fonts.Arial).FontSize(9).FontColor(Ink));
            page.Header().PaddingBottom(9).BorderBottom(1).BorderColor(Line).Row(row =>
            {
                row.RelativeItem().Column(column =>
                {
                    column.Item().Text(residenceHallName.ToUpperInvariant()).SemiBold().FontSize(8).FontColor(Primary);
                    column.Item().Text("Incident report").Bold().FontSize(17).FontColor(Ink);
                });
                row.ConstantItem(150).AlignRight().Text($"Generated {generatedAt:MMM d, yyyy 'at' h:mm tt} UTC").FontSize(7).FontColor(Muted);
            });
            page.Content().PaddingVertical(18).Column(column =>
            {
                column.Spacing(14);
                column.Item().Element(container => Overview(container, report));
                column.Item().Element(container => Details(container, report));
                column.Item().Element(container => Students(container, report));
                column.Item().Element(container => Narrative(container, report));
            });
            page.Footer().BorderTop(1).BorderColor(Line).PaddingTop(7).Row(row =>
            {
                row.RelativeItem().Text("Restricted residence-life information").FontSize(7).FontColor(Muted);
                row.ConstantItem(65).AlignRight().Text(text => { text.Span("Page "); text.CurrentPageNumber(); text.Span(" of "); text.TotalPages(); });
            });
        });
    }).GeneratePdf();

    private static void Overview(IContainer container, IncidentReportDto report) => container.Border(1).BorderColor(Line).Padding(12).Column(column =>
    {
        column.Spacing(7);
        column.Item().Text(IncidentLabel(report.IncidentType.ToString())).SemiBold().FontSize(12).FontColor(Primary);
        column.Item().Table(table =>
        {
            table.ColumnsDefinition(columns => { columns.RelativeColumn(); columns.RelativeColumn(); });
            Entry(table.Cell(), "Occurred", report.OccurredAt.ToString("MMM d, yyyy 'at' h:mm tt UTC"));
            Entry(table.Cell(), "Location", report.Location);
            Entry(table.Cell(), "Reported by", report.ReportedByName);
            Entry(table.Cell(), "Infraction price", report.InfractionPrice is null ? "Not entered" : $"${report.InfractionPrice:0.00}");
            if (report.IncidentType == RaDuty.Domain.IncidentType.Alcohol)
                Entry(table.Cell(), "Alcohol found", report.AlcoholOunces is null ? "Not entered" : $"{report.AlcoholOunces:0.##} oz");
        });
    });

    private static void Details(IContainer container, IncidentReportDto report) => container.Column(column =>
    {
        column.Spacing(5);
        column.Item().Text("Staff present").SemiBold().FontSize(10);
        column.Item().Text(report.StaffPresent.Count == 0 ? $"Reporter only: {report.ReportedByName}" :
            $"Reporter: {report.ReportedByName}\nAlso present: {string.Join(", ", report.StaffPresent.Select(staff => staff.Name))}").FontColor(Muted);
    });

    private static void Students(IContainer container, IncidentReportDto report) => container.Column(column =>
    {
        column.Spacing(6);
        column.Item().Text("Students involved").SemiBold().FontSize(10);
        column.Item().Table(table =>
        {
            table.ColumnsDefinition(columns => { columns.RelativeColumn(2); columns.RelativeColumn(2); columns.RelativeColumn(); });
            Header(table.Cell(), "Name"); Header(table.Cell(), "Student ID"); Header(table.Cell(), "Room");
            foreach (var student in report.Students)
            {
                Cell(table.Cell(), $"{student.FirstName} {student.LastName}");
                Cell(table.Cell(), student.StudentId);
                Cell(table.Cell(), $"{student.RoomNumber}{student.RoomLetter}");
            }
        });
    });

    private static void Narrative(IContainer container, IncidentReportDto report) => container.Column(column =>
    {
        column.Spacing(6);
        column.Item().Text("Description").SemiBold().FontSize(10);
        column.Item().Background("#F7F9F7").Padding(10).Text(report.Description).FontColor(Ink);
    });

    private static void Entry(IContainer container, string label, string value) => container.PaddingVertical(3).PaddingRight(12).Column(column =>
    {
        column.Item().Text(label.ToUpperInvariant()).SemiBold().FontSize(7).FontColor(Muted);
        column.Item().Text(value).FontSize(9).FontColor(Ink);
    });
    private static void Header(IContainer container, string text) => container.Background("#F0F3F0").Padding(6).Text(text).SemiBold().FontSize(8).FontColor(Muted);
    private static void Cell(IContainer container, string text) => container.BorderBottom(1).BorderColor(Line).Padding(6).Text(text).FontSize(8);
    private static string IncidentLabel(string value) => System.Text.RegularExpressions.Regex.Replace(value, "([a-z])([A-Z])", "$1 $2");
}

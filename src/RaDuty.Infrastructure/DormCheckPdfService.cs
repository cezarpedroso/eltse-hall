using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using RaDuty.Application;

namespace RaDuty.Infrastructure;

public sealed class DormCheckPdfService : IDormCheckPdfService
{
    private const string Ink = "#1C2923";
    private const string Muted = "#647069";
    private const string Primary = "#1F5A43";
    private const string Line = "#D9DDD9";

    public DormCheckPdfService() => QuestPDF.Settings.License = LicenseType.Community;

    public byte[] Render(DormCheckReportDto report, DateTimeOffset generatedAt)
    {
        var roomCount = report.Suites.Sum(x => x.Rooms.Count);
        var checkedCount = report.Suites.Sum(x => x.Rooms.Count(room => room.LatestCheck is not null));
        return Document.Create(document =>
        {
            document.Page(page =>
            {
                ConfigurePage(page, generatedAt, report.ResidenceHallName, "Room check report");
                page.Content().PaddingVertical(14).Column(column =>
                {
                    column.Spacing(10);
                    column.Item().Element(container => Summary(container,
                        "Latest submitted checklist for every suite and room.",
                        report.Suites.Count.ToString(), "Suites",
                        roomCount.ToString(), "Rooms",
                        checkedCount.ToString(), "Completed",
                        "Unchecked rooms are included. N/A is retained for common-area responses."));
                    foreach (var suite in report.Suites)
                    {
                        column.Item().Element(container => SuiteHeader(container, suite.SuiteNumber,
                            $"{suite.Rooms.Count(x => x.LatestCheck is not null)}/4 checked"));
                        foreach (var room in suite.Rooms)
                            column.Item().Element(container => RoomCard(container, room));
                    }
                });
            });
        }).GeneratePdf();
    }

    private static void ConfigurePage(PageDescriptor page, DateTimeOffset generatedAt, string residenceHallName, string title)
    {
        page.Size(PageSizes.Letter);
        page.Margin(32);
        page.DefaultTextStyle(x => x.FontFamily(Fonts.Arial).FontSize(8).FontColor(Ink));
        page.Header().PaddingBottom(8).BorderBottom(1).BorderColor(Line).Row(row =>
        {
            row.RelativeItem().Column(column =>
            {
                column.Item().Text(residenceHallName.ToUpperInvariant()).SemiBold().FontSize(8).FontColor(Primary);
                column.Item().Text(title).Bold().FontSize(14).FontColor(Ink);
            });
            row.ConstantItem(130).AlignRight().Text($"Generated {generatedAt:MMM d, yyyy}").FontSize(7).FontColor(Muted);
        });
        page.Footer().BorderTop(1).BorderColor(Line).PaddingTop(7).Row(row =>
        {
            row.RelativeItem().Text($"Generated {generatedAt:MMM d, yyyy 'at' h:mm tt} UTC - Restricted residence-life information").FontSize(7).FontColor(Muted);
            row.ConstantItem(65).AlignRight().Text(text => { text.Span("Page "); text.CurrentPageNumber(); text.Span(" of "); text.TotalPages(); });
        });
    }

    private static void Summary(IContainer container, string description, string firstValue, string firstLabel,
        string secondValue, string secondLabel, string thirdValue, string thirdLabel, string note) => container
        .Border(1).BorderColor(Line).Background("#FBFCFB").Padding(10).Column(column =>
        {
            column.Spacing(8);
            column.Item().Text(description).FontSize(9).FontColor(Muted);
            column.Item().Row(row =>
            {
                Metric(row.RelativeItem(), firstValue, firstLabel);
                row.Spacing(8);
                Metric(row.RelativeItem(), secondValue, secondLabel);
                row.Spacing(8);
                Metric(row.RelativeItem(), thirdValue, thirdLabel);
            });
            column.Item().Text(note).FontSize(7).FontColor(Muted);
        });

    private static void Metric(IContainer container, string value, string label) => container
        .Border(1).BorderColor(Line).Background("#F7F9F7").Padding(8).Column(column =>
        {
            column.Item().Text(value).Bold().FontSize(15).FontColor(Primary);
            column.Item().Text(label.ToUpperInvariant()).SemiBold().FontSize(7).FontColor(Muted);
        });

    private static void SuiteHeader(IContainer container, string suiteNumber, string status) => container
        .PaddingTop(3).BorderBottom(1).BorderColor(Line).PaddingBottom(5).Row(row =>
        {
            row.RelativeItem().Text($"Suite {suiteNumber}").Bold().FontSize(12).FontColor(Ink);
            row.ConstantItem(90).AlignRight().Text(status).SemiBold().FontSize(8).FontColor(Muted);
        });

    private static void RoomCard(IContainer container, DormRoomReportDto room)
    {
        container.Border(1).BorderColor(Line).Padding(7).Column(column =>
        {
            column.Spacing(4);
            column.Item().Row(row =>
            {
                row.ConstantItem(54).Text($"Room {room.RoomLetter}").Bold().FontSize(10).FontColor(Ink);
                row.RelativeItem().Text(room.Residents.Count == 0 ? "Vacant" : string.Join("  |  ", room.Residents.Select(x => $"{x.FirstName} {x.LastName}"))).FontSize(8).FontColor(Muted);
                row.ConstantItem(145).AlignRight().Text(room.LatestCheck is null ? "NOT CHECKED" : $"Checked by {room.LatestCheck.CheckedByName}")
                    .SemiBold().FontSize(7).FontColor(room.LatestCheck is null ? "#9A3F37" : Primary);
            });

            if (room.LatestCheck is null)
            {
                column.Item().PaddingVertical(3).Text("No form response has been submitted for this room.").Italic().FontColor(Muted);
                return;
            }

            var check = room.LatestCheck;
            column.Item().Text($"Completed {check.CheckedAt:MMM d, yyyy 'at' h:mm tt} UTC - {check.PhotoCount} photo(s) attached").FontSize(7).FontColor(Muted);
            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns => { columns.RelativeColumn(); columns.RelativeColumn(); });
                Response(table.Cell(), "Is the room clean?", YesNo(check.IsRoomClean));
                Response(table.Cell(), "Is all furniture present?", YesNo(check.IsAllFurniturePresent));
                Response(table.Cell(), "Is the smoke detector clear?", YesNo(check.IsSmokeDetectorClear));
                Response(table.Cell(), "Is the room odor-free?", YesNo(check.IsRoomOdorFree));
                Response(table.Cell(), "Is the room trash-free?", YesNo(check.IsRoomTrashFree));
                Response(table.Cell(), "Is the common area clean?", YesNoNa(check.IsCommonAreaClean));
                Response(table.Cell(), "Is the room alcohol-free?", YesNo(check.IsRoomAlcoholFree));
                Response(table.Cell(), "Is the room free of damage?", YesNo(check.IsRoomDamageFree));
            });
            column.Item().Background("#F7F9F7").Padding(6).Text(text =>
            {
                text.Span("Notes: ").SemiBold();
                text.Span(string.IsNullOrWhiteSpace(check.Notes) ? "None" : check.Notes);
            });
        });
    }

    private static void Response(IContainer container, string label, string answer) => container.PaddingVertical(2).PaddingRight(8).Row(row =>
    {
        row.RelativeItem().Text(label).FontSize(7).FontColor(Muted);
        row.ConstantItem(27).AlignRight().Text(answer).Bold().FontSize(7).FontColor(answer == "Yes" ? Primary : answer == "N/A" ? Muted : "#9A3F37");
    });

    private static string YesNo(bool value) => value ? "Yes" : "No";
    private static string YesNoNa(bool? value) => value.HasValue ? YesNo(value.Value) : "N/A";
}

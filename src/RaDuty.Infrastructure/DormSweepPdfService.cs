using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using RaDuty.Application;

namespace RaDuty.Infrastructure;

public sealed class DormSweepPdfService : IDormSweepPdfService
{
    private const string Ink = "#1C2923";
    private const string Muted = "#647069";
    private const string Primary = "#1F5A43";
    private const string Danger = "#9A3F37";
    private const string Line = "#D9DDD9";

    public DormSweepPdfService() => QuestPDF.Settings.License = LicenseType.Community;

    public byte[] Render(DormSweepReportDto report, DateTimeOffset generatedAt)
    {
        var completed = report.Suites.Count(x => x.LatestSweep is not null);
        var concerns = report.Suites.Count(x => x.LatestSweep?.HasConcerns == true);
        return Document.Create(document =>
        {
            document.Page(page =>
            {
                ConfigurePage(page, generatedAt, report.ResidenceHallName, "Dorm sweep report");
                page.Content().PaddingVertical(14).Column(column =>
                {
                    column.Spacing(10);
                    column.Item().Element(container => Summary(container,
                        "Latest suite common-area and bathroom sweep for every Eltse Hall suite.",
                        report.Suites.Count.ToString(), "Suites",
                        completed.ToString(), "Completed",
                        concerns.ToString(), "Need attention",
                        "Each suite lists its current residents and the latest submitted sweep response."));
                    foreach (var suite in report.Suites)
                    {
                        column.Item().Element(container => SuiteSection(container, suite));
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

    private static void SuiteSection(IContainer container, DormSweepSuiteReportDto suite)
    {
        container.Border(1).BorderColor(Line).Padding(8).Column(column =>
        {
            column.Spacing(7);
            column.Item().Row(row =>
            {
                row.RelativeItem().Text($"Suite {suite.SuiteNumber}").Bold().FontSize(12).FontColor(Ink);
                row.ConstantItem(110).AlignRight().Text(Status(suite.LatestSweep))
                    .SemiBold().FontSize(8).FontColor(suite.LatestSweep is null || suite.LatestSweep.HasConcerns ? Danger : Primary);
            });
            column.Item().Element(container => Residents(container, suite));
            column.Item().Element(container => SweepCard(container, suite.LatestSweep));
        });
    }

    private static void Residents(IContainer container, DormSweepSuiteReportDto suite)
    {
        container.Background("#F7F9F7").Padding(6).Column(column =>
        {
            column.Spacing(4);
            column.Item().Text("Residents").SemiBold().FontSize(8).FontColor(Ink);
            if (suite.Residents.Count == 0)
            {
                column.Item().Text("No residents listed for this suite.").Italic().FontColor(Muted);
                return;
            }

            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(70);
                    columns.RelativeColumn();
                });
                foreach (var resident in suite.Residents)
                {
                    table.Cell().PaddingVertical(2).Text(resident.RoomCode).SemiBold().FontSize(7).FontColor(Muted);
                    table.Cell().PaddingVertical(2).Text($"{resident.FirstName} {resident.LastName}").FontSize(8).FontColor(Ink);
                }
            });
        });
    }

    private static void SweepCard(IContainer container, DormSuiteSweepDto? sweep)
    {
        container.PaddingTop(2).Column(column =>
        {
            column.Spacing(5);
            column.Item().Text("Sweep response").Bold().FontSize(9).FontColor(Ink);
            if (sweep is null)
            {
                column.Item().Text("No suite sweep has been submitted.").Italic().FontColor(Muted);
                return;
            }

            column.Item().Text($"Completed {sweep.CheckedAt:MMM d, yyyy 'at' h:mm tt} UTC by {sweep.CheckedByName}")
                .FontSize(7).FontColor(Muted);
            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns => { columns.RelativeColumn(); columns.RelativeColumn(); });
                Response(table.Cell(), "Is the common area clean?", YesNo(sweep.IsCommonAreaClean), !sweep.IsCommonAreaClean);
                Response(table.Cell(), "Any mold or leaking?", YesNo(sweep.HasMoldOrLeak), sweep.HasMoldOrLeak);
                Response(table.Cell(), "Furniture moved to common area?", YesNo(sweep.HasFurnitureMovedToCommonArea), sweep.HasFurnitureMovedToCommonArea);
                Response(table.Cell(), "Is the bathroom clean?", YesNo(sweep.IsBathroomClean), !sweep.IsBathroomClean);
                Response(table.Cell(), "Toilets and sinks working?", YesNo(sweep.AreToiletsAndSinksWorking), !sweep.AreToiletsAndSinksWorking);
                Response(table.Cell(), "Showers working?", YesNo(sweep.AreShowersWorking), !sweep.AreShowersWorking);
                Response(table.Cell(), "Smells like marijuana or alcohol?", YesNo(sweep.SmellsLikeMarijuanaOrAlcohol), sweep.SmellsLikeMarijuanaOrAlcohol);
            });
            column.Item().Background("#F7F9F7").Padding(6).Text(text =>
            {
                text.Span("Notes: ").SemiBold();
                text.Span(string.IsNullOrWhiteSpace(sweep.Notes) ? "None" : sweep.Notes);
            });
        });
    }

    private static void Response(IContainer container, string label, string answer, bool concern) => container.PaddingVertical(2).PaddingRight(8).Row(row =>
    {
        row.RelativeItem().Text(label).FontSize(7).FontColor(Muted);
        row.ConstantItem(27).AlignRight().Text(answer).Bold().FontSize(7).FontColor(concern ? Danger : Primary);
    });

    private static string Status(DormSuiteSweepDto? sweep) =>
        sweep is null ? "NOT SWEPT" : sweep.HasConcerns ? "NEEDS ATTENTION" : "CLEAR";

    private static string YesNo(bool value) => value ? "Yes" : "No";
}

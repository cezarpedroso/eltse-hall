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
                ConfigurePage(page, generatedAt);
                page.Content().PaddingVertical(28).Column(column =>
                {
                    column.Spacing(18);
                    column.Item().Text(report.ResidenceHallName.ToUpperInvariant()).SemiBold().FontSize(11).FontColor(Primary);
                    column.Item().Text("Dorm sweep report").Bold().FontSize(28).FontColor(Ink);
                    column.Item().Text("Latest suite common-area and bathroom sweep for every Eltse Hall suite.").FontSize(11).FontColor(Muted);
                    column.Item().PaddingTop(8).Row(row =>
                    {
                        Metric(row.RelativeItem(), report.Suites.Count.ToString(), "Suites");
                        row.Spacing(10);
                        Metric(row.RelativeItem(), completed.ToString(), "Completed");
                        row.Spacing(10);
                        Metric(row.RelativeItem(), concerns.ToString(), "Need attention");
                    });
                    column.Item().PaddingTop(12).BorderTop(1).BorderColor(Line).PaddingTop(14)
                        .Text("Each suite lists its current residents and the latest submitted sweep response.")
                        .FontSize(9).FontColor(Muted);
                });
            });

            foreach (var suite in report.Suites)
            {
                document.Page(page =>
                {
                    ConfigurePage(page, generatedAt);
                    page.Header().PaddingBottom(10).BorderBottom(1).BorderColor(Line).Row(row =>
                    {
                        row.RelativeItem().Column(left =>
                        {
                            left.Item().Text(report.ResidenceHallName.ToUpperInvariant()).SemiBold().FontSize(8).FontColor(Primary);
                            left.Item().Text($"Suite {suite.SuiteNumber}").Bold().FontSize(20).FontColor(Ink);
                        });
                        row.ConstantItem(110).AlignRight().Text(suite.LatestSweep is null
                                ? "NOT SWEPT"
                                : suite.LatestSweep.HasConcerns ? "NEEDS ATTENTION" : "CLEAR")
                            .SemiBold().FontSize(8).FontColor(suite.LatestSweep is null || suite.LatestSweep.HasConcerns ? Danger : Primary);
                    });
                    page.Content().PaddingVertical(12).Column(column =>
                    {
                        column.Spacing(12);
                        column.Item().Element(container => Residents(container, suite));
                        column.Item().Element(container => SweepCard(container, suite.LatestSweep));
                    });
                });
            }
        }).GeneratePdf();
    }

    private static void ConfigurePage(PageDescriptor page, DateTimeOffset generatedAt)
    {
        page.Size(PageSizes.Letter);
        page.Margin(32);
        page.DefaultTextStyle(x => x.FontFamily(Fonts.Arial).FontSize(8).FontColor(Ink));
        page.Footer().BorderTop(1).BorderColor(Line).PaddingTop(7).Row(row =>
        {
            row.RelativeItem().Text($"Generated {generatedAt:MMM d, yyyy 'at' h:mm tt} UTC - Restricted residence-life information").FontSize(7).FontColor(Muted);
            row.ConstantItem(65).AlignRight().Text(text => { text.Span("Page "); text.CurrentPageNumber(); text.Span(" of "); text.TotalPages(); });
        });
    }

    private static void Metric(IContainer container, string value, string label) => container
        .Border(1).BorderColor(Line).Background("#F7F9F7").Padding(14).Column(column =>
        {
            column.Item().Text(value).Bold().FontSize(22).FontColor(Primary);
            column.Item().Text(label.ToUpperInvariant()).SemiBold().FontSize(7).FontColor(Muted);
        });

    private static void Residents(IContainer container, DormSweepSuiteReportDto suite)
    {
        container.Border(1).BorderColor(Line).Padding(10).Column(column =>
        {
            column.Spacing(6);
            column.Item().Text("Residents").Bold().FontSize(11).FontColor(Ink);
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
        container.Border(1).BorderColor(Line).Padding(10).Column(column =>
        {
            column.Spacing(7);
            column.Item().Text("Sweep response").Bold().FontSize(11).FontColor(Ink);
            if (sweep is null)
            {
                column.Item().PaddingTop(8).PaddingBottom(8).Text("No suite sweep has been submitted.").Italic().FontColor(Muted);
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

    private static string YesNo(bool value) => value ? "Yes" : "No";
}

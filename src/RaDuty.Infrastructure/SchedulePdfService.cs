using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using RaDuty.Application;

namespace RaDuty.Infrastructure;

public sealed class SchedulePdfService : ISchedulePdfService
{
    public SchedulePdfService() => QuestPDF.Settings.License = LicenseType.Community;

    public byte[] Render(ScheduleDto schedule, DateTimeOffset generatedAt)
    {
        var monthName = CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(schedule.Month);
        return Document.Create(document => document.Page(page =>
        {
            page.Size(PageSizes.Letter);
            page.Margin(36);
            page.DefaultTextStyle(x => x.FontFamily(Fonts.Arial).FontSize(9).FontColor("#202A25"));
            page.Header().Column(header =>
            {
                header.Item().Row(row =>
                {
                    row.RelativeItem().Column(left =>
                    {
                        left.Item().Text(schedule.ResidenceHallName.ToUpperInvariant()).SemiBold().FontSize(10).FontColor("#365A49");
                        left.Item().Text($"{monthName} {schedule.Year} night-duty schedule").Bold().FontSize(20);
                    });
                    row.ConstantItem(110).AlignRight().Column(right =>
                    {
                        right.Item().Text(schedule.Status.ToString()).SemiBold();
                        right.Item().Text(schedule.PublishedAt is null ? "Preview" : $"Published {schedule.PublishedAt:MMM d, yyyy}").FontSize(8).FontColor("#56635D");
                    });
                });
                header.Item().PaddingTop(10).BorderBottom(1).BorderColor("#9AA69F");
            });
            page.Content().PaddingVertical(16).Column(column =>
            {
                column.Spacing(5);
                column.Item().Row(row =>
                {
                    row.ConstantItem(86).Text("DUTY DATE").SemiBold().FontSize(8).FontColor("#56635D");
                    row.ConstantItem(115).Text("HOURS").SemiBold().FontSize(8).FontColor("#56635D");
                    row.RelativeItem().Text("ASSIGNED RESIDENT ASSISTANT(S)").SemiBold().FontSize(8).FontColor("#56635D");
                });
                foreach (var shift in schedule.Shifts)
                {
                    column.Item().BorderTop(0.5f).BorderColor("#D8DEDA").PaddingVertical(6).Row(row =>
                    {
                        row.ConstantItem(86).Column(date =>
                        {
                            date.Item().Text(shift.DutyDate.ToString("ddd, MMM d", CultureInfo.InvariantCulture)).SemiBold();
                        });
                        row.ConstantItem(115).Text($"{LocalTime(shift.StartsAt, schedule.TimeZone):h:mm tt}–{LocalTime(shift.EndsAt, schedule.TimeZone):h:mm tt}");
                        row.RelativeItem().Column(people =>
                        {
                            if (shift.Assignments.Count == 0)
                                people.Item().Text("UNFILLED").Bold().FontColor("#8B2B2B");
                            else
                                foreach (var assignment in shift.Assignments)
                                    people.Item().Text($"{assignment.FirstName} {assignment.LastName}{(string.IsNullOrWhiteSpace(assignment.RoomNumber) ? "" : $"  ·  Room {assignment.RoomNumber}")}");
                            if (shift.Assignments.Count < shift.RequiredStaffCount)
                                people.Item().Text($"{shift.RequiredStaffCount - shift.Assignments.Count} opening(s) remaining").FontSize(8).Italic().FontColor("#8B2B2B");
                        });
                    });
                }
            });
            page.Footer().BorderTop(1).BorderColor("#9AA69F").PaddingTop(8).Row(row =>
            {
                row.RelativeItem().Text($"Generated {generatedAt:MMM d, yyyy 'at' h:mm tt} UTC · Restricted residence-life directory information").FontSize(7).FontColor("#56635D");
                row.ConstantItem(70).AlignRight().Text(x => { x.Span("Page "); x.CurrentPageNumber(); x.Span(" of "); x.TotalPages(); });
            });
        })).GeneratePdf();
    }

    private static DateTimeOffset LocalTime(DateTimeOffset utc, string timeZone) =>
        TimeZoneInfo.ConvertTime(utc, TimeZoneInfo.FindSystemTimeZoneById(timeZone));
}

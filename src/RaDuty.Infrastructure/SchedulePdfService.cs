using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using RaDuty.Application;
using RaDuty.Domain;

namespace RaDuty.Infrastructure;

public sealed class SchedulePdfService : ISchedulePdfService
{
    private const string Ink = "#18251F";
    private const string Muted = "#5F6E66";
    private const string Primary = "#17603E";
    private const string PrimarySoft = "#E8F3ED";
    private const string Line = "#D7DED9";
    private const string Weekend = "#F6F8F6";
    private const string Danger = "#9A342D";

    public SchedulePdfService() => QuestPDF.Settings.License = LicenseType.Community;

    public byte[] Render(ScheduleDto schedule, IReadOnlyList<ResidentAssistantDto> staffDirectory,
        DateTimeOffset generatedAt)
    {
        var monthName = CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(schedule.Month);
        return Document.Create(document =>
        {
            document.Page(page => ComposeCalendarPage(page, schedule, monthName, generatedAt));
            document.Page(page => ComposeDirectoryPage(page, schedule, staffDirectory, monthName, generatedAt));
        }).GeneratePdf();
    }

    private static void ComposeCalendarPage(PageDescriptor page, ScheduleDto schedule, string monthName,
        DateTimeOffset generatedAt)
    {
        page.Size(PageSizes.Letter.Landscape());
        page.Margin(24);
        page.DefaultTextStyle(x => x.FontFamily(Fonts.Arial).FontSize(8.5f).FontColor(Ink));
        page.Header().Element(container => ComposeHeader(container, schedule, monthName, "Night-duty schedule"));
        page.Content().PaddingTop(10).Column(column =>
        {
            column.Item().Element(container => ComposeCalendar(container, schedule));
            column.Item().PaddingTop(8).Row(row =>
            {
                row.AutoItem().Background(PrimarySoft).PaddingHorizontal(7).PaddingVertical(3)
                    .Text("Assigned").SemiBold().FontColor(Primary).FontSize(7.5f);
                row.AutoItem().PaddingLeft(7).Background("#FDECE9").PaddingHorizontal(7).PaddingVertical(3)
                    .Text("Open / unfilled").SemiBold().FontColor(Danger).FontSize(7.5f);
                row.RelativeItem().AlignRight()
                    .Text($"{schedule.Shifts.Count} duty nights - {schedule.Shifts.Sum(x => x.Assignments.Count)} assignments")
                    .FontSize(7.5f).FontColor(Muted);
            });
        });
        page.Footer().Element(container => ComposeFooter(container, schedule, generatedAt));
    }

    private static void ComposeHeader(IContainer container, ScheduleDto schedule, string monthName, string subtitle)
    {
        container.BorderBottom(1).BorderColor(Line).PaddingBottom(9).Row(row =>
        {
            row.RelativeItem().Column(left =>
            {
                left.Item().Text(schedule.ResidenceHallName.ToUpperInvariant()).SemiBold().FontSize(8).FontColor(Primary);
                left.Item().Text($"{monthName} {schedule.Year}").Bold().FontSize(22);
                left.Item().Text(subtitle).FontSize(8).FontColor(Muted);
            });
            row.AutoItem().AlignRight().Column(right =>
            {
                right.Item().AlignRight().Background(PrimarySoft).PaddingHorizontal(9).PaddingVertical(4)
                    .Text("LIVE SCHEDULE").SemiBold().FontSize(7.5f).FontColor(Primary);
                right.Item().PaddingTop(5).AlignRight().Text("Changes appear immediately")
                    .FontSize(7).FontColor(Muted);
            });
        });
    }

    private static void ComposeCalendar(IContainer container, ScheduleDto schedule)
    {
        var first = new DateOnly(schedule.Year, schedule.Month, 1);
        var firstDayOffset = (int)first.DayOfWeek;
        var daysInMonth = DateTime.DaysInMonth(schedule.Year, schedule.Month);
        var weeks = (int)Math.Ceiling((firstDayOffset + daysInMonth) / 7d);
        var cellHeight = weeks == 6 ? 61f : 73f;
        var shiftsByDay = schedule.Shifts.ToDictionary(x => x.DutyDate.Day);
        var weekdays = new[] { "SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT" };

        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                for (var index = 0; index < 7; index++) columns.RelativeColumn();
            });

            foreach (var weekday in weekdays)
            {
                table.Cell().Height(22).Background("#EFF2F0").Border(0.5f).BorderColor(Line)
                    .AlignMiddle().AlignCenter().Text(weekday).SemiBold().FontSize(7).FontColor(Muted);
            }

            for (var index = 0; index < weeks * 7; index++)
            {
                var day = index - firstDayOffset + 1;
                if (day < 1 || day > daysInMonth)
                {
                    table.Cell().Height(cellHeight).Background("#FAFBFA").Border(0.5f).BorderColor(Line);
                    continue;
                }

                shiftsByDay.TryGetValue(day, out var shift);
                var isWeekend = index % 7 is 0 or 6;
                table.Cell().Height(cellHeight).Background(isWeekend ? Weekend : Colors.White)
                    .Border(0.5f).BorderColor(Line).Padding(5).Column(cell =>
                    {
                        cell.Item().Row(top =>
                        {
                            top.AutoItem().Text(day.ToString(CultureInfo.InvariantCulture)).Bold().FontSize(10);
                            if (shift is not null)
                                top.RelativeItem().AlignRight().Text(ShiftHours(shift, schedule.TimeZone))
                                    .FontSize(6.5f).FontColor(Muted);
                        });

                        if (shift is null)
                        {
                            cell.Item().PaddingTop(5).Text("No duty shift").FontSize(7).FontColor(Muted);
                            return;
                        }

                        if (shift.Status == ShiftStatus.Cancelled)
                        {
                            cell.Item().PaddingTop(5).Text("No duty").SemiBold().FontSize(7.5f).FontColor(Muted);
                            return;
                        }

                        foreach (var assignment in shift.Assignments.Take(3))
                        {
                            cell.Item().PaddingTop(3).Background(Primary).PaddingHorizontal(4).PaddingVertical(2)
                                .Text($"{assignment.FirstName} {assignment.LastName}").SemiBold().FontSize(7).FontColor(Colors.White);
                        }

                        if (shift.Assignments.Count > 3)
                            cell.Item().PaddingTop(2).Text($"+{shift.Assignments.Count - 3} more")
                                .SemiBold().FontSize(6.5f).FontColor(Primary);

                        var openings = Math.Max(0, shift.RequiredStaffCount - shift.Assignments.Count);
                        if (openings > 0)
                            cell.Item().PaddingTop(3).Text(openings == 1 ? "1 OPEN SHIFT" : $"{openings} OPEN SHIFTS")
                                .Bold().FontSize(6.5f).FontColor(Danger);
                    });
            }
        });
    }

    private static void ComposeDirectoryPage(PageDescriptor page, ScheduleDto schedule,
        IReadOnlyList<ResidentAssistantDto> staffDirectory, string monthName, DateTimeOffset generatedAt)
    {
        page.Size(PageSizes.Letter.Landscape());
        page.Margin(30);
        page.DefaultTextStyle(x => x.FontFamily(Fonts.Arial).FontSize(9).FontColor(Ink));
        page.Header().Element(container => ComposeHeader(container, schedule, monthName, "RA and hall staff directory"));
        page.Content().PaddingTop(14).Column(column =>
        {
            column.Item().Text("RA & HALL STAFF INFORMATION").SemiBold().FontSize(8).FontColor(Primary);
            column.Item().PaddingTop(3).Text("Contact details and scheduled night totals for the current hall team.")
                .FontSize(8).FontColor(Muted);
            column.Item().PaddingTop(12).Element(container => ComposeDirectoryTable(container, staffDirectory));
            column.Item().PaddingTop(13).Background("#F3F6F4").Padding(10).Row(row =>
            {
                row.RelativeItem().Text("Keep this directory within the residence-life team. Contact information may change; the app remains the current source of record.")
                    .FontSize(7.5f).FontColor(Muted);
                row.AutoItem().PaddingLeft(15).Text($"{staffDirectory.Count} active team members")
                    .SemiBold().FontSize(7.5f).FontColor(Primary);
            });
        });
        page.Footer().Element(container => ComposeFooter(container, schedule, generatedAt));
    }

    private static void ComposeDirectoryTable(IContainer container, IReadOnlyList<ResidentAssistantDto> directory)
    {
        var people = directory.OrderBy(x => RoleOrder(x.Role)).ThenBy(x => x.LastName).ThenBy(x => x.FirstName).ToList();
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(1.3f);
                columns.RelativeColumn(1.05f);
                columns.RelativeColumn(2f);
                columns.RelativeColumn(0.7f);
                columns.RelativeColumn(1.1f);
                columns.ConstantColumn(48);
            });

            table.Header(header =>
            {
                DirectoryHeader(header.Cell(), "NAME");
                DirectoryHeader(header.Cell(), "ROLE");
                DirectoryHeader(header.Cell(), "EMAIL");
                DirectoryHeader(header.Cell(), "ROOM");
                DirectoryHeader(header.Cell(), "PHONE");
                DirectoryHeader(header.Cell(), "NIGHTS", alignRight: true);
            });

            if (people.Count == 0)
            {
                table.Cell().ColumnSpan(6).BorderBottom(0.5f).BorderColor(Line).PaddingVertical(16)
                    .AlignCenter().Text("No active hall staff are listed.").FontColor(Muted);
                return;
            }

            for (var index = 0; index < people.Count; index++)
            {
                var person = people[index];
                var background = index % 2 == 0 ? "#FFFFFF" : "#F8FAF8";
                DirectoryCell(table.Cell(), $"{person.FirstName} {person.LastName}", background, semiBold: true);
                DirectoryCell(table.Cell(), RoleLabel(person), background);
                DirectoryCell(table.Cell(), person.SchoolEmail, background);
                DirectoryCell(table.Cell(), string.IsNullOrWhiteSpace(person.RoomNumber) ? "-" : person.RoomNumber, background);
                DirectoryCell(table.Cell(), string.IsNullOrWhiteSpace(person.PhoneNumber) ? "-" : person.PhoneNumber, background);
                DirectoryCell(table.Cell(), person.ShiftCount.ToString(CultureInfo.InvariantCulture), background, alignRight: true, semiBold: true);
            }
        });
    }

    private static void DirectoryHeader(IContainer container, string label, bool alignRight = false)
    {
        var cell = container.Background(Ink).PaddingHorizontal(7).PaddingVertical(6);
        if (alignRight) cell = cell.AlignRight();
        cell.Text(label).SemiBold().FontSize(7).FontColor(Colors.White);
    }

    private static void DirectoryCell(IContainer container, string value, string background,
        bool alignRight = false, bool semiBold = false)
    {
        var cell = container.Background(background).BorderBottom(0.5f).BorderColor(Line)
            .PaddingHorizontal(7).PaddingVertical(7).AlignMiddle();
        if (alignRight) cell = cell.AlignRight();
        var text = cell.Text(value).FontSize(8);
        if (semiBold) text.SemiBold();
    }

    private static void ComposeFooter(IContainer container, ScheduleDto schedule, DateTimeOffset generatedAt)
    {
        var localGeneratedAt = LocalTime(generatedAt, schedule.TimeZone);
        container.BorderTop(0.75f).BorderColor(Line).PaddingTop(6).Row(row =>
        {
            row.RelativeItem().Text($"Generated {localGeneratedAt:MMM d, yyyy 'at' h:mm tt} - {schedule.ResidenceHallName} local time")
                .FontSize(6.5f).FontColor(Muted);
            row.AutoItem().Text(text =>
            {
                text.DefaultTextStyle(x => x.FontSize(6.5f).FontColor(Muted));
                text.Span("Page ");
                text.CurrentPageNumber();
                text.Span(" of ");
                text.TotalPages();
            });
        });
    }

    private static string ShiftHours(ShiftDto shift, string timeZone)
    {
        var starts = LocalTime(shift.StartsAt, timeZone);
        var ends = LocalTime(shift.EndsAt, timeZone);
        return $"{starts:h tt}-{ends:h tt}";
    }

    private static DateTimeOffset LocalTime(DateTimeOffset value, string timeZone) =>
        TimeZoneInfo.ConvertTime(value, TimeZoneInfo.FindSystemTimeZoneById(timeZone));

    private static int RoleOrder(HallRole role) => role switch
    {
        HallRole.ResidentAssistant => 0,
        HallRole.HallDirector => 1,
        HallRole.Admin => 2,
        _ => 3
    };

    private static string RoleLabel(ResidentAssistantDto person)
    {
        if (string.Equals(person.SchoolEmail, "CezarPedroso@wmpenn.edu", StringComparison.OrdinalIgnoreCase))
            return "Resident Assistant";

        return person.Role switch
        {
            HallRole.ResidentAssistant => "Resident Assistant",
            HallRole.HallDirector => "Hall Director",
            HallRole.Admin => "Admin",
            _ => person.Role.ToString()
        };
    }

}

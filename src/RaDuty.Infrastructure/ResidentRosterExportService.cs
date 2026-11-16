using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using RaDuty.Application;

namespace RaDuty.Infrastructure;

public sealed class ResidentRosterExportService(RaDutyDbContext db, ICurrentUserService currentUserService)
    : IResidentRosterExportService
{
    private static readonly XLColor Navy = XLColor.FromHtml("#153B45");
    private static readonly XLColor Teal = XLColor.FromHtml("#1E6070");
    private static readonly XLColor Gold = XLColor.FromHtml("#E7B56D");
    private static readonly XLColor Pale = XLColor.FromHtml("#EAF3F3");
    private static readonly XLColor Stripe = XLColor.FromHtml("#F4F8F8");
    private static readonly XLColor Line = XLColor.FromHtml("#D9E5E5");
    private static readonly XLColor Muted = XLColor.FromHtml("#52717A");

    public async Task<ResidentRosterWorkbookDto> ExportAsync(CancellationToken cancellationToken)
    {
        var current = await currentUserService.GetAsync(cancellationToken);
        var hall = await db.ResidenceHalls.AsNoTracking()
            .SingleAsync(hall => hall.Id == current.ResidenceHallId, cancellationToken);
        var rooms = await db.DormRooms.AsNoTracking()
            .Include(room => room.ResidenceHall)
            .Include(room => room.Residents)
            .Where(room => room.ResidenceHallId == hall.Id)
            .OrderBy(room => room.SuiteNumber).ThenBy(room => room.RoomLetter)
            .ToListAsync(cancellationToken);
        var generatedAt = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById(hall.TimeZone));
        var residentCount = rooms.Sum(room => room.Residents.Count);
        var occupiedRooms = rooms.Count(room => room.Residents.Count > 0);

        using var workbook = new XLWorkbook();
        workbook.Properties.Title = $"{hall.Name} resident roster";
        workbook.Properties.Subject = "Residence hall resident roster";
        var sheet = workbook.Worksheets.Add("Residents");
        sheet.ShowGridLines = false;
        sheet.Style.Font.FontName = "Aptos";
        sheet.Style.Font.FontSize = 11;
        sheet.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        sheet.Column(1).Width = 20;
        sheet.Column(2).Width = 21;
        sheet.Column(3).Width = 21;
        sheet.Column(4).Width = 35;
        sheet.Column(5).Width = 17;
        sheet.Column(6).Width = 15;

        sheet.Row(1).Height = 7;
        sheet.Range("A1:F1").Style.Fill.BackgroundColor = Gold;
        sheet.Row(2).Height = 28;
        sheet.Row(3).Height = 25;
        sheet.Row(4).Height = 24;
        sheet.Row(5).Height = 25;
        sheet.Range("A2:F5").Style.Fill.BackgroundColor = Navy;
        sheet.Range("A2:F5").Style.Font.FontColor = XLColor.White;
        sheet.Range("A2:F5").Style.Alignment.Indent = 1;
        sheet.Range("A2:F3").Merge();
        sheet.Cell("A2").Value = "RESIDENT ROSTER";
        sheet.Cell("A2").Style.Font.FontSize = 23;
        sheet.Cell("A2").Style.Font.Bold = true;
        sheet.Range("A4:F4").Merge();
        sheet.Cell("A4").Value = hall.Name;
        sheet.Cell("A4").Style.Font.FontSize = 14;
        sheet.Cell("A4").Style.Font.Bold = true;
        sheet.Range("A5:F5").Merge();
        sheet.Cell("A5").Value = $"Generated {generatedAt.ToString("MMMM d, yyyy 'at' h:mm tt", CultureInfo.GetCultureInfo("en-US"))}  •  {hall.TimeZone}";
        sheet.Cell("A5").Style.Font.FontSize = 10;
        sheet.Cell("A5").Style.Font.FontColor = XLColor.FromHtml("#C8DCDF");
        sheet.Row(6).Height = 16;

        SummaryCard(sheet, 1, "RESIDENTS", residentCount);
        SummaryCard(sheet, 3, "OCCUPIED ROOMS", occupiedRooms);
        SummaryCard(sheet, 5, "ROOMS IN HALL", rooms.Count);
        sheet.Row(10).Height = 18;
        sheet.Row(11).Height = 26;
        sheet.Range("A11:F11").Merge();
        sheet.Cell("A11").Value = "RESIDENT DETAILS";
        sheet.Cell("A11").Style.Font.Bold = true;
        sheet.Cell("A11").Style.Font.FontColor = Navy;
        sheet.Cell("A11").Style.Font.FontSize = 12;
        sheet.Range("A11:F11").Style.Border.BottomBorder = XLBorderStyleValues.Medium;
        sheet.Range("A11:F11").Style.Border.BottomBorderColor = Gold;

        var headers = new[] { "Room", "First name", "Last name", "Sport / activity", "Occupancy", "Capacity" };
        sheet.Row(12).Height = 31;
        for (var column = 1; column <= headers.Length; column++)
            sheet.Cell(12, column).Value = headers[column - 1];
        var header = sheet.Range(12, 1, 12, 6);
        header.Style.Fill.BackgroundColor = Teal;
        header.Style.Font.FontColor = XLColor.White;
        header.Style.Font.Bold = true;
        header.Style.Alignment.Indent = 1;

        var rowNumber = 13;
        foreach (var room in rooms)
        {
            var occupancy = room.Residents.Count;
            foreach (var resident in room.Residents.OrderBy(person => person.LastName).ThenBy(person => person.FirstName))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var row = sheet.Range(rowNumber, 1, rowNumber, 6);
                row.Style.Fill.BackgroundColor = rowNumber % 2 == 0 ? Stripe : XLColor.White;
                row.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
                row.Style.Border.BottomBorderColor = Line;
                var activity = resident.SportOrActivity ?? "";
                sheet.Row(rowNumber).Height = Math.Min(64, 25 + 14 * (activity.Length / 35));
                sheet.Cell(rowNumber, 1).Value = DormRoomNaming.Code(room);
                sheet.Cell(rowNumber, 1).Style.Font.Bold = true;
                sheet.Cell(rowNumber, 1).Style.Font.FontColor = Teal;
                sheet.Cell(rowNumber, 2).Value = resident.FirstName;
                sheet.Cell(rowNumber, 3).Value = resident.LastName;
                sheet.Cell(rowNumber, 4).Value = activity;
                sheet.Cell(rowNumber, 4).Style.Alignment.WrapText = true;
                sheet.Cell(rowNumber, 5).Value = occupancy;
                sheet.Cell(rowNumber, 6).Value = room.Capacity;
                sheet.Range(rowNumber, 5, rowNumber, 6).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                rowNumber++;
            }
        }

        if (residentCount == 0)
        {
            sheet.Range("A13:F13").Merge();
            sheet.Cell("A13").Value = "No residents are assigned to this hall yet.";
            sheet.Cell("A13").Style.Font.FontColor = Muted;
            sheet.Row(13).Height = 34;
        }
        else
        {
            sheet.Range(12, 1, rowNumber - 1, 6).SetAutoFilter();
        }
        sheet.SheetView.FreezeRows(12);
        sheet.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        sheet.PageSetup.PaperSize = XLPaperSize.LetterPaper;
        sheet.PageSetup.FitToPages(1, 0);
        sheet.PageSetup.SetRowsToRepeatAtTop(11, 12);

        using var output = new MemoryStream();
        workbook.SaveAs(output);
        var slug = Regex.Replace(hall.Name.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        return new ResidentRosterWorkbookDto(output.ToArray(), $"{slug}-residents-{generatedAt:yyyy-MM-dd}.xlsx");
    }

    private static void SummaryCard(IXLWorksheet sheet, int firstColumn, string label, int value)
    {
        var lastColumn = firstColumn + 1;
        var card = sheet.Range(7, firstColumn, 9, lastColumn);
        card.Style.Fill.BackgroundColor = Pale;
        card.Style.Alignment.Indent = 1;
        sheet.Range(7, firstColumn, 7, lastColumn).Merge();
        sheet.Range(8, firstColumn, 9, lastColumn).Merge();
        sheet.Cell(7, firstColumn).Value = label;
        sheet.Cell(7, firstColumn).Style.Font.FontSize = 9;
        sheet.Cell(7, firstColumn).Style.Font.Bold = true;
        sheet.Cell(7, firstColumn).Style.Font.FontColor = Muted;
        sheet.Cell(8, firstColumn).Value = value;
        sheet.Cell(8, firstColumn).Style.Font.FontSize = 22;
        sheet.Cell(8, firstColumn).Style.Font.Bold = true;
        sheet.Cell(8, firstColumn).Style.Font.FontColor = Navy;
        sheet.Row(7).Height = 23;
        sheet.Row(8).Height = 20;
        sheet.Row(9).Height = 21;
    }
}

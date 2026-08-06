using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using RaDuty.Application;
using RaDuty.Domain;

namespace RaDuty.Infrastructure;

public sealed class DormRosterImportService(RaDutyDbContext db, ICurrentUserService currentUserService) : IDormRosterImportService
{
    private const long MaximumWorkbookBytes = 10 * 1024 * 1024;
    private const int MaximumRows = 5000;

    public async Task<DormRosterImportPreviewDto> PreviewAsync(DormRosterWorkbookUpload upload, CancellationToken cancellationToken)
    {
        var current = await currentUserService.GetAsync(cancellationToken);
        var parsed = await ParseAsync(upload, cancellationToken);
        var rooms = await db.DormRooms.AsNoTracking().Include(room => room.Residents)
            .Where(room => room.ResidenceHallId == current.ResidenceHallId)
            .OrderBy(room => room.SuiteNumber).ThenBy(room => room.RoomLetter)
            .ToListAsync(cancellationToken);
        return Analyze(parsed, rooms).Preview;
    }

    public async Task<DormRosterImportPreviewDto> ApplyAsync(DormRosterWorkbookUpload upload, CancellationToken cancellationToken)
    {
        var current = await currentUserService.GetAsync(cancellationToken);
        var parsed = await ParseAsync(upload, cancellationToken);
        var rooms = await db.DormRooms.Include(room => room.Residents)
            .Where(room => room.ResidenceHallId == current.ResidenceHallId)
            .OrderBy(room => room.SuiteNumber).ThenBy(room => room.RoomLetter)
            .ToListAsync(cancellationToken);
        var analysis = Analyze(parsed, rooms);
        if (!analysis.Preview.CanApply)
            throw new AppException(400, "ROSTER_IMPORT_HAS_ERRORS", "Fix the spreadsheet issues before updating the resident roster.");

        var roomByCode = rooms.ToDictionary(RoomCode, StringComparer.OrdinalIgnoreCase);
        foreach (var match in analysis.Matches)
        {
            var room = roomByCode[match.Incoming.RoomCode];
            if (match.Current is null)
            {
                db.DormResidents.Add(new DormResident
                {
                    DormRoomId = room.Id,
                    FirstName = match.Incoming.FirstName,
                    LastName = match.Incoming.LastName,
                    SportOrActivity = match.Incoming.Activity,
                    SourceRow = match.Incoming.SourceRow
                });
                continue;
            }

            match.Current.Entity.DormRoomId = room.Id;
            match.Current.Entity.FirstName = match.Incoming.FirstName;
            match.Current.Entity.LastName = match.Incoming.LastName;
            match.Current.Entity.SportOrActivity = match.Incoming.Activity;
            match.Current.Entity.SourceRow = match.Incoming.SourceRow;
        }
        db.DormResidents.RemoveRange(analysis.Removed.Select(resident => resident.Entity));
        db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = current.Id,
            Action = "DORM_ROSTER_IMPORTED",
            EntityType = "ResidenceHall",
            EntityId = current.ResidenceHallId.ToString(),
            OldValuesJson = JsonSerializer.Serialize(new { ResidentCount = analysis.CurrentResidentCount }),
            NewValuesJson = JsonSerializer.Serialize(new
            {
                FileName = analysis.Preview.FileName,
                analysis.Preview.ResidentCount,
                analysis.Preview.AddedResidents,
                analysis.Preview.RemovedResidents,
                analysis.Preview.MovedResidents,
                analysis.Preview.UpdatedResidents
            })
        });
        await db.SaveChangesAsync(cancellationToken);
        return analysis.Preview;
    }

    private static AnalysisResult Analyze(ParsedWorkbook parsed, IReadOnlyList<DormRoom> rooms)
    {
        var issues = parsed.Issues.ToList();
        var roomByCode = rooms.ToDictionary(RoomCode, StringComparer.OrdinalIgnoreCase);
        var incoming = new List<ParsedResident>();
        foreach (var resident in parsed.Residents)
        {
            if (!roomByCode.ContainsKey(resident.RoomCode))
            {
                issues.Add(new DormRosterImportIssueDto(resident.SourceRow, $"Room {resident.RoomCode} does not exist in Eltse Hall."));
                continue;
            }
            incoming.Add(resident);
        }

        var current = rooms.SelectMany(room => room.Residents.Select(resident => new CurrentResident(resident, RoomCode(room)))).ToList();
        var unmatchedCurrent = new HashSet<Guid>(current.Select(resident => resident.Entity.Id));
        var currentByPerson = current.GroupBy(resident => PersonKey(resident.Entity.FirstName, resident.Entity.LastName))
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        var matches = new List<ResidentMatch>();
        var changes = new List<DormRosterChangeDto>();
        var moved = 0;
        var added = 0;
        var updated = 0;
        var unchanged = 0;

        foreach (var resident in incoming)
        {
            currentByPerson.TryGetValue(PersonKey(resident.FirstName, resident.LastName), out var candidates);
            var available = candidates?.Where(candidate => unmatchedCurrent.Contains(candidate.Entity.Id)).ToList() ?? [];
            var matched = available.FirstOrDefault(candidate => string.Equals(candidate.RoomCode, resident.RoomCode, StringComparison.OrdinalIgnoreCase))
                ?? available.FirstOrDefault();
            if (matched is null)
            {
                added++;
                changes.Add(new DormRosterChangeDto("Added", resident.FirstName, resident.LastName, null, resident.RoomCode));
                matches.Add(new ResidentMatch(resident, null));
                continue;
            }

            unmatchedCurrent.Remove(matched.Entity.Id);
            matches.Add(new ResidentMatch(resident, matched));
            if (!string.Equals(matched.RoomCode, resident.RoomCode, StringComparison.OrdinalIgnoreCase))
            {
                moved++;
                changes.Add(new DormRosterChangeDto("Moved", resident.FirstName, resident.LastName, matched.RoomCode, resident.RoomCode));
            }
            else if (!SameDetails(matched.Entity, resident))
            {
                updated++;
                changes.Add(new DormRosterChangeDto("Updated", resident.FirstName, resident.LastName, matched.RoomCode, resident.RoomCode));
            }
            else
            {
                unchanged++;
            }
        }

        var removed = current.Where(resident => unmatchedCurrent.Contains(resident.Entity.Id)).ToList();
        changes.AddRange(removed.Select(resident => new DormRosterChangeDto("Removed", resident.Entity.FirstName,
            resident.Entity.LastName, resident.RoomCode, null)));
        if (incoming.Count == 0)
            issues.Add(new DormRosterImportIssueDto(null, "The spreadsheet does not contain any residents assigned to ELTS rooms."));

        var preview = new DormRosterImportPreviewDto(parsed.FileName, parsed.RowsRead, parsed.IgnoredRows,
            incoming.Count, incoming.Select(resident => resident.RoomCode).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            added, removed.Count, moved, updated, unchanged, issues.Count == 0, issues,
            changes.OrderBy(change => ChangeOrder(change.Type)).ThenBy(change => change.LastName).ThenBy(change => change.FirstName).ToList());
        return new AnalysisResult(preview, matches, removed, current.Count);
    }

    private static async Task<ParsedWorkbook> ParseAsync(DormRosterWorkbookUpload upload, CancellationToken cancellationToken)
    {
        var fileName = Path.GetFileName(upload.FileName.Trim());
        if (string.IsNullOrWhiteSpace(fileName) || !string.Equals(Path.GetExtension(fileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new AppException(400, "ROSTER_FILE_TYPE_INVALID", "Choose an Excel .xlsx spreadsheet.");
        if (upload.Length <= 0) throw new AppException(400, "ROSTER_FILE_EMPTY", "The selected spreadsheet is empty.");
        if (upload.Length > MaximumWorkbookBytes) throw new AppException(400, "ROSTER_FILE_TOO_LARGE", "The spreadsheet must be 10 MB or smaller.");

        await using var memory = new MemoryStream((int)Math.Min(upload.Length, MaximumWorkbookBytes));
        await upload.Content.CopyToAsync(memory, cancellationToken);
        if (memory.Length == 0) throw new AppException(400, "ROSTER_FILE_EMPTY", "The selected spreadsheet is empty.");
        if (memory.Length > MaximumWorkbookBytes) throw new AppException(400, "ROSTER_FILE_TOO_LARGE", "The spreadsheet must be 10 MB or smaller.");
        var bytes = memory.GetBuffer();
        if (memory.Length < 4 || bytes[0] != 0x50 || bytes[1] != 0x4B)
            throw new AppException(400, "ROSTER_FILE_INVALID", "The selected file is not a valid Excel .xlsx spreadsheet.");
        memory.Position = 0;

        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(memory);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not OperationCanceledException)
        {
            throw new AppException(400, "ROSTER_FILE_INVALID", "The spreadsheet could not be read. Save it as an Excel .xlsx file and try again.");
        }
        using (workbook)
        {
            var header = FindHeader(workbook);
            if (header is null)
                return new ParsedWorkbook(fileName, 0, 0, [],
                    [new DormRosterImportIssueDto(null, "Could not find the required LastName, FirstName, and Room columns.")]);

            var issues = new List<DormRosterImportIssueDto>();
            var residents = new List<ParsedResident>();
            var seenPeople = new Dictionary<string, int>(StringComparer.Ordinal);
            var rowsRead = 0;
            var ignoredRows = 0;
            var dataRows = header.Worksheet.RowsUsed().Where(row => row.RowNumber() > header.RowNumber).ToList();
            if (dataRows.Count > MaximumRows)
                issues.Add(new DormRosterImportIssueDto(null, $"The spreadsheet has more than {MaximumRows:N0} data rows."));

            foreach (var row in dataRows.Take(MaximumRows))
            {
                if (rowsRead % 100 == 0) cancellationToken.ThrowIfCancellationRequested();
                var lastName = CellText(row, header.LastNameColumn);
                var firstName = CellText(row, header.FirstNameColumn);
                var roomText = CellText(row, header.RoomColumn);
                var activity = header.ActivityColumn is null ? null : NullIfWhiteSpace(CellText(row, header.ActivityColumn.Value));
                if (string.IsNullOrWhiteSpace(lastName) && string.IsNullOrWhiteSpace(firstName) && string.IsNullOrWhiteSpace(roomText)) continue;
                rowsRead++;

                if (!IsEltseRoomCandidate(roomText))
                {
                    if (string.IsNullOrWhiteSpace(roomText))
                        issues.Add(new DormRosterImportIssueDto(row.RowNumber(), "A resident row is missing a room assignment."));
                    else
                        ignoredRows++;
                    continue;
                }
                var roomCode = NormalizeRoom(roomText);
                if (roomCode is null)
                {
                    issues.Add(new DormRosterImportIssueDto(row.RowNumber(), $"'{roomText}' is not a valid ELTS room. Use a room such as ELTS-01A."));
                    continue;
                }
                if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
                {
                    issues.Add(new DormRosterImportIssueDto(row.RowNumber(), "ELTS resident rows must include both FirstName and LastName."));
                    continue;
                }
                firstName = CleanText(firstName);
                lastName = CleanText(lastName);
                activity = activity is null ? null : CleanText(activity);
                if (firstName.Length > 80 || lastName.Length > 80)
                {
                    issues.Add(new DormRosterImportIssueDto(row.RowNumber(), "Resident first and last names must be 80 characters or fewer."));
                    continue;
                }
                if (activity?.Length > 120)
                {
                    issues.Add(new DormRosterImportIssueDto(row.RowNumber(), "Sport/Activity must be 120 characters or fewer."));
                    continue;
                }
                var personKey = PersonKey(firstName, lastName);
                if (seenPeople.TryGetValue(personKey, out var earlierRow))
                {
                    issues.Add(new DormRosterImportIssueDto(row.RowNumber(), $"This resident also appears on row {earlierRow}. Each resident should appear once."));
                    continue;
                }
                seenPeople.Add(personKey, row.RowNumber());
                residents.Add(new ParsedResident(row.RowNumber(), firstName, lastName, roomCode, activity));
            }
            return new ParsedWorkbook(fileName, rowsRead, ignoredRows, residents, issues);
        }
    }

    private static HeaderLocation? FindHeader(XLWorkbook workbook)
    {
        foreach (var worksheet in workbook.Worksheets)
        {
            var lastColumn = Math.Min(worksheet.LastColumnUsed()?.ColumnNumber() ?? 0, 50);
            if (lastColumn == 0) continue;
            foreach (var row in worksheet.RowsUsed().Take(25))
            {
                var columns = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var column in Enumerable.Range(1, lastColumn))
                {
                    var name = NormalizeHeader(row.Cell(column).GetString());
                    if (name.Length > 0) columns.TryAdd(name, column);
                }
                var lastName = FindColumn(columns, "lastname", "surname", "familyname");
                var firstName = FindColumn(columns, "firstname", "givenname");
                var room = FindColumn(columns, "room", "roomnumber", "dormroom", "assignment");
                if (lastName is null || firstName is null || room is null) continue;
                var activity = FindColumn(columns, "sportactivity", "sport", "activity", "sportoractivity");
                return new HeaderLocation(worksheet, row.RowNumber(), lastName.Value, firstName.Value, room.Value, activity);
            }
        }
        return null;
    }

    private static int? FindColumn(IReadOnlyDictionary<string, int> columns, params string[] aliases)
    {
        foreach (var alias in aliases)
            if (columns.TryGetValue(alias, out var column)) return column;
        return null;
    }

    private static string CellText(IXLRow row, int column) => row.Cell(column).GetFormattedString().Trim();
    private static string? NullIfWhiteSpace(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
    private static string CleanText(string value) => Regex.Replace(value.Normalize(NormalizationForm.FormKC).Trim(), @"\s+", " ");
    private static string NormalizeHeader(string value) => string.Concat(CleanText(value).Where(char.IsLetterOrDigit)).ToLowerInvariant();
    private static bool IsEltseRoomCandidate(string value) => Regex.Replace(value.ToUpperInvariant(), @"[\s_-]", "").StartsWith("ELTS", StringComparison.Ordinal);

    private static string? NormalizeRoom(string value)
    {
        var compact = Regex.Replace(value.ToUpperInvariant(), @"[\s_-]", "");
        var match = Regex.Match(compact, @"^ELTS(?<suite>\d{1,2})(?<room>[A-D])$");
        if (!match.Success || !int.TryParse(match.Groups["suite"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var suite) || suite is < 1 or > 25)
            return null;
        return $"ELTS-{suite:00}{match.Groups["room"].Value}";
    }

    private static string PersonKey(string firstName, string lastName) => $"{CleanText(firstName).ToUpperInvariant()}|{CleanText(lastName).ToUpperInvariant()}";
    private static string RoomCode(DormRoom room) => $"ELTS-{room.SuiteNumber}{room.RoomLetter}";
    private static bool SameDetails(DormResident current, ParsedResident incoming) =>
        string.Equals(CleanText(current.FirstName), incoming.FirstName, StringComparison.Ordinal) &&
        string.Equals(CleanText(current.LastName), incoming.LastName, StringComparison.Ordinal) &&
        string.Equals(NullIfWhiteSpace(current.SportOrActivity ?? string.Empty), incoming.Activity, StringComparison.Ordinal);
    private static int ChangeOrder(string type) => type switch { "Moved" => 0, "Added" => 1, "Removed" => 2, _ => 3 };

    private sealed record ParsedWorkbook(string FileName, int RowsRead, int IgnoredRows, IReadOnlyList<ParsedResident> Residents,
        IReadOnlyList<DormRosterImportIssueDto> Issues);
    private sealed record ParsedResident(int SourceRow, string FirstName, string LastName, string RoomCode, string? Activity);
    private sealed record CurrentResident(DormResident Entity, string RoomCode);
    private sealed record ResidentMatch(ParsedResident Incoming, CurrentResident? Current);
    private sealed record AnalysisResult(DormRosterImportPreviewDto Preview, IReadOnlyList<ResidentMatch> Matches,
        IReadOnlyList<CurrentResident> Removed, int CurrentResidentCount);
    private sealed record HeaderLocation(IXLWorksheet Worksheet, int RowNumber, int LastNameColumn, int FirstNameColumn,
        int RoomColumn, int? ActivityColumn);
}

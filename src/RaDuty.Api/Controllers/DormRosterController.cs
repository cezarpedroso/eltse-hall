using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RaDuty.Application;

namespace RaDuty.Api.Controllers;

[ApiController, Route("api/admin/dorm-roster"), Authorize(Policy = "HallDirectorOnly"), EnableRateLimiting("roster-imports")]
public sealed class DormRosterController(IDormRosterImportService rosterImport) : ControllerBase
{
    private const int MaximumRequestBytes = 11 * 1024 * 1024;

    [HttpPost("preview"), Consumes("multipart/form-data"), RequestSizeLimit(MaximumRequestBytes)]
    public Task<DormRosterImportPreviewDto> Preview([FromForm] IFormFile workbook, CancellationToken cancellationToken) =>
        WithWorkbookAsync(workbook, rosterImport.PreviewAsync, cancellationToken);

    [HttpPost("apply"), Consumes("multipart/form-data"), RequestSizeLimit(MaximumRequestBytes)]
    public Task<DormRosterImportPreviewDto> Apply([FromForm] IFormFile workbook, CancellationToken cancellationToken) =>
        WithWorkbookAsync(workbook, rosterImport.ApplyAsync, cancellationToken);

    private static async Task<DormRosterImportPreviewDto> WithWorkbookAsync(IFormFile workbook,
        Func<DormRosterWorkbookUpload, CancellationToken, Task<DormRosterImportPreviewDto>> action,
        CancellationToken cancellationToken)
    {
        if (workbook is null) throw new AppException(400, "ROSTER_FILE_REQUIRED", "Choose an Excel spreadsheet.");
        await using var stream = workbook.OpenReadStream();
        return await action(new DormRosterWorkbookUpload(workbook.FileName, workbook.Length, stream), cancellationToken);
    }
}

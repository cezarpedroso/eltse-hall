using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RaDuty.Application;

namespace RaDuty.Api.Controllers;

[ApiController, Route("api/dorm-checks"), Authorize(Policy = "ResidentAssistantOrDirector")]
public sealed class DormChecksController(IDormCheckService dormChecks, IDormCheckPdfService pdf, IDormCheckPhotoService photos) : ControllerBase
{
    [HttpGet("suites")]
    public Task<IReadOnlyList<DormSuiteDto>> GetSuites(CancellationToken cancellationToken) =>
        dormChecks.GetSuitesAsync(cancellationToken);

    [HttpGet("pdf")]
    public async Task<IActionResult> Pdf(CancellationToken cancellationToken)
    {
        var report = await dormChecks.GetReportAsync(cancellationToken);
        var bytes = pdf.Render(report, DateTimeOffset.UtcNow);
        return File(bytes, "application/pdf", $"{Slug(report.ResidenceHallName)}-dorm-checks.pdf");
    }

    [HttpPost("rooms/{roomId:guid}")]
    public async Task<ActionResult<DormRoomCheckDto>> Submit(Guid roomId, SubmitDormRoomCheckRequest request, CancellationToken cancellationToken)
    {
        var check = await dormChecks.SubmitAsync(roomId, request, cancellationToken);
        return Created($"/api/dorm-checks/rooms/{roomId}/checks/{check.Id}", check);
    }

    [HttpDelete]
    public Task<DormCheckResetDto> Reset(CancellationToken cancellationToken) =>
        dormChecks.ResetAsync(cancellationToken);

    [HttpPost("checks/{checkId:guid}/photos"), RequestSizeLimit(21 * 1024 * 1024)]
    public async Task<ActionResult<IReadOnlyList<DormCheckPhotoDto>>> AddPhotos(Guid checkId, [FromForm] List<IFormFile> photosToAdd, CancellationToken cancellationToken)
    {
        var streams = photosToAdd.Select(file => file.OpenReadStream()).ToList();
        try
        {
            var uploads = photosToAdd.Select((file, index) => new DormCheckPhotoUpload(file.FileName, file.ContentType, file.Length, streams[index])).ToList();
            var saved = await photos.AddAsync(checkId, uploads, cancellationToken);
            return Created($"/api/dorm-checks/checks/{checkId}/photos", saved);
        }
        finally
        {
            foreach (var stream in streams) await stream.DisposeAsync();
        }
    }

    [HttpGet("photos/{photoId:guid}")]
    public async Task<IActionResult> GetPhoto(Guid photoId, CancellationToken cancellationToken)
    {
        var photo = await photos.GetAsync(photoId, cancellationToken);
        return File(photo.Content, photo.ContentType, photo.FileName);
    }

    private static string Slug(string value) => string.Concat(value.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-')).Trim('-');
}

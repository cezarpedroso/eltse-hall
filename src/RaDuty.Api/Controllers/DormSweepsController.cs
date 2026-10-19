using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RaDuty.Application;

namespace RaDuty.Api.Controllers;

[ApiController, Route("api/dorm-sweeps"), Authorize(Policy = "ResidentAssistantOrDirector")]
public sealed class DormSweepsController(IDormSweepService dormSweeps, IDormSweepPdfService pdf, IDormSweepPhotoService photos) : ControllerBase
{
    [HttpGet("suites")]
    public Task<IReadOnlyList<DormSweepSuiteDto>> GetSuites(CancellationToken cancellationToken) =>
        dormSweeps.GetSuitesAsync(cancellationToken);

    [HttpGet("pdf")]
    public async Task<IActionResult> Pdf(CancellationToken cancellationToken)
    {
        var report = await dormSweeps.GetReportAsync(cancellationToken);
        var bytes = pdf.Render(report, DateTimeOffset.UtcNow);
        return File(bytes, "application/pdf", $"{Slug(report.ResidenceHallName)}-dorm-sweeps.pdf");
    }

    [HttpPost("suites/{suiteNumber}")]
    public async Task<ActionResult<DormSuiteSweepDto>> Submit(string suiteNumber,
        SubmitDormSuiteSweepRequest request, CancellationToken cancellationToken)
    {
        var sweep = await dormSweeps.SubmitAsync(suiteNumber, request, cancellationToken);
        return Created($"/api/dorm-sweeps/suites/{sweep.SuiteNumber}/sweeps/{sweep.Id}", sweep);
    }

    [HttpDelete]
    public Task<DormSweepResetDto> Reset(CancellationToken cancellationToken) =>
        dormSweeps.ResetAsync(cancellationToken);

    [HttpPost("sweeps/{sweepId:guid}/photos"), RequestSizeLimit(21 * 1024 * 1024)]
    public async Task<ActionResult<IReadOnlyList<DormSweepPhotoDto>>> AddPhotos(Guid sweepId, [FromForm] List<IFormFile> photosToAdd, CancellationToken cancellationToken)
    {
        var streams = photosToAdd.Select(file => file.OpenReadStream()).ToList();
        try
        {
            var uploads = photosToAdd.Select((file, index) => new DormSweepPhotoUpload(file.FileName, file.ContentType, file.Length, streams[index])).ToList();
            var saved = await photos.AddAsync(sweepId, uploads, cancellationToken);
            return Created($"/api/dorm-sweeps/sweeps/{sweepId}/photos", saved);
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
        return File(photo.Content, photo.ContentType);
    }

    private static string Slug(string value) => string.Concat(value.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-')).Trim('-');
}

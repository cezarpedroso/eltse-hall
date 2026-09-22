using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RaDuty.Application;

namespace RaDuty.Api.Controllers;

[ApiController, Route("api/dorm-sweeps"), Authorize(Policy = "ResidentAssistantOrDirector")]
public sealed class DormSweepsController(IDormSweepService dormSweeps, IDormSweepPdfService pdf) : ControllerBase
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

    private static string Slug(string value) => string.Concat(value.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-')).Trim('-');
}

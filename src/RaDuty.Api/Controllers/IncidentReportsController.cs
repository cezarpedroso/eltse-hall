using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RaDuty.Application;

namespace RaDuty.Api.Controllers;

[ApiController, Route("api/incidents"), Authorize(Policy = "ResidentAssistantOrDirector")]
public sealed class IncidentReportsController(IIncidentReportService incidents, IIncidentReportPdfService pdf, ICurrentUserService currentUser) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<IncidentReportSummaryDto>> GetAll(CancellationToken cancellationToken) =>
        incidents.GetAllAsync(cancellationToken);

    [HttpGet("{incidentId:guid}")]
    public Task<IncidentReportDto> Get(Guid incidentId, CancellationToken cancellationToken) =>
        incidents.GetAsync(incidentId, cancellationToken);

    [HttpPost]
    public async Task<ActionResult<IncidentReportDto>> Create(CreateIncidentReportRequest request, CancellationToken cancellationToken)
    {
        var incident = await incidents.CreateAsync(request, cancellationToken);
        return Created($"/api/incidents/{incident.Id}", incident);
    }

    [HttpPut("{incidentId:guid}")]
    public Task<IncidentReportDto> Update(Guid incidentId, CreateIncidentReportRequest request, CancellationToken cancellationToken) =>
        incidents.UpdateAsync(incidentId, request, cancellationToken);

    [HttpDelete("{incidentId:guid}")]
    public async Task<IActionResult> Delete(Guid incidentId, CancellationToken cancellationToken)
    {
        await incidents.DeleteAsync(incidentId, cancellationToken);
        return NoContent();
    }

    [HttpGet("{incidentId:guid}/pdf")]
    public async Task<IActionResult> Pdf(Guid incidentId, CancellationToken cancellationToken)
    {
        var incident = await incidents.GetAsync(incidentId, cancellationToken);
        var user = await currentUser.GetAsync(cancellationToken);
        var bytes = pdf.Render(incident, user.ResidenceHallName, DateTimeOffset.UtcNow);
        return File(bytes, "application/pdf", $"{Slug(user.ResidenceHallName)}-incident-{incident.OccurredAt:yyyyMMdd}.pdf");
    }

    private static string Slug(string value) => string.Concat(value.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-')).Trim('-');
}

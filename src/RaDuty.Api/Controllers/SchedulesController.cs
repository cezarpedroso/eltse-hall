using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RaDuty.Application;
using RaDuty.Domain;

namespace RaDuty.Api.Controllers;

[ApiController, Route("api"), Authorize(Policy = "ResidentAssistantOrDirector")]
public sealed class SchedulesController(IScheduleService schedules, ISchedulePdfService pdfService) : ControllerBase
{
    [HttpGet("schedules/{year:int}/{month:int}")]
    public Task<ScheduleDto> Get(int year, int month, CancellationToken cancellationToken) => schedules.GetAsync(year, month, cancellationToken);

    [HttpGet("schedules/{year:int}/{month:int}/summary")]
    public Task<ScheduleSummaryDto> GetSummary(int year, int month, CancellationToken cancellationToken) => schedules.GetSummaryAsync(year, month, cancellationToken);

    [HttpGet("schedules/{year:int}/{month:int}/pdf")]
    public async Task<IActionResult> Pdf(int year, int month, CancellationToken cancellationToken)
    {
        var schedule = await schedules.GetAsync(year, month, cancellationToken);
        if (schedule.Status != ScheduleStatus.Published && !User.IsInRole("HallDirector"))
            throw new AppException(422, "SCHEDULE_NOT_PUBLISHED", "The final PDF is available after the schedule is published.");
        var bytes = pdfService.Render(schedule, DateTimeOffset.UtcNow);
        return File(bytes, "application/pdf", $"{Slug(schedule.ResidenceHallName)}-{year}-{month:00}-night-duty.pdf");
    }

    [HttpPost("shifts/{shiftId:guid}/assignments/me"), EnableRateLimiting("assignments")]
    public async Task<ActionResult<AssignmentDto>> AssignMe(Guid shiftId, CancellationToken cancellationToken)
    {
        var assignment = await schedules.AssignMeAsync(shiftId, cancellationToken);
        return Created($"/api/shifts/{shiftId}/assignments/{assignment.Id}", assignment);
    }

    [HttpDelete("shifts/{shiftId:guid}/assignments/me"), EnableRateLimiting("assignments")]
    public async Task<IActionResult> RemoveMe(Guid shiftId, CancellationToken cancellationToken)
    {
        await schedules.RemoveMeAsync(shiftId, cancellationToken);
        return NoContent();
    }

    private static string Slug(string value) => string.Concat(value.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-')).Trim('-');
}


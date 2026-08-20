using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RaDuty.Application;

namespace RaDuty.Api.Controllers;

[ApiController, Route("api"), Authorize(Policy = "ResidentAssistantOrDirector")]
public sealed class SchedulesController(IScheduleService schedules, ISchedulePdfService pdfService,
    IUserService users) : ControllerBase
{
    [HttpGet("schedules/{year:int}/{month:int}")]
    public Task<ScheduleDto> Get(int year, int month, CancellationToken cancellationToken) => schedules.GetAsync(year, month, cancellationToken);

    [HttpGet("schedules/{year:int}/{month:int}/summary")]
    public Task<ScheduleSummaryDto> GetSummary(int year, int month, CancellationToken cancellationToken) => schedules.GetSummaryAsync(year, month, cancellationToken);

    [HttpGet("schedules/{year:int}/{month:int}/pdf"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Pdf(int year, int month, CancellationToken cancellationToken)
    {
        var schedule = await schedules.GetAsync(year, month, cancellationToken);
        var counts = schedule.Shifts.SelectMany(x => x.Assignments)
            .GroupBy(x => x.UserId).ToDictionary(x => x.Key, x => x.Count());
        var directory = (await users.GetDirectoryAsync(null, cancellationToken))
            .Select(x => x with { ShiftCount = counts.GetValueOrDefault(x.Id) }).ToList();
        var bytes = pdfService.Render(schedule, directory, DateTimeOffset.UtcNow);
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

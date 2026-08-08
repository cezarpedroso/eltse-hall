using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RaDuty.Application;
using RaDuty.Domain;

namespace RaDuty.Api.Controllers;

[ApiController, Route("api/admin"), Authorize(Policy = "HallDirectorOnly")]
public sealed class AdminController(IScheduleService schedules, IUserService users, IAccountService accounts) : ControllerBase
{
    [HttpPost("schedules/{year:int}/{month:int}/generate")]
    public async Task<ActionResult<ScheduleDto>> Generate(int year, int month, GenerateScheduleRequest request, CancellationToken cancellationToken)
    {
        var schedule = await schedules.GenerateAsync(year, month, request, cancellationToken);
        return Created($"/api/schedules/{year}/{month}", schedule);
    }

    [HttpPut("schedules/{schedulePeriodId:guid}")]
    public Task<ScheduleDto> Update(Guid schedulePeriodId, UpdateScheduleRequest request, CancellationToken cancellationToken) =>
        schedules.UpdateAsync(schedulePeriodId, request, cancellationToken);

    [HttpPost("schedules/{schedulePeriodId:guid}/open")]
    public Task<ScheduleDto> Open(Guid schedulePeriodId, CancellationToken cancellationToken) => Transition(schedulePeriodId, ScheduleStatus.OpenForSelection, cancellationToken);

    [HttpPost("schedules/{schedulePeriodId:guid}/close")]
    public Task<ScheduleDto> Close(Guid schedulePeriodId, CancellationToken cancellationToken) => Transition(schedulePeriodId, ScheduleStatus.Closed, cancellationToken);

    [HttpPost("schedules/{schedulePeriodId:guid}/draft")]
    public Task<ScheduleDto> Draft(Guid schedulePeriodId, CancellationToken cancellationToken) => Transition(schedulePeriodId, ScheduleStatus.Draft, cancellationToken);

    [HttpPost("schedules/{schedulePeriodId:guid}/publish")]
    public Task<ScheduleDto> Publish(Guid schedulePeriodId, CancellationToken cancellationToken) => Transition(schedulePeriodId, ScheduleStatus.Published, cancellationToken);

    [HttpPost("schedules/{schedulePeriodId:guid}/archive")]
    public Task<ScheduleDto> Archive(Guid schedulePeriodId, CancellationToken cancellationToken) => Transition(schedulePeriodId, ScheduleStatus.Archived, cancellationToken);

    [HttpGet("schedules/{schedulePeriodId:guid}/unfilled")]
    public Task<IReadOnlyList<ShiftDto>> Unfilled(Guid schedulePeriodId, CancellationToken cancellationToken) => schedules.GetUnfilledAsync(schedulePeriodId, cancellationToken);

    [HttpGet("schedules/{schedulePeriodId:guid}/distribution")]
    public Task<IReadOnlyList<DistributionDto>> Distribution(Guid schedulePeriodId, CancellationToken cancellationToken) => schedules.GetDistributionAsync(schedulePeriodId, cancellationToken);

    [HttpPost("shifts/{shiftId:guid}/assignments")]
    public async Task<ActionResult<AssignmentDto>> Assign(Guid shiftId, AdminAssignmentRequest request, CancellationToken cancellationToken)
    {
        var assignment = await schedules.AssignAsync(shiftId, request, cancellationToken);
        return Created($"/api/shifts/{shiftId}/assignments/{assignment.Id}", assignment);
    }

    [HttpDelete("shifts/{shiftId:guid}/assignments/{assignmentId:guid}")]
    public async Task<IActionResult> Remove(Guid shiftId, Guid assignmentId, CancellationToken cancellationToken)
    {
        await schedules.RemoveAssignmentAsync(shiftId, assignmentId, cancellationToken);
        return NoContent();
    }

    [HttpPut("shifts/{shiftId:guid}")]
    public Task<ShiftDto> UpdateShift(Guid shiftId, UpdateShiftRequest request, CancellationToken cancellationToken) =>
        schedules.UpdateShiftAsync(shiftId, request, cancellationToken);

    [HttpPut("users/{userId:guid}")]
    public Task<ResidentAssistantDto> UpdateUser(Guid userId, UpdateUserRequest request, CancellationToken cancellationToken) =>
        users.UpdateUserAsync(userId, request, cancellationToken);

    [HttpGet("users")]
    public Task<IReadOnlyList<ResidentAssistantDto>> GetUsers([FromQuery] string? search, CancellationToken cancellationToken) =>
        users.GetUsersAsync(search, cancellationToken);

    [HttpPost("users")]
    public async Task<ActionResult<ProvisionedAccountDto>> CreateUser(CreateStaffAccountRequest request,
        CancellationToken cancellationToken)
    {
        var account = await accounts.CreateAsync(request, cancellationToken);
        return Created($"/api/admin/users/{account.User.Id}", account);
    }

    [HttpPost("users/{userId:guid}/reset-password")]
    public Task<TemporaryPasswordDto> ResetPassword(Guid userId, CancellationToken cancellationToken) =>
        accounts.ResetPasswordAsync(userId, cancellationToken);

    [HttpGet("audit-logs")]
    public Task<PagedResult<AuditLogDto>> AuditLogs([FromQuery] string? action, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, CancellationToken cancellationToken = default) =>
        schedules.GetAuditLogsAsync(action, page, pageSize, cancellationToken);

    private Task<ScheduleDto> Transition(Guid id, ScheduleStatus status, CancellationToken cancellationToken) =>
        schedules.TransitionAsync(id, status, cancellationToken);
}

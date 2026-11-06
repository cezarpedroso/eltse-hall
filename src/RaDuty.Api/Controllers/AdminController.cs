using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RaDuty.Application;

namespace RaDuty.Api.Controllers;

[ApiController, Route("api/admin"), Authorize(Policy = "ResidentAssistantOrDirector")]
public sealed class AdminController(IScheduleService schedules, IUserService users, IAccountService accounts,
    IResidenceHallManagementService halls) : ControllerBase
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

    [HttpPut("users/{userId:guid}"), Authorize(Policy = "CampusAdministrator")]
    public Task<ResidentAssistantDto> UpdateUser(Guid userId, UpdateUserRequest request, CancellationToken cancellationToken) =>
        users.UpdateUserAsync(userId, request, cancellationToken);

    [HttpDelete("users/{userId:guid}"), Authorize(Policy = "CampusAdministrator")]
    public async Task<IActionResult> DeleteUser(Guid userId, CancellationToken cancellationToken)
    {
        await users.DeleteUserAsync(userId, cancellationToken);
        return NoContent();
    }

    [HttpGet("users")]
    public Task<IReadOnlyList<ResidentAssistantDto>> GetUsers([FromQuery] string? search, CancellationToken cancellationToken) =>
        users.GetUsersAsync(search, cancellationToken);

    [HttpPost("users"), Authorize(Policy = "CampusAdministrator")]
    public async Task<ActionResult<ProvisionedAccountDto>> CreateUser(CreateStaffAccountRequest request,
        CancellationToken cancellationToken)
    {
        var account = await accounts.CreateAsync(request, cancellationToken);
        return Created($"/api/admin/users/{account.User.Id}", account);
    }

    [HttpPost("users/{userId:guid}/reset-password"), Authorize(Policy = "CampusAdministrator")]
    public Task<TemporaryPasswordDto> ResetPassword(Guid userId, CancellationToken cancellationToken) =>
        accounts.ResetPasswordAsync(userId, cancellationToken);

    [HttpGet("audit-logs"), Authorize(Policy = "CampusAdministrator")]
    public Task<PagedResult<AuditLogDto>> AuditLogs([FromQuery] string? action, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, CancellationToken cancellationToken = default) =>
        schedules.GetAuditLogsAsync(action, page, pageSize, cancellationToken);

    [HttpGet("halls"), Authorize(Policy = "CampusAdministrator")]
    public Task<IReadOnlyList<ResidenceHallDto>> GetHalls(CancellationToken cancellationToken) => halls.GetAsync(cancellationToken);

    [HttpPost("halls"), Authorize(Policy = "CampusAdministrator")]
    public async Task<ActionResult<ResidenceHallDto>> CreateHall(CreateResidenceHallRequest request, CancellationToken cancellationToken)
    {
        var hall = await halls.CreateAsync(request, cancellationToken);
        return Created($"/api/admin/halls/{hall.Id}", hall);
    }

}

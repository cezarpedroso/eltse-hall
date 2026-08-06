using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RaDuty.Application;

namespace RaDuty.Api.Controllers;

[ApiController, Route("api/me"), Authorize(Policy = "ResidentAssistantOrDirector")]
public sealed class MeController(ICurrentUserService currentUserService, IUserService userService) : ControllerBase
{
    [HttpGet]
    public Task<CurrentUserDto> Get(CancellationToken cancellationToken) => currentUserService.GetAsync(cancellationToken);

    [HttpPut("profile")]
    public Task<CurrentUserDto> UpdateProfile(UpdateProfileRequest request, CancellationToken cancellationToken) =>
        userService.UpdateProfileAsync(request, cancellationToken);

    [HttpGet("shifts")]
    public Task<IReadOnlyList<ShiftDto>> GetShifts(CancellationToken cancellationToken) => userService.GetMyShiftsAsync(cancellationToken);
}


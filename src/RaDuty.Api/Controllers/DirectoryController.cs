using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RaDuty.Application;

namespace RaDuty.Api.Controllers;

[ApiController, Route("api/resident-assistants"), Authorize(Policy = "ResidentAssistantOrDirector")]
public sealed class DirectoryController(IUserService users) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<ResidentAssistantDto>> Get([FromQuery] string? search, CancellationToken cancellationToken) =>
        users.GetDirectoryAsync(search, cancellationToken);

    [HttpGet("{userId:guid}")]
    public Task<ResidentAssistantDto> GetOne(Guid userId, CancellationToken cancellationToken) =>
        users.GetResidentAssistantAsync(userId, cancellationToken);
}


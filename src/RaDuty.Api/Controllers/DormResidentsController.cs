using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RaDuty.Application;

namespace RaDuty.Api.Controllers;

[ApiController, Route("api/residents"), Authorize(Policy = "ResidentAssistantOrDirector")]
public sealed class DormResidentsController(IDormResidentManagementService residents) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<ManagedDormResidentDto>> Get([FromQuery] string? search, CancellationToken cancellationToken) =>
        residents.GetAsync(search, cancellationToken);

    [HttpGet("rooms")]
    public Task<IReadOnlyList<DormRoomOptionDto>> GetRooms(CancellationToken cancellationToken) =>
        residents.GetRoomsAsync(cancellationToken);

    [HttpPost]
    public async Task<ActionResult<ManagedDormResidentDto>> Create(SaveDormResidentRequest request, CancellationToken cancellationToken)
    {
        var resident = await residents.CreateAsync(request, cancellationToken);
        return Created($"/api/residents/{resident.Id}", resident);
    }

    [HttpPut("{residentId:guid}")]
    public Task<ManagedDormResidentDto> Update(Guid residentId, SaveDormResidentRequest request, CancellationToken cancellationToken) =>
        residents.UpdateAsync(residentId, request, cancellationToken);

    [HttpDelete("{residentId:guid}")]
    public async Task<IActionResult> Remove(Guid residentId, CancellationToken cancellationToken)
    {
        await residents.RemoveAsync(residentId, cancellationToken);
        return NoContent();
    }
}

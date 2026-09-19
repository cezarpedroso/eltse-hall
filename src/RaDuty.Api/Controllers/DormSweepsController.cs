using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RaDuty.Application;

namespace RaDuty.Api.Controllers;

[ApiController, Route("api/dorm-sweeps"), Authorize(Policy = "ResidentAssistantOrDirector")]
public sealed class DormSweepsController(IDormSweepService dormSweeps) : ControllerBase
{
    [HttpGet("suites")]
    public Task<IReadOnlyList<DormSweepSuiteDto>> GetSuites(CancellationToken cancellationToken) =>
        dormSweeps.GetSuitesAsync(cancellationToken);

    [HttpPost("suites/{suiteNumber}")]
    public async Task<ActionResult<DormSuiteSweepDto>> Submit(string suiteNumber,
        SubmitDormSuiteSweepRequest request, CancellationToken cancellationToken)
    {
        var sweep = await dormSweeps.SubmitAsync(suiteNumber, request, cancellationToken);
        return Created($"/api/dorm-sweeps/suites/{sweep.SuiteNumber}/sweeps/{sweep.Id}", sweep);
    }
}

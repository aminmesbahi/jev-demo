using JevHrTriage.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace JevHrTriage.Api.Controllers;

[ApiController]
[Route("api/demo")]
public sealed class DemoController : ControllerBase
{
    private readonly ISampleDataService _samples;

    public DemoController(ISampleDataService samples)
    {
        _samples = samples;
    }

    [HttpGet("samples")]
    [SwaggerOperation(
        Summary = "Get synthetic sample requests",
        Description = "Returns sample HR requests for straightforward routing, urgency, missing information, ambiguity, multi-topic detection, and review scenarios.")]
    public IActionResult GetSamples()
    {
        return Ok(_samples.GetSamples());
    }
}

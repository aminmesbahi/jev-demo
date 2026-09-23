using JevHrTriage.Api.Models;
using JevHrTriage.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace JevHrTriage.Api.Controllers;

[ApiController]
[Route("api/hr")]
public sealed class HrController : ControllerBase
{
    private readonly ITriageWorkflowService _workflow;

    public HrController(ITriageWorkflowService workflow)
    {
        _workflow = workflow;
    }

    [HttpPost("triage", Name = "TriageOneRequest")]
    [ProducesResponseType(typeof(TriageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status402PaymentRequired)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status504GatewayTimeout)]
    [SwaggerOperation(
        Summary = "Analyze one HR service request",
        Description = "Returns Jev judgments and the application's final recommendation for simulated queue routing and review status. Example request is available in GET /api/demo/samples and JevHrTriage.Api.http.")]
    public async Task<IActionResult> TriageAsync([FromBody] TriageRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _workflow.AnalyzeAsync(request, cancellationToken);
            return Ok(result);
        }
        catch (TriageException ex)
        {
            return Problem(
                title: ex.Title,
                detail: ex.Detail,
                statusCode: ex.StatusCode,
                type: $"https://httpstatuses.com/{ex.StatusCode}");
        }
    }
}

using Jobly.Core.Models;
using Microsoft.AspNetCore.Mvc;


namespace Jobly.Core.Controllers;

[Controller]
[ApiExplorerSettings(IgnoreApi = true)]
public class DetailsController : Controller
{
    private readonly IJoblyService _joblyService;

    public DetailsController(IJoblyService joblyService)
    {
        _joblyService = joblyService;
    }

    [HttpGet("details")]
    public async Task<IActionResult> Index(JobStateRequest request)
    {
        var model = await _joblyService.GetJobStates(request);

        return Ok(model);
    }
}

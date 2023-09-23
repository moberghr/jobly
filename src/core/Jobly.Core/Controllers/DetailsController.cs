using Jobly.Core.Models;
using Microsoft.AspNetCore.Mvc;


namespace Jobly.Core.Controllers;

[Controller]
[ApiExplorerSettings(IgnoreApi = true)]
public class DetailsController : Controller
{
    private readonly IJoblyService _handfireService;

    public DetailsController(IJoblyService handfireService)
    {
        _handfireService = handfireService;
    }

    [HttpGet("details")]
    public async Task<IActionResult> Index(JobStateRequest request)
    {
        var model = await _handfireService.GetJobStates(request);

        return Ok(model);
    }
}

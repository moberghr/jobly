using Microsoft.AspNetCore.Mvc;

namespace Handfire.Core.Controllers;

[Controller]
[ApiExplorerSettings(IgnoreApi = true)]
public class JobsController : Controller
{
    private readonly IHandfireService _handfireService;

    public JobsController(IHandfireService handfireService)
    {
        _handfireService = handfireService;
    }

    [HttpGet("created")]
    public async Task<IActionResult> Created(BaseListRequest request)
    {
        var model = await _handfireService.GetCreatedJobs(request);

        return View(model);
    }

    [HttpGet("completed")]
    public async Task<IActionResult> Completed(BaseListRequest request)
    {
        var model = await _handfireService.GetCompetedJobs(request);

        return View(model);
    }

    [HttpGet("failed")]
    public async Task<IActionResult> Failed(BaseListRequest request)
    {
        var model = await _handfireService.GetFailedJobs(request);

        return View(model);
    }
}

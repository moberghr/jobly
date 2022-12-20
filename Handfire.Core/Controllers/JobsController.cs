using Handfire.Core.Enums;
using Handfire.Core.Models;
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
        var model = await _handfireService.GetJobsList(request, State.Created);

        return View(model);
    }

    [HttpGet("completed")]
    public async Task<IActionResult> Completed(BaseListRequest request)
    {
        var model = await _handfireService.GetJobsList(request, State.Completed);

        return View(model);
    }

    [HttpGet("failed")]
    public async Task<IActionResult> Failed(BaseListRequest request)
    {
        var model = await _handfireService.GetJobsList(request, State.Failed);

        return View(model);
    }

    [HttpGet("retry")]
    public async Task<IActionResult> Retry(int jobId)
    {
        await _handfireService.SetRetry(jobId);

        var url = Request.Headers["Referer"].ToString();

        return Redirect(url);
    }
}

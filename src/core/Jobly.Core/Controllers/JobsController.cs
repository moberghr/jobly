using Jobly.Core.Enums;
using Jobly.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jobly.Core.Controllers;

[Controller]
[ApiExplorerSettings(IgnoreApi = true)]
public class JobsController : Controller
{
    private readonly IJoblyService _joblyService;

    public JobsController(IJoblyService joblyService)
    {
        _joblyService = joblyService;
    }

    [HttpGet("created")]
    public async Task<IActionResult> Created(BaseListRequest request)
    {
        var model = await _joblyService.GetJobsList(request, State.Enqueued);

        return Ok(model);
    }

    [HttpGet("completed")]
    public async Task<IActionResult> Completed(BaseListRequest request)
    {
        var model = await _joblyService.GetJobsList(request, State.Completed);

        return Ok(model);
    }

    [HttpGet("failed")]
    public async Task<IActionResult> Failed(BaseListRequest request)
    {
        var model = await _joblyService.GetJobsList(request, State.Failed);

        return Ok(model);
    }
    [HttpGet("processing")]
    public async Task<IActionResult> Processing(BaseListRequest request)
    {
        var model = await _joblyService.GetJobStatesInProcess(request);
        return View(model);
    }

    [HttpGet("retry")]
    public async Task<IActionResult> Retry(string jobId)
    {
        await _joblyService.SetRetry(jobId);

        var url = Request.GetTypedHeaders().Referer!.ToString();

        return Redirect(url);
    }
}

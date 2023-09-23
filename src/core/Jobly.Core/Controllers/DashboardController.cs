using Jobly.Core.Enums;
using Jobly.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace Jobly.Core.Controllers;

[Controller]
[ApiExplorerSettings(IgnoreApi = true)]
public class DashboardController : Controller
{
    private readonly IJoblyService _joblyService;

    public DashboardController(IJoblyService joblyService)
    {
        _joblyService = joblyService;
    }

    [HttpGet("status")]
    public async Task<IActionResult> Status()
    {
        var total = await _joblyService.GetTotalJobsCount();
        var pending = await _joblyService.GetPendingJobsCount();
        var scheduled = await _joblyService.GetScheduledJobsCount();
        var created = await _joblyService.GetJobsCount(State.Enqueued);
        var completed = await _joblyService.GetJobsCount(State.Completed);
        var failed = await _joblyService.GetJobsCount(State.Failed);
        var processing = await _joblyService.CountProcessingJobs() - completed - failed;

        var model = new DashboardStatistics
        {
            Total = total,
            Pending = pending,
            Scheduled = scheduled,
            Created = created,
            Completed = completed,
            Failed = failed,
            Processing = processing,
        };

        return Ok(model);

    }
    [HttpGet("dashboard")]
    public IActionResult Index()
    {
        return View();
    }
}

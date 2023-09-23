using Jobly.Core.Enums;
using Jobly.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace Jobly.Core.Controllers;

[Controller]
[ApiExplorerSettings(IgnoreApi = true)]
public class DashboardController : Controller
{
    private readonly IJoblyService _handfireService;

    public DashboardController(IJoblyService handfireService)
    {
        _handfireService = handfireService;
    }

    [HttpGet("status")]
    public async Task<IActionResult> Status()
    {
        var total = await _handfireService.GetTotalJobsCount();
        var pending = await _handfireService.GetPendingJobsCount();
        var scheduled = await _handfireService.GetScheduledJobsCount();
        var created = await _handfireService.GetJobsCount(State.Enqueued);
        var completed = await _handfireService.GetJobsCount(State.Completed);
        var failed = await _handfireService.GetJobsCount(State.Failed);
        var processing = await _handfireService.CountProcessingJobs() - completed - failed;

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

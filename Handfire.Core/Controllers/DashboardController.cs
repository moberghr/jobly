using Handfire.Core.Enums;
using Handfire.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace Handfire.Core.Controllers;

[Controller]
[ApiExplorerSettings(IgnoreApi = true)]
public class DashboardController : Controller
{
    private readonly IHandfireService _handfireService;

    public DashboardController(IHandfireService handfireService)
    {
        _handfireService = handfireService;
    }

    [HttpGet("dashboard")]
    public async Task<IActionResult> Index()
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

        return View(model);
    }
}

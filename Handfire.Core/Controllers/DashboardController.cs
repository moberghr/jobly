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
        var total = await _handfireService.GetTotalJobs();
        var pending = await _handfireService.GetPendingJobs();

        var model = new DashboardStatistics
        {
            Total = total,
            Pending = pending
        };

        return View(model);
    }
}

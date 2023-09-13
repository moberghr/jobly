using Handfire.Core.Models;
using Microsoft.AspNetCore.Mvc;


namespace Handfire.Core.Controllers;

[Controller]
[ApiExplorerSettings(IgnoreApi = true)]
public class DetailsController : Controller
{
    private readonly IHandfireService _handfireService;

    public DetailsController(IHandfireService handfireService)
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

using Microsoft.AspNetCore.Mvc;

namespace Handfire.Core.Controllers;

[Controller]
[ApiExplorerSettings(IgnoreApi = true)]
public class BatchesController : Controller
{
    private readonly IHandfireService _handfireService;

    public BatchesController(IHandfireService handfireService)
    {
        _handfireService = handfireService;
    }

    [HttpGet("batches")]
    public async Task<IActionResult> Index(BaseListRequest request)
    {
        var model = await _handfireService.GetBatchList(request);

        return View(model);
    }
}

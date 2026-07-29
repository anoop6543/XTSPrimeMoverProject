using Microsoft.AspNetCore.Mvc;
using PrimeMoverService.TrackEngine;

namespace PrimeMoverService.Controllers;

[ApiController]
[Route("api/track")]
public class TrackController : ControllerBase
{
    private readonly XtsTrackEngine _engine;

    public TrackController(XtsTrackEngine engine) => _engine = engine;

    [HttpPost("tick")]
    public IActionResult Tick([FromQuery] double speedFactor = 1.0)
    {
        var result = _engine.Tick(0.1 * speedFactor);
        return Ok(result);
    }

    [HttpPost("start")]
    public IActionResult Start() { _engine.Start(); return Ok(); }

    [HttpPost("stop")]
    public IActionResult Stop() { _engine.Stop(); return Ok(); }

    [HttpPost("set-speed")]
    public IActionResult SetSpeed([FromQuery] double factor) { _engine.SetSpeed(factor); return Ok(); }

    [HttpGet("status")]
    public IActionResult GetStatus() => Ok(new
    {
        IsRunning = _engine.IsRunning,
        TotalParts = _engine.TotalParts,
        GoodParts = _engine.GoodParts,
        BadParts = _engine.BadParts,
        Entered = _engine.Entered
    });
}

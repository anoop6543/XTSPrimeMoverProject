using Microsoft.AspNetCore.Mvc;
using WorkforceService.Services;
using WorkforceService.Domain;

namespace WorkforceService.Controllers;

[ApiController]
[Route("api/employees")]
public class EmployeesController : ControllerBase
{
    private readonly EmployeeService _service;

    public EmployeesController(EmployeeService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] bool? activeOnly) =>
        Ok(await _service.GetAllAsync(activeOnly));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var e = await _service.GetByIdAsync(id);
        if (e == null) return NotFound();
        var certs = await _service.GetCertificationsAsync(id);
        return Ok(_service.ToDto(e, certs));
    }

    [HttpGet("badge/{badgeId}")]
    public async Task<IActionResult> GetByBadge(string badgeId)
    {
        var e = await _service.GetByBadgeIdAsync(badgeId);
        return e == null ? NotFound() : Ok(_service.ToDto(e));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateEmployeeRequest req)
    {
        var emp = await _service.CreateAsync(new Employee
        {
            FirstName = req.FirstName,
            LastName = req.LastName,
            Email = req.Email,
            Role = Enum.TryParse<EmployeeRole>(req.Role, true, out var r) ? r : EmployeeRole.Operator,
            Department = req.Department ?? string.Empty,
            BadgeId = req.BadgeId
        });
        return CreatedAtAction(nameof(GetById), new { id = emp.Id }, _service.ToDto(emp));
    }

    [HttpGet("{id:guid}/qualified/{machineType}")]
    public async Task<IActionResult> IsQualified(Guid id, string machineType) =>
        Ok(new { qualified = await _service.IsQualifiedForMachineAsync(id, machineType) });

    [HttpPost("{id:guid}/certifications")]
    public async Task<IActionResult> AddCertification(Guid id, [FromBody] AddCertificationRequest req)
    {
        await _service.AddCertificationAsync(new Certification
        {
            EmployeeId = id,
            CertificationType = req.CertificationType,
            MachineType = req.MachineType,
            IssuedAt = DateTime.UtcNow,
            ExpiresAt = req.ExpiresAt,
            IssuedBy = req.IssuedBy
        });
        return Ok(new { message = "Certification added." });
    }
}

[ApiController]
[Route("api/shifts")]
public class ShiftsController : ControllerBase
{
    private readonly ShiftService _service;

    public ShiftsController(ShiftService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> GetShifts([FromQuery] string? date)
    {
        var d = DateOnly.TryParse(date, out var parsed) ? parsed : DateOnly.FromDateTime(DateTime.Today);
        return Ok(await _service.GetShiftsAsync(d));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateShiftRequest req)
    {
        var shift = await _service.CreateShiftAsync(new Shift
        {
            ShiftName = req.ShiftName,
            StartTime = TimeSpan.Parse(req.StartTime),
            EndTime = TimeSpan.Parse(req.EndTime),
            Date = DateOnly.Parse(req.Date)
        });
        return Ok(shift);
    }

    [HttpPost("{shiftId:guid}/assign")]
    public async Task<IActionResult> Assign(Guid shiftId, [FromBody] AssignEmployeeRequest req)
    {
        await _service.AssignEmployeeAsync(new ShiftAssignment
        {
            ShiftId = shiftId,
            EmployeeId = req.EmployeeId,
            MachineId = req.MachineId,
            RoleOnShift = req.RoleOnShift
        });
        return Ok(new { message = "Employee assigned to shift." });
    }

    [HttpPost("{shiftId:guid}/clock-in/{employeeId:guid}")]
    public async Task<IActionResult> ClockIn(Guid shiftId, Guid employeeId)
    {
        await _service.ClockInAsync(shiftId, employeeId);
        return Ok(new { message = "Clocked in." });
    }

    [HttpPost("{shiftId:guid}/clock-out/{employeeId:guid}")]
    public async Task<IActionResult> ClockOut(Guid shiftId, Guid employeeId)
    {
        await _service.ClockOutAsync(shiftId, employeeId);
        return Ok(new { message = "Clocked out." });
    }

    [HttpGet("machine/{machineId:int}/operator-present")]
    public async Task<IActionResult> IsOperatorPresent(int machineId) =>
        Ok(new { present = await _service.IsOperatorLoggedInAtMachineAsync(machineId) });
}

[ApiController]
[Route("api/training")]
public class TrainingController : ControllerBase
{
    private readonly TrainingService _service;

    public TrainingController(TrainingService service) => _service = service;

    [HttpGet("{employeeId:guid}")]
    public async Task<IActionResult> GetForEmployee(Guid employeeId) =>
        Ok(await _service.GetForEmployeeAsync(employeeId));

    [HttpGet("expiring")]
    public async Task<IActionResult> GetExpiring([FromQuery] int days = 30) =>
        Ok(await _service.GetExpiringAsync(days));

    [HttpPost("{employeeId:guid}/complete")]
    public async Task<IActionResult> RecordCompletion(
        Guid employeeId, [FromBody] CompleteTrainingRequest req)
    {
        await _service.RecordCompletionAsync(employeeId, req.ModuleName, req.MachineType, req.Score);
        return Ok(new { message = "Training completion recorded." });
    }

    [HttpGet("{employeeId:guid}/qualified/{machineType}")]
    public async Task<IActionResult> IsQualified(Guid employeeId, string machineType) =>
        Ok(new { qualified = await _service.HasCompletedRequiredTrainingAsync(employeeId, machineType) });
}

public record CreateEmployeeRequest(
    string FirstName, string LastName, string? Email,
    string Role, string? Department, string? BadgeId);
public record AddCertificationRequest(string CertificationType, string? MachineType, DateTime? ExpiresAt, string? IssuedBy);
public record CreateShiftRequest(string ShiftName, string StartTime, string EndTime, string Date);
public record AssignEmployeeRequest(Guid EmployeeId, int? MachineId, string? RoleOnShift);
public record CompleteTrainingRequest(string ModuleName, string MachineType, string Score);

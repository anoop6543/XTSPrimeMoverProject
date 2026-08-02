namespace XtsContracts.Dtos;

public record RobotDto(
    int RobotId,
    string Name,
    string State,
    int AssignedMachineId,
    double ActionProgress,
    string? HeldPartTrackingNumber
);

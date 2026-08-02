namespace XtsContracts.Dtos;

public record MoverDto(
    int MoverId,
    double Position,
    double Velocity,
    string State,
    string? LoadedPartTrackingNumber,
    int TargetStation
);

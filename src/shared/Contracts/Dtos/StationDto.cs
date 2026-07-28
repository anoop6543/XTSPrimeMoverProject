namespace XtsContracts.Dtos;

public record StationDto(
    int StationId,
    string Name,
    string Type,
    string Status,
    double ProcessTime,
    double ElapsedTime,
    string? CurrentPartTrackingNumber
);

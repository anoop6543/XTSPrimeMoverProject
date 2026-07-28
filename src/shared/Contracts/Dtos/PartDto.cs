namespace XtsContracts.Dtos;

public record PartDto(
    Guid PartId,
    string TrackingNumber,
    string Status,
    DateTime CreatedAt,
    bool HasDefect,
    int NextMachineIndex,
    int CompletedStations,
    string CurrentLocation,
    IReadOnlyList<string> ProcessHistory
);

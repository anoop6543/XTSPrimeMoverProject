namespace XtsContracts.Dtos;

public record MachineDto(
    int MachineId,
    string Name,
    string Type,
    string SequencerState,
    bool IsOperational,
    bool FaultActive,
    string FaultMessage,
    int PartsEnteredCount,
    int PartsExitedCount,
    IReadOnlyList<StationDto> Stations,
    int CurrentStationIndex,
    bool IsIndexing,
    double RotaryAngle
);

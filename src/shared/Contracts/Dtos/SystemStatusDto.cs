namespace XtsContracts.Dtos;

public record SystemStatusDto(
    bool IsRunning,
    int TotalPartsProduced,
    int GoodPartsCount,
    int BadPartsCount,
    int PrimeMoverEnteredCount,
    int PrimeMoverExitedCount,
    IReadOnlyList<MoverDto> Movers,
    IReadOnlyList<MachineDto> Machines,
    IReadOnlyList<RobotDto> Robots,
    DateTime Timestamp
);

public record RecipeDto(
    Guid RecipeId,
    string Name,
    IReadOnlyList<int> MachineOrder,
    DateTime CreatedAt
);

public record WatchdogEntryDto(
    string Key,
    string LastObject,
    string Message,
    int RecoveryCount,
    bool IsFaulted
);

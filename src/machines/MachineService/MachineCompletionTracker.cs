using System.Collections.Concurrent;

namespace MachineService;

public record CompletionRecord(bool IsComplete, bool IsFaulted, string FaultMessage, bool HasDefect, string FinalPartStatus, List<string> StationLog);

public class MachineCompletionTracker
{
    private readonly ConcurrentDictionary<string, CompletionRecord> _completions = new();

    public void SetCompletion(string trackingNumber, CompletionRecord record) =>
        _completions[trackingNumber] = record;

    public CompletionRecord? GetCompletion(string trackingNumber) =>
        _completions.TryGetValue(trackingNumber, out var r) ? r : null;

    public void Clear(string trackingNumber) =>
        _completions.TryRemove(trackingNumber, out _);
}

namespace XtsContracts;

/// <summary>
/// Sanitizes user-provided values before they are written to structured logs,
/// preventing log-injection via embedded newlines or control characters.
/// </summary>
public static class LogSanitizer
{
    /// <summary>Strips CR, LF, and TAB characters from a user-supplied string.</summary>
    public static string Sanitize(string? value) =>
        value == null ? string.Empty
        : value.Replace("\r", string.Empty)
                .Replace("\n", string.Empty)
                .Replace("\t", string.Empty);
}

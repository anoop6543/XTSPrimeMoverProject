using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace XTSPrimeMoverProject.Services
{
    public enum ErrorSeverity
    {
        Info,
        Warning,
        Error,
        Critical
    }

    public enum ErrorCategory
    {
        Database,
        Engine,
        Gateway,
        ViewModel,
        Unhandled,
        Configuration
    }

    public class ErrorRecord
    {
        public DateTime Timestamp { get; init; }
        public ErrorSeverity Severity { get; init; }
        public ErrorCategory Category { get; init; }
        public string Source { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
        public string? Detail { get; init; }
        public bool WasRecovered { get; init; }
    }

    public sealed class CircuitBreakerState
    {
        public string Name { get; init; } = string.Empty;
        public int ConsecutiveFailures { get; set; }
        public DateTime? LastFailureAt { get; set; }
        public DateTime? OpenedAt { get; set; }
        public bool IsOpen { get; set; }

        private const int FailureThreshold = 5;
        private static readonly TimeSpan CooldownPeriod = TimeSpan.FromSeconds(30);

        public bool ShouldTrip()
        {
            return ConsecutiveFailures >= FailureThreshold;
        }

        public void RecordFailure()
        {
            ConsecutiveFailures++;
            LastFailureAt = DateTime.UtcNow;

            if (ShouldTrip() && !IsOpen)
            {
                IsOpen = true;
                OpenedAt = DateTime.UtcNow;
            }
        }

        public void RecordSuccess()
        {
            ConsecutiveFailures = 0;
            IsOpen = false;
            OpenedAt = null;
        }

        public bool AllowAttempt()
        {
            if (!IsOpen)
            {
                return true;
            }

            if (OpenedAt.HasValue && DateTime.UtcNow - OpenedAt.Value > CooldownPeriod)
            {
                return true;
            }

            return false;
        }

        public void Reset()
        {
            ConsecutiveFailures = 0;
            LastFailureAt = null;
            OpenedAt = null;
            IsOpen = false;
        }
    }

    public sealed class ErrorHandlingService
    {
        private static readonly Lazy<ErrorHandlingService> _instance =
            new(() => new ErrorHandlingService());

        public static ErrorHandlingService Instance => _instance.Value;

        private readonly ConcurrentQueue<ErrorRecord> _errorLog = new();
        private readonly ConcurrentDictionary<string, CircuitBreakerState> _circuitBreakers = new();
        private readonly ConcurrentDictionary<string, DateTime> _throttleTracker = new();
        private const int MaxErrorLogSize = 500;
        private static readonly TimeSpan ThrottleWindow = TimeSpan.FromSeconds(2);

        public event EventHandler<ErrorRecord>? ErrorOccurred;

        private ErrorHandlingService() { }

        public void ReportError(
            ErrorSeverity severity,
            ErrorCategory category,
            string source,
            string message,
            string? detail = null,
            bool wasRecovered = false)
        {
            string throttleKey = $"{category}:{source}:{message}";
            if (_throttleTracker.TryGetValue(throttleKey, out DateTime lastReported)
                && DateTime.UtcNow - lastReported < ThrottleWindow)
            {
                return;
            }

            _throttleTracker[throttleKey] = DateTime.UtcNow;

            var record = new ErrorRecord
            {
                Timestamp = DateTime.UtcNow,
                Severity = severity,
                Category = category,
                Source = source,
                Message = message,
                Detail = detail,
                WasRecovered = wasRecovered
            };

            _errorLog.Enqueue(record);

            while (_errorLog.Count > MaxErrorLogSize)
            {
                _errorLog.TryDequeue(out _);
            }

            ErrorOccurred?.Invoke(this, record);
        }

        public void ReportException(
            ErrorCategory category,
            string source,
            Exception ex,
            bool wasRecovered = false)
        {
            ErrorSeverity severity = ClassifyException(ex);
            ReportError(severity, category, source, ex.Message, ex.ToString(), wasRecovered);
        }

        public bool ExecuteWithRetry(
            Action action,
            string operationName,
            ErrorCategory category,
            int maxRetries = 3,
            int baseDelayMs = 50)
        {
            var breaker = GetOrCreateCircuitBreaker(operationName);
            if (!breaker.AllowAttempt())
            {
                ReportError(
                    ErrorSeverity.Warning,
                    category,
                    operationName,
                    $"Circuit breaker open for '{operationName}'. Skipping execution.",
                    wasRecovered: false);
                return false;
            }

            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                try
                {
                    action();
                    breaker.RecordSuccess();
                    return true;
                }
                catch (Exception ex) when (attempt < maxRetries && IsTransient(ex))
                {
                    int delay = baseDelayMs * (int)Math.Pow(2, attempt);
                    System.Threading.Thread.Sleep(delay);
                    ReportError(
                        ErrorSeverity.Warning,
                        category,
                        operationName,
                        $"Retry {attempt + 1}/{maxRetries}: {ex.Message}",
                        wasRecovered: true);
                }
                catch (Exception ex)
                {
                    breaker.RecordFailure();
                    ReportException(category, operationName, ex, wasRecovered: false);
                    return false;
                }
            }

            return false;
        }

        public T? ExecuteWithRetry<T>(
            Func<T> func,
            string operationName,
            ErrorCategory category,
            T? fallback = default,
            int maxRetries = 3,
            int baseDelayMs = 50)
        {
            var breaker = GetOrCreateCircuitBreaker(operationName);
            if (!breaker.AllowAttempt())
            {
                ReportError(
                    ErrorSeverity.Warning,
                    category,
                    operationName,
                    $"Circuit breaker open for '{operationName}'. Returning fallback.",
                    wasRecovered: false);
                return fallback;
            }

            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                try
                {
                    T result = func();
                    breaker.RecordSuccess();
                    return result;
                }
                catch (Exception ex) when (attempt < maxRetries && IsTransient(ex))
                {
                    int delay = baseDelayMs * (int)Math.Pow(2, attempt);
                    System.Threading.Thread.Sleep(delay);
                    ReportError(
                        ErrorSeverity.Warning,
                        category,
                        operationName,
                        $"Retry {attempt + 1}/{maxRetries}: {ex.Message}",
                        wasRecovered: true);
                }
                catch (Exception ex)
                {
                    breaker.RecordFailure();
                    ReportException(category, operationName, ex, wasRecovered: false);
                    return fallback;
                }
            }

            return fallback;
        }

        public IReadOnlyList<ErrorRecord> GetRecentErrors(int count = 50)
        {
            return _errorLog
                .Reverse()
                .Take(count)
                .ToList();
        }

        public IReadOnlyList<ErrorRecord> GetErrorsByCategory(ErrorCategory category)
        {
            return _errorLog
                .Where(e => e.Category == category)
                .OrderByDescending(e => e.Timestamp)
                .ToList();
        }

        public IReadOnlyDictionary<string, CircuitBreakerState> GetCircuitBreakerStates()
        {
            return _circuitBreakers.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }

        public void ResetCircuitBreaker(string name)
        {
            if (_circuitBreakers.TryGetValue(name, out var breaker))
            {
                breaker.Reset();
            }
        }

        public void ResetAllCircuitBreakers()
        {
            foreach (var breaker in _circuitBreakers.Values)
            {
                breaker.Reset();
            }
        }

        public void ClearErrorLog()
        {
            while (_errorLog.TryDequeue(out _)) { }
            _throttleTracker.Clear();
        }

        private CircuitBreakerState GetOrCreateCircuitBreaker(string name)
        {
            return _circuitBreakers.GetOrAdd(name, key => new CircuitBreakerState { Name = key });
        }

        private static ErrorSeverity ClassifyException(Exception ex)
        {
            return ex switch
            {
                OutOfMemoryException => ErrorSeverity.Critical,
                StackOverflowException => ErrorSeverity.Critical,
                AccessViolationException => ErrorSeverity.Critical,
                InvalidOperationException => ErrorSeverity.Error,
                ArgumentException => ErrorSeverity.Error,
                NullReferenceException => ErrorSeverity.Error,
                System.IO.IOException => ErrorSeverity.Warning,
                TimeoutException => ErrorSeverity.Warning,
                _ => ErrorSeverity.Error
            };
        }

        private static bool IsTransient(Exception ex)
        {
            return ex is System.IO.IOException
                || ex is TimeoutException
                || ex is Microsoft.Data.Sqlite.SqliteException sqlEx && IsTransientSqlite(sqlEx);
        }

        private static bool IsTransientSqlite(Microsoft.Data.Sqlite.SqliteException ex)
        {
            // SQLite error codes: 5 = SQLITE_BUSY, 6 = SQLITE_LOCKED
            return ex.SqliteErrorCode == 5 || ex.SqliteErrorCode == 6;
        }
    }
}

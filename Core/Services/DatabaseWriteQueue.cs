using System;
using System.Collections.Concurrent;
using System.Threading;

namespace XTSPrimeMoverProject.Services
{
    public sealed class DatabaseWriteQueue : IDisposable
    {
        private readonly BlockingCollection<Action> _queue = new(boundedCapacity: 2048);
        private readonly Thread _consumerThread;
        private volatile bool _disposed;

        public int PendingCount => _queue.Count;

        public DatabaseWriteQueue()
        {
            _consumerThread = new Thread(ConsumeLoop)
            {
                Name = "DB-WriteQueue",
                IsBackground = true,
                Priority = ThreadPriority.BelowNormal
            };
            _consumerThread.Start();
        }

        public void Enqueue(Action writeOperation)
        {
            if (_disposed)
            {
                return;
            }

            if (!_queue.TryAdd(writeOperation, millisecondsTimeout: 100))
            {
                System.Diagnostics.Debug.WriteLine("[DatabaseWriteQueue] Queue full, dropping write operation.");
            }
        }

        private void ConsumeLoop()
        {
            try
            {
                foreach (var operation in _queue.GetConsumingEnumerable())
                {
                    try
                    {
                        operation();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[DatabaseWriteQueue] Write failed: {ex.Message}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _queue.CompleteAdding();

            if (!_consumerThread.Join(TimeSpan.FromSeconds(5)))
            {
                System.Diagnostics.Debug.WriteLine("[DatabaseWriteQueue] Consumer thread did not exit in time.");
            }

            _queue.Dispose();
        }
    }
}

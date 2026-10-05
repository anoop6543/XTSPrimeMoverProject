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

            try
            {
                if (!_queue.TryAdd(writeOperation, millisecondsTimeout: 100))
                {
                    System.Diagnostics.Debug.WriteLine("[DatabaseWriteQueue] Queue full, dropping write operation.");
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
            {
                // Shutdown raced with this write (adding completed or queue disposed): drop it.
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
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or InvalidOperationException)
            {
                // Normal shutdown – never let a background-thread exception take the process down.
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
                // Still draining a backlog: leave the collection alive for the (background) consumer
                // instead of disposing it underneath it, which used to crash the process on shutdown.
                System.Diagnostics.Debug.WriteLine("[DatabaseWriteQueue] Consumer thread did not exit in time; leaving it to finish in the background.");
                return;
            }

            _queue.Dispose();
        }
    }
}
